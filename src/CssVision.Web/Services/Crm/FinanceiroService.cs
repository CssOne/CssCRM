using CssVision.Web.Authorization;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Controle financeiro do consultor: vendas e recebimentos, custos, metas e reserva. Regras de acesso: tudo é do usuário logado
/// (o id vem dos claims, nunca da requisição) e um registro de outra pessoa responde "não encontrado", como se não existisse.
/// </summary>
public interface IFinanceiroService
{
    /// <param name="consultor">Vazio = o próprio usuário; um id = esse consultor; "equipe" = todos os que quem consulta enxerga (só para quem gere o comercial).</param>
    Task<FinanceiroAnoDto> ObterAnoAsync(int? ano, CancellationToken ct) => ObterAnoAsync(ano, null, ct);
    Task<FinanceiroAnoDto> ObterAnoAsync(int? ano, string? consultor, CancellationToken ct);

    /// <summary>Consultores (papel Comercial) que quem consulta enxerga, para escolher de quem ver os números. Administradores não aparecem.</summary>
    Task<IReadOnlyList<ConsultorFinanceiroDto>> ListarConsultoresAsync(CancellationToken ct);
    Task SalvarConfigAsync(int ano, FinanceiroConfigDto config, CancellationToken ct);
    Task SalvarMetaAsync(int ano, FinanceiroMetaDto meta, CancellationToken ct);
    Task<FinanceiroVendaDto> SalvarVendaAsync(Guid? id, FinanceiroVendaDto venda, CancellationToken ct);
    Task ExcluirVendaAsync(Guid id, CancellationToken ct);
    Task<FinanceiroCustoDto> SalvarCustoAsync(Guid? id, FinanceiroCustoDto custo, CancellationToken ct);
    Task ExcluirCustoAsync(Guid id, CancellationToken ct);
}

public sealed class FinanceiroService(ApplicationDbContext db, ICurrentUserService currentUser, IAuditSink auditoria, IEquipeComercialService? equipe = null) : IFinanceiroService
{
    private static readonly TimeSpan JanelaDuplicidade = TimeSpan.FromSeconds(10);

    private Guid Eu => currentUser.UserId;

    public Task<FinanceiroAnoDto> ObterAnoAsync(int? ano, CancellationToken ct) => ObterAnoAsync(ano, null, ct);

    public async Task<FinanceiroAnoDto> ObterAnoAsync(int? ano, string? consultor, CancellationToken ct)
    {
        var a = FinanceiroValidador.ValidarAno(ano ?? HorarioBrasilia.Hoje.Year);
        if (string.IsNullOrWhiteSpace(consultor) || (Guid.TryParse(consultor, out var eu) && eu == Eu))
        {
            return (await MontarAnoAsync(Eu, a, ct)) with { Visao = new FinanceiroVisaoDto("propria", Eu, null, false, 1) };
        }

        // Ver os números de outras pessoas é da gestão comercial, dentro do escopo que cada um já tem no Relatório comercial
        // (administrador/gestor master/supervisor: todos; gestor regional: a equipe dele). Sempre só leitura.
        if (!currentUser.PodeGerirComercial || equipe is null)
            throw new CrmForbiddenException("Você não pode ver o controle financeiro de outros consultores.");

        if (string.Equals(consultor, "equipe", StringComparison.OrdinalIgnoreCase)) return await MontarEquipeAsync(a, ct);

        if (!Guid.TryParse(consultor, out var alvo)) throw new CrmBusinessException("Consultor inválido.", "financeiro_invalido");
        if (!await equipe.PodeAcessarVendedorAsync(alvo, ct)) throw new CrmForbiddenException("Você não pode ver este consultor.");
        var nome = await db.Users.AsNoTracking().Where(u => u.Id == alvo).Select(u => u.NomeCompleto).FirstOrDefaultAsync(ct)
            ?? throw new CrmNotFoundException("Consultor", alvo);
        var dto = await MontarAnoAsync(alvo, a, ct);
        return dto with
        {
            Vendas = dto.Vendas.Select(v => v with { Consultor = nome, ConsultorId = alvo }).ToList(),
            Custos = dto.Custos.Select(c => c with { Consultor = nome, ConsultorId = alvo }).ToList(),
            Rendas = (dto.Rendas ?? []).Select(r => r with { Consultor = nome, ConsultorId = alvo }).ToList(),
            Visao = new FinanceiroVisaoDto("consultor", alvo, nome, true, 1),
        };
    }

    public async Task<IReadOnlyList<ConsultorFinanceiroDto>> ListarConsultoresAsync(CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial || equipe is null)
            throw new CrmForbiddenException("Você não pode ver o controle financeiro de outros consultores.");
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct, ignorarRegionaisOcultas: false);
        return await db.Users.AsNoTracking()
            .Where(u => visiveis == null || visiveis.Contains(u.Id))
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Comercial)))
            .Where(u => !u.NomeCompleto.StartsWith(Notion.NotionPageExtensions.PrefixoNomeProvisorio))
            .OrderBy(u => u.NomeCompleto)
            .Select(u => new ConsultorFinanceiroDto(u.Id, u.NomeCompleto, u.Ativo))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Todos os consultores visíveis somados: vendas e custos de cada um (com o nome), metas somadas mês a mês e caixa inicial somado.
    /// A reserva para investimento é pessoal de cada consultor, então a visão da equipe não a calcula.
    /// </summary>
    private async Task<FinanceiroAnoDto> MontarEquipeAsync(int a, CancellationToken ct)
    {
        var visiveis = await equipe!.ObterVendedoresVisiveisAsync(ct);
        var candidatos = await db.Users.AsNoTracking()
            .Where(u => visiveis == null || visiveis.Contains(u.Id))
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Comercial)))
            .Select(u => new { u.Id, u.NomeCompleto })
            .OrderBy(u => u.NomeCompleto)
            .ToListAsync(ct);

        var vendas = new List<FinanceiroVendaDto>();
        var custos = new List<FinanceiroCustoDto>();
        var rendas = new List<FinanceiroRendaDto>();
        var metas = new Dictionary<int, FinanceiroMetaDto>();
        var caixaInicial = 0m;
        foreach (var c in candidatos)
        {
            var d = await MontarAnoAsync(c.Id, a, ct);
            vendas.AddRange(d.Vendas.Select(v => v with { Consultor = c.NomeCompleto, ConsultorId = c.Id }));
            custos.AddRange(d.Custos.Select(x => x with { Consultor = c.NomeCompleto, ConsultorId = c.Id }));
            caixaInicial += d.Config.CaixaInicial;
            rendas.AddRange((d.Rendas ?? []).Select(r => r with { Consultor = c.NomeCompleto, ConsultorId = c.Id }));
            foreach (var m in d.Metas)
            {
                metas[m.Mes] = metas.TryGetValue(m.Mes, out var soma)
                    ? soma with { MetaAdesao = soma.MetaAdesao + m.MetaAdesao, MetaBonus = soma.MetaBonus + m.MetaBonus, MetaRecorrencia = soma.MetaRecorrencia + m.MetaRecorrencia, MetaVendas = soma.MetaVendas + m.MetaVendas }
                    : m with { Observacoes = null, DoCrm = true };
            }
        }

        return new FinanceiroAnoDto(
            a,
            new FinanceiroConfigDto(0.10m, caixaInicial),
            vendas.OrderBy(v => v.DataVenda).ThenBy(v => v.Cliente).ToList(),
            custos.OrderBy(x => x.Data).ThenBy(x => x.Categoria).ToList(),
            metas.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList(),
            new FinanceiroVisaoDto("equipe", null, null, true, candidatos.Count),
            Rendas: rendas);
    }

    /// <summary>Monta o ano de UM usuário: o que ele lançou, o que veio do quadro de leads e as metas.</summary>
    private async Task<FinanceiroAnoDto> MontarAnoAsync(Guid eu, int a, CancellationToken ct)
    {
        var de = new DateOnly(a, 1, 1);
        var ate = new DateOnly(a + 1, 1, 1);

        var config = await db.CrmFinanceiroConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == eu && c.Ano == a, ct);
        var vendasSalvas = await db.CrmFinanceiroVendas.AsNoTracking()
            .Where(v => v.UsuarioId == eu && (v.OportunidadeId != null || (v.DataVenda >= de && v.DataVenda < ate)))
            .ToListAsync(ct);
        var custos = (await db.CrmFinanceiroCustos.AsNoTracking()
            .Where(c => c.UsuarioId == eu && c.Data >= de && c.Data < ate)
            .ToListAsync(ct)).Select(ParaDto).ToList();
        var metas = await db.CrmFinanceiroMetas.AsNoTracking().Where(m => m.UsuarioId == eu && m.Ano == a).ToListAsync(ct);

        // Vendas e custos que o consultor já lançou no quadro de leads entram aqui sozinhos (oportunidades ganhas dele no ano).
        var ganhas = await db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.ResponsavelId == eu && o.Etapa.Tipo == TipoEtapaPipeline.Ganho
                        && o.DataEfetivaFechamento >= HorarioBrasilia.Inicio(de) && o.DataEfetivaFechamento < HorarioBrasilia.Inicio(ate))
            .Select(o => new
            {
                o.Id, Cliente = o.Lead.NomeOuRazaoSocial, o.DataEfetivaFechamento, o.ProdutoOuServico, o.PagamentoAdesao,
                o.PagamentoAdesaoArquivoUrl, o.DataPagamentoAdesaoPrevista, o.Indicacao, o.ValorIndicacao, o.ComprovanteIndicacaoArquivoUrl,
                Rastreador = o.Veiculo != null ? o.Veiculo.Rastreador : null,
                AdesaoDoLead = o.Lead.ValorAdesao,
            })
            .ToListAsync(ct);

        var vendas = new List<FinanceiroVendaDto>();
        foreach (var o in ganhas)
        {
            var dia = HorarioBrasilia.Dia(o.DataEfetivaFechamento!.Value);
            var salva = vendasSalvas.FirstOrDefault(v => v.OportunidadeId == o.Id);
            // Adesão sem comprovante e com data combinada ainda não foi paga; nos demais casos o card já conta como recebida.
            var pendente = o.PagamentoAdesaoArquivoUrl is null && o.DataPagamentoAdesaoPrevista is not null;
            // Fechada sem pagar a adesão no dia (ficou combinada para outra data): o "valor pago" do fechamento costuma ficar em 0,00 — o que está para receber é o valor da adesão
            // que o consultor informou na cotação. Sem isso a venda apareceria "sem valor" e fora de "Com pendência"/"Em atraso".
            var valorAdesao = pendente && !(o.PagamentoAdesao > 0) && o.AdesaoDoLead is > 0 ? o.AdesaoDoLead.Value : o.PagamentoAdesao ?? 0m;
            vendas.Add(new FinanceiroVendaDto(
                salva?.Id, o.Id, true, dia, o.Cliente, string.IsNullOrWhiteSpace(o.ProdutoOuServico) ? "Adesão" : o.ProdutoOuServico!,
                valorAdesao, salva?.ValorBonus ?? 0m, salva?.Premiacoes ?? 0m,
                salva?.DataPrevistaAdesao ?? o.DataPagamentoAdesaoPrevista,
                salva?.StatusAdesao ?? (pendente ? FinanceiroStatus.Pendente : FinanceiroStatus.Recebido),
                salva?.DataPrevistaBonus, salva?.StatusBonus ?? FinanceiroStatus.Pendente, salva?.Observacoes, salva?.RowVersion, BonusRecebido: salva?.BonusRecebido, TaxaRastreador: o.Rastreador));

            if (o.Rastreador is > 0)
                custos.Add(new FinanceiroCustoDto(null, true, dia, "Rastreador", o.Cliente, o.Rastreador.Value, dia, FinanceiroStatus.Pago, dia, false));
            if (o.Indicacao == true && o.ValorIndicacao is > 0)
                custos.Add(new FinanceiroCustoDto(null, true, dia, "Indicação", o.Cliente, o.ValorIndicacao.Value, dia, FinanceiroStatus.Pago, dia, o.ComprovanteIndicacaoArquivoUrl is not null));
        }
        vendas.AddRange(vendasSalvas.Where(v => v.OportunidadeId is null).Select(ParaDto));

        // Meta de valor (adesão) e de vendas já definidas pela gestão na aba Metas do CRM valem aqui — o consultor não redigita.
        var metasCrm = await db.CrmSalesGoals.AsNoTracking()
            .Where(g => g.VendedorId == eu && g.MesReferencia >= de && g.MesReferencia < ate).ToListAsync(ct);
        var metasMes = new List<FinanceiroMetaDto>();
        for (var mes = 1; mes <= 12; mes++)
        {
            var salva = metas.FirstOrDefault(m => m.Mes == mes);
            var crm = metasCrm.FirstOrDefault(g => g.MesReferencia.Month == mes);
            if (salva is null && crm is null) continue;
            metasMes.Add(new FinanceiroMetaDto(mes,
                crm?.MetaValor ?? salva?.MetaAdesao ?? 0m, salva?.MetaBonus ?? 0m, salva?.MetaRecorrencia ?? 0m,
                crm?.MetaQuantidadeVendas ?? salva?.MetaVendas ?? 0, salva?.Observacoes, crm is not null));
        }

        // Contrato definido pela gestão (PJ/CLT): bônus por placa, % da adesão do CLT, salário, recorrência e custos fixos do PJ.
        // O contrato vale mês a mês: o do próprio consultor ou o do grupo em que ele estava (quem nunca esteve em grupo segue só o dele, como sempre).
        var efetivo = await ContratoEfetivoCarregador.CarregarAsync(db, eu, ct);
        var perfil = efetivo.PerfilDoAno(a, HorarioBrasilia.Hoje);
        var rendas = new List<FinanceiroRendaDto>();
        if (perfil?.Tipo is { } tipo)
        {
            var regras = await RegrasContratoCarregador.CarregarAsync(db, eu, ct);
            var meses = ContratoFinanceiroCalculo.Meses(efetivo, a, HorarioBrasilia.Hoje, regras);
            var hojeMes = ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Hoje);
            // Coworking, custo de lead e tráfego pago são cobrados pela CSS: ficam "A pagar" no controle até o Setor Financeiro marcar a cobrança como paga.
            var pagamentosCsS = await db.CrmCobrancasPagamento.AsNoTracking().Where(p => p.UsuarioId == eu && p.CustoId == null).ToListAsync(ct);
            // CLT: a porcentagem do mês vem da faixa de contratos fechados (vendas não canceladas) e vale para todas as vendas do mês.
            var percentualClt = vendas.Where(v => v.StatusAdesao != FinanceiroStatus.Cancelado).GroupBy(v => v.DataVenda.Month)
                .ToDictionary(g => g.Key, g => ContratoFinanceiroCalculo.PercentualBonificacaoClt(g.Count()));
            // O tipo de contrato é o do mês da venda (quem mudou de grupo no ano pode ter sido PJ num mês e CLT em outro).
            vendas = vendas.Select(v => efetivo.PerfilNoMes(new DateOnly(a, v.DataVenda.Month, 1))?.Tipo is { } tipoDoMes
                ? AplicarContratoNaVenda(v, tipoDoMes, meses[v.DataVenda.Month - 1], percentualClt.GetValueOrDefault(v.DataVenda.Month), hojeMes)
                : v).ToList();
            foreach (var m in meses)
            {
                if (m.Salario > 0) rendas.Add(new FinanceiroRendaDto(m.Mes, "Salário", m.Salario));
                if (m.Alimentacao > 0) rendas.Add(new FinanceiroRendaDto(m.Mes, "Alimentação", m.Alimentacao));
                if (m.Passagem > 0) rendas.Add(new FinanceiroRendaDto(m.Mes, "Passagem", m.Passagem));
                // Recorrência é paga 2 meses depois do mês a que se refere: só entra na renda do mês do pagamento (e só quando ele já chegou).
                if (m.Recorrencia > 0 && ContratoFinanceiroCalculo.MesDoPagamento(new DateOnly(a, m.Mes, 1)) is { } pagaRec && pagaRec.Year == a && pagaRec <= hojeMes)
                    rendas.Add(new FinanceiroRendaDto(pagaRec.Month, "Recorrência", m.Recorrencia));
                var dia = new DateOnly(a, m.Mes, 1);
                foreach (var f in m.Custos)
                {
                    ItemCobranca? cobrado = f.Chave switch { "Coworking" => ItemCobranca.Coworking, "CustoLead" => ItemCobranca.CustoLead, "TrafegoPago" => ItemCobranca.TrafegoPago, _ => null };
                    var registro = cobrado is { } item ? pagamentosCsS.FirstOrDefault(p => p.Item == item && p.Competencia == dia) : null;
                    var pago = cobrado is null || registro?.Pago == true;
                    custos.Add(new FinanceiroCustoDto(null, false, dia, f.Categoria, f.Descricao, f.Valor, null, pago ? FinanceiroStatus.Pago : FinanceiroStatus.APagar,
                        pago ? (cobrado is null ? dia : registro!.PagoEm ?? dia) : null, false, Fixo: true));
                }
            }
            if (tipo == TipoContrato.Pj) rendas.AddRange(await PagamentosDoAnoAnteriorAsync(eu, a, perfil, efetivo, regras, hojeMes, ct));
        }

        // Sem contrato definido: coworking, tráfego pago e custo de lead chegam sozinhos a cada mês (valores padrão da gestão), a pagar até o Setor Financeiro dar baixa.
        if (perfil?.Tipo is null && await CobrancasAutomaticas.CarregarAsync(db, eu, ct) is { } automatica)
        {
            var hojeAuto = HorarioBrasilia.Hoje;
            var mesAtualAuto = ContratoFinanceiroCalculo.PrimeiroDia(hojeAuto);
            var pagamentosAuto = await db.CrmCobrancasPagamento.AsNoTracking().Where(p => p.UsuarioId == eu && p.CustoId == null).ToListAsync(ct);
            foreach (var c in CobrancasCalculo.Listar(automatica.Perfil, [], automatica.Regras, pagamentosAuto, [], hojeAuto).Where(c => c.Competencia.Year == a && c.Competencia <= mesAtualAuto))
            {
                var categoria = c.Item switch { "Coworking" => "Coworking", "TrafegoPago" => "Gestão de Tráfego", _ => "Custo de Leads" };
                custos.Add(new FinanceiroCustoDto(null, false, c.Competencia, categoria, c.Descricao, c.Valor, null, c.Pago ? FinanceiroStatus.Pago : FinanceiroStatus.APagar, c.PagoEm, false, Fixo: true));
            }
        }

        // Custos adicionais lançados pelo financeiro (notebook, celular...): cada parcela até o mês atual é um custo do mês, pago ou a pagar conforme o Setor Financeiro.
        var adicionais = await db.CrmCustosConsultor.AsNoTracking().Where(c => c.UsuarioId == eu).ToListAsync(ct);
        if (adicionais.Count > 0)
        {
            var pagamentosAdicionais = await db.CrmCobrancasPagamento.AsNoTracking().Where(p => p.UsuarioId == eu && p.CustoId != null).ToListAsync(ct);
            var mesAtualCusto = ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Hoje);
            foreach (var cobranca in CobrancasCalculo.Listar(null, [], ContratoFinanceiroCalculo.Regras.Nenhuma, pagamentosAdicionais, adicionais, HorarioBrasilia.Hoje).Where(c => c.Competencia.Year == a && c.Competencia <= mesAtualCusto))
            {
                var tipoCusto = adicionais.First(c => c.Id == cobranca.CustoId).Tipo;
                custos.Add(new FinanceiroCustoDto(null, false, cobranca.Competencia, tipoCusto == TipoCustoConsultor.Outro ? "Outros" : "Material de Trabalho", cobranca.Descricao, cobranca.Valor, null,
                    cobranca.Pago ? FinanceiroStatus.Pago : FinanceiroStatus.APagar, cobranca.PagoEm, false, Fixo: true));
            }
        }

        // CLT não tem custos fixos: o único desconto dele é o rastreador, que já abate a bonificação da venda.
        if (perfil?.Tipo == TipoContrato.Clt) custos.RemoveAll(c => c.Fixo);

        return new FinanceiroAnoDto(
            a,
            new FinanceiroConfigDto(config?.PercentualReserva ?? 0.10m, config?.CaixaInicial ?? 0m),
            vendas.OrderBy(v => v.DataVenda).ThenBy(v => v.Cliente).ToList(),
            custos.OrderBy(c => c.Data).ThenBy(c => c.Categoria).ToList(),
            metasMes,
            Rendas: rendas,
            TipoContrato: perfil?.Tipo?.ToString());
    }

    /// <summary>
    /// Venda do consultor com contrato definido: PJ recebe a adesão inteira e o bônus por placa vigente no mês da venda (não se digita mais);
    /// CLT recebe a bonificação por produtividade: a porcentagem da faixa do mês sobre o faturado líquido da venda (adesão menos a taxa de instalação do
    /// rastreador) e não tem bônus por placa. A adesão original do card fica em <c>AdesaoOriginal</c>.
    /// </summary>
    private static FinanceiroVendaDto AplicarContratoNaVenda(FinanceiroVendaDto v, TipoContrato tipo, FinanceiroMesContratoDto mes, decimal percentualClt, DateOnly hojeMes) =>
        tipo == TipoContrato.Pj
            ? ComPagamentoDoBonus(v, v.StatusAdesao == FinanceiroStatus.Cancelado ? 0m : mes.BonusPorPlaca, hojeMes)
            : v with
            {
                ValorBonus = 0m,
                ValorAdesao = FinanceiroValidador.Arredondar(ContratoFinanceiroCalculo.LiquidoDaVenda(v.ValorAdesao, v.TaxaRastreador) * percentualClt),
                AdesaoOriginal = v.ValorAdesao,
                BonusAutomatico = true,
            };

    /// <summary>O bônus por placa é pago 2 meses depois da venda (venda de janeiro = pago em março): fica com data prevista e, chegado o mês, recebido.</summary>
    private static FinanceiroVendaDto ComPagamentoDoBonus(FinanceiroVendaDto v, decimal bonus, DateOnly hojeMes)
    {
        // O bônus é só uma previsão (vale sobre o 1º boleto pago do associado, que pode não ser pago): fica pendente, com data prevista no fim do mês do
        // pagamento (ou a que o consultor ajustou), até ele informar quanto realmente recebeu — aí vale o valor informado, mesmo que seja zero.
        var cancelada = v.StatusAdesao == FinanceiroStatus.Cancelado;
        var prevista = v.DataPrevistaBonus ?? ContratoFinanceiroCalculo.MesDoPagamento(v.DataVenda).AddMonths(1).AddDays(-1);
        var informado = !cancelada && v.BonusRecebido is not null;
        return v with
        {
            BonusAutomatico = true,
            BonusPrevisto = bonus,
            DataPrevistaBonus = prevista,
            ValorBonus = informado ? v.BonusRecebido!.Value : bonus,
            StatusBonus = cancelada ? FinanceiroStatus.Cancelado : informado ? FinanceiroStatus.Recebido : FinanceiroStatus.Pendente,
        };
    }

    /// <summary>
    /// Bônus e recorrência de novembro e dezembro do ano anterior só são pagos em janeiro e fevereiro deste ano: entram como renda do mês do pagamento
    /// (já que as vendas e os lançamentos deles são do ano anterior).
    /// </summary>
    private async Task<List<FinanceiroRendaDto>> PagamentosDoAnoAnteriorAsync(
        Guid eu, int a, CrmFinanceiroPerfil perfil, ContratoEfetivo efetivo, ContratoFinanceiroCalculo.Regras regras, DateOnly hojeMes, CancellationToken ct)
    {
        var rendas = new List<FinanceiroRendaDto>();
        var anterior = ContratoFinanceiroCalculo.Meses(efetivo, a - 1, HorarioBrasilia.Hoje, regras);
        if (anterior.Count == 0) return rendas;

        foreach (var m in anterior.Where(m => m.Recorrencia > 0))
        {
            var paga = ContratoFinanceiroCalculo.MesDoPagamento(new DateOnly(a - 1, m.Mes, 1));
            if (paga.Year == a && paga <= hojeMes) rendas.Add(new FinanceiroRendaDto(paga.Month, "Recorrência", m.Recorrencia));
        }

        if (perfil.ModeloPj == ModeloRemuneracaoPj.Recorrencia) return rendas;
        var de = HorarioBrasilia.Inicio(new DateOnly(a - 1, 11, 1));
        var ate = HorarioBrasilia.Inicio(new DateOnly(a, 1, 1));
        var vendas = await db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.ResponsavelId == eu && o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.DataEfetivaFechamento >= de && o.DataEfetivaFechamento < ate)
            .Select(o => new { o.Id, Data = o.DataEfetivaFechamento!.Value })
            .ToListAsync(ct);
        var ids = vendas.Select(v => v.Id).ToList();
        // O bônus é só uma previsão: só vira renda deste ano o que o consultor informou ter realmente recebido (visão do ano anterior).
        var informados = await db.CrmFinanceiroVendas.AsNoTracking()
            .Where(v => v.UsuarioId == eu && v.OportunidadeId != null && ids.Contains(v.OportunidadeId.Value) && v.BonusRecebido != null && v.StatusAdesao != FinanceiroStatus.Cancelado)
            .Select(v => new { Id = v.OportunidadeId!.Value, Valor = v.BonusRecebido!.Value })
            .ToListAsync(ct);
        foreach (var grupo in informados.Join(vendas, i => i.Id, v => v.Id, (i, v) => (Mes: HorarioBrasilia.Dia(v.Data).Month, i.Valor)).GroupBy(x => x.Mes))
        {
            var paga = ContratoFinanceiroCalculo.MesDoPagamento(new DateOnly(a - 1, grupo.Key, 1));
            var total = grupo.Sum(x => x.Valor);
            if (total > 0 && paga.Year == a) rendas.Add(new FinanceiroRendaDto(paga.Month, "Bônus", total));
        }
        return rendas;
    }

    public async Task SalvarConfigAsync(int ano, FinanceiroConfigDto dto, CancellationToken ct)
    {
        ano = FinanceiroValidador.ValidarAno(ano);
        FinanceiroValidador.Validar(dto);

        var c = await db.CrmFinanceiroConfigs.FirstOrDefaultAsync(x => x.UsuarioId == Eu && x.Ano == ano, ct);
        if (c is null) db.CrmFinanceiroConfigs.Add(c = new CrmFinanceiroConfig { UsuarioId = Eu, Ano = ano });
        c.PercentualReserva = Math.Round(dto.PercentualReserva, 4);
        c.CaixaInicial = FinanceiroValidador.Arredondar(dto.CaixaInicial);
        await SalvarAsync(ct);
        await auditoria.RegistrarAsync("financeiro.config.salva", nameof(CrmFinanceiroConfig), c.Id, new { ano, reserva = c.PercentualReserva }, ct);
    }

    public async Task SalvarMetaAsync(int ano, FinanceiroMetaDto dto, CancellationToken ct)
    {
        ano = FinanceiroValidador.ValidarAno(ano);
        FinanceiroValidador.Validar(dto);

        var m = await db.CrmFinanceiroMetas.FirstOrDefaultAsync(x => x.UsuarioId == Eu && x.Ano == ano && x.Mes == dto.Mes, ct);
        if (m is null) db.CrmFinanceiroMetas.Add(m = new CrmFinanceiroMeta { UsuarioId = Eu, Ano = ano, Mes = dto.Mes });
        m.MetaAdesao = FinanceiroValidador.Arredondar(dto.MetaAdesao);
        m.MetaBonus = FinanceiroValidador.Arredondar(dto.MetaBonus);
        m.MetaRecorrencia = FinanceiroValidador.Arredondar(dto.MetaRecorrencia);
        m.MetaVendas = dto.MetaVendas;
        m.Observacoes = FinanceiroValidador.Limpar(dto.Observacoes);
        await SalvarAsync(ct);
        await auditoria.RegistrarAsync("financeiro.meta.salva", nameof(CrmFinanceiroMeta), m.Id, new { ano, dto.Mes }, ct);
    }

    public async Task<FinanceiroVendaDto> SalvarVendaAsync(Guid? id, FinanceiroVendaDto dto, CancellationToken ct)
    {
        CrmFinanceiroVenda venda;
        var criando = false;

        if (id is { } vendaId)
        {
            venda = await db.CrmFinanceiroVendas.FirstOrDefaultAsync(x => x.Id == vendaId && x.UsuarioId == Eu, ct)
                ?? throw new CrmNotFoundException("Venda", vendaId);
        }
        else
        {
            // Vendas nascem no quadro de leads; aqui só se complementa uma venda já registrada lá, e só uma vez por venda.
            if (dto.OportunidadeId is not { } opId)
                throw new CrmBusinessException("As vendas vêm do quadro de leads: conclua a venda por lá.", "financeiro_venda_sem_card");

            var ehMinha = await db.CrmOpportunities.AnyAsync(
                o => o.Id == opId && o.ResponsavelId == Eu && !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho, ct);
            if (!ehMinha) throw new CrmNotFoundException("Venda", opId);

            var existente = await db.CrmFinanceiroVendas.FirstOrDefaultAsync(x => x.UsuarioId == Eu && x.OportunidadeId == opId, ct);
            venda = existente ?? new CrmFinanceiroVenda { UsuarioId = Eu, OportunidadeId = opId };
            criando = existente is null;
        }

        var doCard = venda.OportunidadeId is not null;
        ConferirVersao(dto.Versao, venda.RowVersion, criando);
        FinanceiroValidador.Validar(dto, doCard);

        // Os campos do card nunca vêm da tela: o que o navegador mandar em cliente/data/produto/adesão é descartado.
        if (!doCard)
        {
            venda.DataVenda = dto.DataVenda;
            venda.Cliente = FinanceiroValidador.Limpar(dto.Cliente)!;
            venda.Produto = dto.Produto;
            venda.ValorAdesao = FinanceiroValidador.Arredondar(dto.ValorAdesao);
        }
        else if (criando)
        {
            // Guarda uma cópia dos dados do card só para o registro não ficar vazio; a tela sempre lê do card.
            venda.DataVenda = dto.DataVenda;
            venda.Cliente = FinanceiroValidador.Limpar(dto.Cliente) ?? "—";
            venda.Produto = FinanceiroValidador.Produtos.Contains(dto.Produto) ? dto.Produto : "Adesão";
            venda.ValorAdesao = FinanceiroValidador.Arredondar(dto.ValorAdesao);
        }
        venda.ValorBonus = FinanceiroValidador.Arredondar(dto.ValorBonus);
        venda.Premiacoes = FinanceiroValidador.Arredondar(dto.Premiacoes);
        venda.DataPrevistaAdesao = dto.DataPrevistaAdesao;
        venda.StatusAdesao = dto.StatusAdesao;
        venda.DataPrevistaBonus = dto.DataPrevistaBonus;
        venda.StatusBonus = dto.StatusBonus;
        if (dto.BonusRecebido is < 0 or > FinanceiroValidador.ValorMaximo) throw new CrmBusinessException("O bônus recebido está fora do limite permitido.", "financeiro_invalido");
        venda.BonusRecebido = dto.BonusRecebido is { } recebido ? FinanceiroValidador.Arredondar(recebido) : null;
        venda.Observacoes = FinanceiroValidador.Limpar(dto.Observacoes);

        if (criando) db.CrmFinanceiroVendas.Add(venda);
        await SalvarAsync(ct);
        await auditoria.RegistrarAsync(criando ? "financeiro.venda.complementada" : "financeiro.venda.alterada", nameof(CrmFinanceiroVenda), venda.Id,
            new { bonus = venda.ValorBonus, premiacoes = venda.Premiacoes, venda.StatusAdesao, venda.StatusBonus }, ct);
        return ParaDto(venda);
    }

    public async Task ExcluirVendaAsync(Guid id, CancellationToken ct)
    {
        var venda = await db.CrmFinanceiroVendas.FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == Eu, ct)
            ?? throw new CrmNotFoundException("Venda", id);
        // O complemento de uma venda do card some junto da venda do card; o que o consultor lançou à mão pode ser excluído.
        if (venda.OportunidadeId is not null)
            throw new CrmBusinessException("Venda vinda do quadro de leads não pode ser excluída aqui.", "financeiro_venda_do_card");

        db.CrmFinanceiroVendas.Remove(venda);
        await SalvarAsync(ct);
        await auditoria.RegistrarAsync("financeiro.venda.excluida", nameof(CrmFinanceiroVenda), id, null, ct);
    }

    public async Task<FinanceiroCustoDto> SalvarCustoAsync(Guid? id, FinanceiroCustoDto dto, CancellationToken ct)
    {
        FinanceiroValidador.Validar(dto);

        CrmFinanceiroCusto custo;
        var criando = id is null;
        if (id is { } custoId)
        {
            custo = await db.CrmFinanceiroCustos.FirstOrDefaultAsync(x => x.Id == custoId && x.UsuarioId == Eu, ct)
                ?? throw new CrmNotFoundException("Custo", custoId);
            ConferirVersao(dto.Versao, custo.RowVersion, criando: false);
        }
        else
        {
            // Duplo clique ou reenvio logo em seguida: o mesmo custo gravado há segundos é devolvido, não repetido.
            var limite = DateTimeOffset.UtcNow - JanelaDuplicidade;
            var valor = FinanceiroValidador.Arredondar(dto.Valor);
            var descricao = FinanceiroValidador.Limpar(dto.Descricao);
            var repetido = await db.CrmFinanceiroCustos.AsNoTracking().FirstOrDefaultAsync(
                x => x.UsuarioId == Eu && x.CriadoEm > limite && x.Data == dto.Data && x.Categoria == dto.Categoria
                     && x.Valor == valor && x.Descricao == descricao, ct);
            if (repetido is not null) return ParaDto(repetido);

            custo = new CrmFinanceiroCusto { UsuarioId = Eu };
        }

        custo.Data = dto.Data;
        custo.Categoria = dto.Categoria;
        custo.Descricao = FinanceiroValidador.Limpar(dto.Descricao);
        custo.Valor = FinanceiroValidador.Arredondar(dto.Valor);
        custo.Vencimento = dto.Vencimento;
        custo.Status = dto.Status;
        // Coerência: só custo pago tem data de pagamento e comprovante; pago sem data fica com a de hoje.
        var pago = dto.Status == FinanceiroStatus.Pago;
        custo.DataPagamento = pago ? dto.DataPagamento ?? HorarioBrasilia.Hoje : null;
        custo.ComprovanteSalvo = pago && dto.ComprovanteSalvo;

        if (criando) db.CrmFinanceiroCustos.Add(custo);
        await SalvarAsync(ct);
        await auditoria.RegistrarAsync(criando ? "financeiro.custo.criado" : "financeiro.custo.alterado", nameof(CrmFinanceiroCusto), custo.Id,
            new { custo.Categoria, custo.Valor, custo.Status }, ct);
        return ParaDto(custo);
    }

    public async Task ExcluirCustoAsync(Guid id, CancellationToken ct)
    {
        var custo = await db.CrmFinanceiroCustos.FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == Eu, ct)
            ?? throw new CrmNotFoundException("Custo", id);
        db.CrmFinanceiroCustos.Remove(custo);
        await SalvarAsync(ct);
        await auditoria.RegistrarAsync("financeiro.custo.excluido", nameof(CrmFinanceiroCusto), id, new { custo.Categoria, custo.Valor }, ct);
    }

    /// <summary>Se a tela mandou o carimbo de edição e ele não bate com o do banco, alguém alterou o registro no meio do caminho.</summary>
    private static void ConferirVersao(uint? enviada, uint atual, bool criando)
    {
        if (!criando && enviada is { } v && v != atual)
            throw new CrmConcurrencyException("Este lançamento foi alterado em outra tela. Atualize a página e tente de novo.");
    }

    private async Task SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException("Este lançamento foi alterado em outra tela. Atualize a página e tente de novo.");
        }
        catch (DbUpdateException)
        {
            // Índice único (a venda do card, a meta do mês, a configuração do ano): o mesmo lançamento já foi gravado por outra requisição.
            db.ChangeTracker.Clear();
            throw new CrmBusinessException("Este lançamento já foi registrado em outra tela. Atualize a página e confira antes de lançar de novo.", "financeiro_duplicado");
        }
    }

    private static FinanceiroVendaDto ParaDto(CrmFinanceiroVenda v) => new(
        v.Id, v.OportunidadeId, false, v.DataVenda, v.Cliente, v.Produto, v.ValorAdesao, v.ValorBonus, v.Premiacoes,
        v.DataPrevistaAdesao, v.StatusAdesao, v.DataPrevistaBonus, v.StatusBonus, v.Observacoes, v.RowVersion, BonusRecebido: v.BonusRecebido);

    private static FinanceiroCustoDto ParaDto(CrmFinanceiroCusto c) => new(
        c.Id, false, c.Data, c.Categoria, c.Descricao, c.Valor, c.Vencimento, c.Status, c.DataPagamento, c.ComprovanteSalvo, c.RowVersion);
}
