using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>Cobranças da CSS por consultor (custos adicionais em parcelas, custo de lead, tráfego pago): o que é devido em cada mês e se foi pago.</summary>
public interface ISetorFinanceiroService
{
    Task<IReadOnlyList<SetorFinanceiroResumoDto>> ResumoAsync(CancellationToken ct);
    Task<SetorFinanceiroConsultorDto> ObterAsync(Guid consultorId, CancellationToken ct);
    Task DefinirPagamentoAsync(Guid consultorId, CobrancaPagamentoRequest request, CancellationToken ct);
    Task<CustoConsultorDto> AdicionarCustoAsync(Guid consultorId, CustoConsultorRequest request, CancellationToken ct);
    Task<CustoConsultorDto> AtualizarCustoAsync(Guid consultorId, Guid custoId, CustoConsultorRequest request, CancellationToken ct);
    Task RemoverCustoAsync(Guid consultorId, Guid custoId, CancellationToken ct);

    /// <summary>O próprio consultor avisa que pagou (data, observação e, se quiser, o comprovante): a cobrança aguarda a conferência do financeiro.</summary>
    Task InformarPagamentoAsync(InformarPagamentoRequest request, IFormFile? comprovante, CancellationToken ct);

    /// <summary>"Pagar depois": o próprio consultor escolhe o dia em que quer ser lembrado de pagar (nulo cancela o lembrete).</summary>
    Task AgendarLembreteAsync(LembreteCobrancaRequest request, CancellationToken ct);

    /// <summary>O financeiro não confirmou o pagamento informado: a cobrança volta ao consultor, com o motivo.</summary>
    Task RecusarPagamentoAsync(Guid consultorId, RecusarPagamentoRequest request, CancellationToken ct);
}

/// <summary>Cálculo puro das cobranças: devido pelo contrato, cruzado com o que o Setor Financeiro marcou como pago.</summary>
public sealed record CobrancasCarregadas(
    string? TipoContrato, IReadOnlyList<CobrancaDto> Cobrancas, ContratoFinanceiroCalculo.Regras Regras, IReadOnlyList<CrmCustoConsultor> Adicionais, IReadOnlyList<CrmCobrancaPagamento> Pagamentos);

public static class CobrancasCalculo
{
    /// <summary>Carrega do banco tudo o que define as cobranças de um consultor (contrato, padrões, custos adicionais e pagamentos) e as calcula.</summary>
    public static async Task<CobrancasCarregadas> CarregarAsync(ApplicationDbContext db, Guid consultorId, DateOnly hoje, CancellationToken ct)
    {
        var perfil = await db.CrmFinanceiroPerfis.AsNoTracking().FirstOrDefaultAsync(p => p.UsuarioId == consultorId, ct);
        var tipoReal = perfil?.Tipo?.ToString();
        var valores = await db.CrmFinanceiroValores.AsNoTracking().Where(v => v.UsuarioId == consultorId).ToListAsync(ct);
        var pagamentos = await db.CrmCobrancasPagamento.AsNoTracking().Where(p => p.UsuarioId == consultorId).ToListAsync(ct);
        var regras = await RegrasContratoCarregador.CarregarAsync(db, consultorId, ct);
        // Sem contrato definido: as cobranças padrão (coworking, tráfego, lead) chegam sozinhas a cada mês.
        if (await CobrancasAutomaticas.CarregarAsync(db, consultorId, ct) is { } auto)
        {
            perfil = auto.Perfil;
            regras = auto.Regras;
        }
        var adicionais = await db.CrmCustosConsultor.AsNoTracking().Where(c => c.UsuarioId == consultorId).OrderBy(c => c.PrimeiraParcela).ThenBy(c => c.CriadoEm).ToListAsync(ct);
        return new CobrancasCarregadas(tipoReal, Listar(perfil, valores, regras, pagamentos, adicionais, hoje), regras, adicionais, pagamentos);
    }

    public static readonly string[] ChavesDaCsS = ["Coworking", "CustoLead", "TrafegoPago"];

    public static string RotuloDoTipo(TipoCustoConsultor tipo) => tipo switch
    {
        TipoCustoConsultor.Notebook => "Notebook",
        TipoCustoConsultor.Celular => "Celular",
        TipoCustoConsultor.Equipamento => "Equipamento",
        _ => "Outro custo",
    };

    /// <summary>Data de vencimento da parcela <paramref name="numero"/> (1 = a primeira): o mesmo dia de cada mês, no último dia quando o mês é mais curto.</summary>
    public static DateOnly VencimentoDaParcela(CrmCustoConsultor custo, int numero) => custo.PrimeiraParcela.AddMonths(numero - 1);

    /// <summary>Valor da parcela: o total dividido pelas parcelas, com a diferença de arredondamento na última.</summary>
    public static decimal ValorDaParcela(CrmCustoConsultor custo, int numero)
    {
        var base_ = Math.Round(custo.ValorTotal / custo.Parcelas, 2);
        return numero == custo.Parcelas ? custo.ValorTotal - base_ * (custo.Parcelas - 1) : base_;
    }

    public static string Rotulo(string chave) => chave switch
    {
        "CustoLead" => "Custo de lead",
        "TrafegoPago" => "Tráfego pago",
        _ => chave,
    };

    /// <summary>Monta a cobrança com a situação do pagamento: paga (confirmada), informada pelo consultor (aguarda conferência), com lembrete ou em aberto.</summary>
    private static CobrancaDto Montar(string item, string rotulo, string descricao, DateOnly competencia, decimal valor, CrmCobrancaPagamento? pg, bool vencida, Guid? custoId, DateOnly? vencimento)
    {
        var pago = pg?.Pago == true;
        var informado = !pago && pg?.InformadoEm is not null;
        return new CobrancaDto(item, rotulo, descricao, competencia, valor, pago, pago ? pg!.PagoEm : null, pg?.Observacao, !pago && !informado && vencida, custoId, vencimento,
            informado, informado ? pg!.DataInformada : null, pg?.ComprovanteUrl, pago ? null : pg?.LembreteEm,
            !pago && !informado ? pg?.MotivoRecusa : null, pg?.ObservacaoConsultor);
    }

    /// <summary>
    /// Todas as cobranças do consultor, do início do contrato até o mês atual. Uma cobrança fica atrasada quando o mês dela já acabou e não foi paga
    /// (a do mês corrente ainda está dentro do prazo); a que o consultor já informou ter pago não conta como atrasada, só aguarda a conferência.
    /// </summary>
    public static IReadOnlyList<CobrancaDto> Listar(
        CrmFinanceiroPerfil? perfil, IReadOnlyCollection<CrmFinanceiroValor> valores, ContratoFinanceiroCalculo.Regras regras,
        IReadOnlyCollection<CrmCobrancaPagamento> pagamentos, IReadOnlyCollection<CrmCustoConsultor> custosAdicionais, DateOnly hoje)
    {
        var mesAtual = ContratoFinanceiroCalculo.PrimeiroDia(hoje);
        var lista = new List<CobrancaDto>();

        // Custos adicionais lançados pelo financeiro (qualquer consultor): uma cobrança por parcela, até o mês atual.
        foreach (var custo in custosAdicionais)
        {
            for (var n = 1; n <= custo.Parcelas; n++)
            {
                var vencimento = VencimentoDaParcela(custo, n);
                var competencia = ContratoFinanceiroCalculo.PrimeiroDia(vencimento);
                if (competencia > mesAtual) break;
                var pg = pagamentos.FirstOrDefault(p => p.CustoId == custo.Id && p.Competencia == competencia);
                var nome = string.IsNullOrWhiteSpace(custo.Descricao) ? RotuloDoTipo(custo.Tipo) : custo.Descricao;
                lista.Add(Montar("Adicional", RotuloDoTipo(custo.Tipo), $"{nome} — parcela {n}/{custo.Parcelas}", competencia, ValorDaParcela(custo, n), pg, vencimento < hoje, custo.Id, vencimento));
            }
        }

        if (perfil is not { Tipo: TipoContrato.Pj, InicioContrato: { } inicio }) return Ordenar(lista);
        for (var ano = inicio.Year; ano <= hoje.Year; ano++)
        {
            foreach (var m in ContratoFinanceiroCalculo.Meses(perfil, valores, ano, hoje, regras))
            {
                var competencia = new DateOnly(ano, m.Mes, 1);
                foreach (var c in m.Custos.Where(c => c.Chave is not null && ChavesDaCsS.Contains(c.Chave)))
                {
                    var item = Enum.Parse<ItemCobranca>(c.Chave!);
                    var pg = pagamentos.FirstOrDefault(p => p.CustoId == null && p.Item == item && p.Competencia == competencia);
                    lista.Add(Montar(c.Chave!, Rotulo(c.Chave!), c.Descricao, competencia, c.Valor, pg, competencia < mesAtual, null, null));
                }
            }
        }
        return Ordenar(lista);
    }

    private static IReadOnlyList<CobrancaDto> Ordenar(List<CobrancaDto> lista) => lista.OrderByDescending(c => c.Competencia).ThenBy(c => c.Item).ThenBy(c => c.Descricao).ToList();
}

public sealed class SetorFinanceiroService(
    ApplicationDbContext db, ICurrentUserService currentUser, IAuditSink auditoria, IEquipeComercialService equipe,
    IFileStorageService? armazenamento = null, IPushService? push = null, ICrmEventHub? eventos = null) : ISetorFinanceiroService
{
    private static readonly string[] ExtensoesDeComprovante = [".pdf", ".jpg", ".jpeg", ".png", ".webp"];
    private const long TamanhoMaximoDoComprovante = 10 * 1024 * 1024;

    private bool EhGestao => Roles.GestaoFinanceira.Any(currentUser.IsInRole);

    private async Task<bool> PodeEditarAsync(Guid consultorId, CancellationToken ct) => EhGestao && await equipe.PodeAcessarVendedorAsync(consultorId, ct);

    public async Task<IReadOnlyList<SetorFinanceiroResumoDto>> ResumoAsync(CancellationToken ct)
    {
        if (!EhGestao) throw new CrmForbiddenException("Só a gestão e o financeiro acessam o Setor Financeiro.");
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var consultores = await db.Users.AsNoTracking()
            .Where(u => u.Ativo && (visiveis == null || visiveis.Contains(u.Id)))
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Comercial)))
            .OrderBy(u => u.NomeCompleto)
            .Select(u => new { u.Id, u.NomeCompleto, u.CriadoEm })
            .ToListAsync(ct);
        var ids = consultores.Select(c => c.Id).ToList();

        var perfis = await db.CrmFinanceiroPerfis.AsNoTracking().Where(p => ids.Contains(p.UsuarioId)).ToDictionaryAsync(p => p.UsuarioId, ct);
        var valores = (await db.CrmFinanceiroValores.AsNoTracking().Where(v => ids.Contains(v.UsuarioId)).ToListAsync(ct)).ToLookup(v => v.UsuarioId);
        var prazos = (await db.CrmFinanceiroPrazos.AsNoTracking().Where(p => ids.Contains(p.UsuarioId)).ToListAsync(ct)).ToLookup(p => p.UsuarioId);
        var pagamentos = (await db.CrmCobrancasPagamento.AsNoTracking().Where(p => ids.Contains(p.UsuarioId)).ToListAsync(ct)).ToLookup(p => p.UsuarioId);
        var adicionais = (await db.CrmCustosConsultor.AsNoTracking().Where(c => ids.Contains(c.UsuarioId)).ToListAsync(ct)).ToLookup(c => c.UsuarioId);
        var padroes = await db.CrmFinanceiroPadroes.AsNoTracking().ToDictionaryAsync(p => p.Parametro, ct);
        // Leads de tráfego pago (e Notion) por consultor e mês, desde o início do contrato mais antigo: base do custo real do lead.
        var desde = perfis.Values.Where(p => p.InicioContrato != null).Select(p => p.InicioContrato!.Value)
            .Concat(consultores.Where(c => !perfis.ContainsKey(c.Id) || perfis[c.Id].Tipo is null).Select(c => HorarioBrasilia.Dia(c.CriadoEm))).DefaultIfEmpty(HorarioBrasilia.Hoje).Min();
        var leadsPorConsultor = await LeadsDeTrafegoPorMes.ContarAsync(db, ids, desde, ct);
        var avisos = await db.CrmAvisosConsultor.AsNoTracking().Where(a => a.Status == StatusAvisoConsultor.Aberto && ids.Contains(a.ConsultorId))
            .GroupBy(a => a.ConsultorId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);

        var hoje = HorarioBrasilia.Hoje;
        var resumo = new List<SetorFinanceiroResumoDto>();
        foreach (var c in consultores)
        {
            perfis.TryGetValue(c.Id, out var perfil);
            var prazosDoConsultor = prazos[c.Id].ToDictionary(p => p.Parametro);
            var regras = new ContratoFinanceiroCalculo.Regras(padroes, prazosDoConsultor, leadsPorConsultor.GetValueOrDefault(c.Id));
            // Sem contrato definido: as cobranças padrão (coworking, tráfego, lead) chegam sozinhas a cada mês.
            var automatica = perfil?.Tipo is null ? CobrancasAutomaticas.Montar(c.Id, c.CriadoEm, padroes, prazosDoConsultor, leadsPorConsultor.GetValueOrDefault(c.Id)) : null;
            var perfilCalculo = automatica?.Perfil ?? perfil;
            if (automatica is { } auto) regras = auto.Regras;
            var cobrancas = CobrancasCalculo.Listar(perfilCalculo, valores[c.Id].ToList(), regras, pagamentos[c.Id].ToList(), adicionais[c.Id].ToList(), hoje);
            var abertas = cobrancas.Where(x => !x.Pago).ToList();
            var proxima = ContratoFinanceiroCalculo.AlertasDeInicio(perfil, valores[c.Id].ToList(), regras, hoje).Select(a => (DateOnly?)a.Inicio).OrderBy(d => d).FirstOrDefault();
            resumo.Add(new SetorFinanceiroResumoDto(c.Id, c.NomeCompleto, perfil?.Tipo?.ToString(), perfil?.ModeloPj?.ToString(),
                abertas.Sum(x => x.Valor), abertas.Count, abertas.Count(x => x.Atrasada), avisos.GetValueOrDefault(c.Id), proxima,
                cobrancas.Where(x => x.Item == "CustoLead" && x.Competencia == ContratoFinanceiroCalculo.PrimeiroDia(hoje)).Sum(x => x.Valor), regras.LeadsNoMes(hoje), cobrancas.Count > 0, cobrancas.Count(x => x.InformadoPeloConsultor)));
        }
        return resumo;
    }

    public async Task<SetorFinanceiroConsultorDto> ObterAsync(Guid consultorId, CancellationToken ct)
    {
        var podeEditar = await PodeEditarAsync(consultorId, ct);
        // O consultor vê as próprias cobranças; a gestão vê as do escopo dela.
        if (consultorId != currentUser.UserId && !podeEditar) throw new CrmForbiddenException("Você não pode ver as cobranças deste consultor.");

        var nome = await db.Users.AsNoTracking().Where(u => u.Id == consultorId).Select(u => u.NomeCompleto).FirstOrDefaultAsync(ct)
            ?? throw new CrmNotFoundException("Consultor", consultorId);
        var hoje = HorarioBrasilia.Hoje;
        var mesAtual = ContratoFinanceiroCalculo.PrimeiroDia(hoje);
        var carregadas = await CobrancasCalculo.CarregarAsync(db, consultorId, hoje, ct);
        var (tipoReal, cobrancas, regras, adicionais, pagamentos) = (carregadas.TipoContrato, carregadas.Cobrancas, carregadas.Regras, carregadas.Adicionais, carregadas.Pagamentos);
        var abertas = cobrancas.Where(c => !c.Pago).ToList();

        var autores = adicionais.Select(c => c.CriadoPorId).Distinct().ToList();
        var nomes = await db.Users.AsNoTracking().Where(u => autores.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);
        var custos = adicionais.Select(c =>
        {
            var parcelaAtual = (mesAtual.Year - c.PrimeiraParcela.Year) * 12 + mesAtual.Month - c.PrimeiraParcela.Month + 1;
            return new CustoConsultorDto(c.Id, c.Tipo.ToString(), CobrancasCalculo.RotuloDoTipo(c.Tipo), c.Descricao, c.ValorTotal, c.Parcelas, CobrancasCalculo.ValorDaParcela(c, 1), c.PrimeiraParcela,
                pagamentos.Count(p => p.CustoId == c.Id && p.Pago), Math.Clamp(parcelaAtual, 0, c.Parcelas), nomes.GetValueOrDefault(c.CriadoPorId), c.CriadoEm);
        }).ToList();

        return new SetorFinanceiroConsultorDto(consultorId, nome, tipoReal, podeEditar,
            abertas.Sum(c => c.Valor), cobrancas.Where(c => c.Pago).Sum(c => c.Valor), abertas.Count, abertas.Count(c => c.Atrasada),
            cobrancas,
            cobrancas.Where(c => c.Item == "CustoLead" && c.Competencia == mesAtual).Sum(c => c.Valor), regras.LeadsNoMes(hoje),
            cobrancas.Where(c => c.Item == "CustoLead").Sum(c => c.Valor), custos);
    }

    public Task DefinirPagamentoAsync(Guid consultorId, CobrancaPagamentoRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => DefinirPagamentoAsyncCore(consultorId, request, ct));

    private async Task DefinirPagamentoAsyncCore(Guid consultorId, CobrancaPagamentoRequest request, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);
        if (!Enum.TryParse<ItemCobranca>(request.Item, ignoreCase: true, out var item) || !Enum.IsDefined(item))
            throw new CrmBusinessException("Item de cobrança inválido.", "financeiro_invalido");
        var competencia = ContratoFinanceiroCalculo.PrimeiroDia(request.Competencia);
        var hoje = HorarioBrasilia.Hoje;
        if (competencia > ContratoFinanceiroCalculo.PrimeiroDia(hoje))
            throw new CrmBusinessException("Só dá para registrar o pagamento de meses que já começaram.", "financeiro_invalido");

        // A cobrança precisa existir no contrato do consultor (não se registra pagamento do que não é devido).
        var atual = await ObterAsync(consultorId, ct);
        var custoId = item == ItemCobranca.Adicional ? request.CustoId : null;
        if (item == ItemCobranca.Adicional && custoId is null) throw new CrmBusinessException("Informe de qual custo é a parcela.", "financeiro_invalido");
        if (!atual.Cobrancas.Any(c => c.Item == item.ToString() && c.Competencia == competencia && c.CustoId == custoId))
            throw new CrmBusinessException("Essa cobrança não existe no contrato deste consultor.", "financeiro_invalido");

        var registro = await db.CrmCobrancasPagamento.FirstOrDefaultAsync(p => p.UsuarioId == consultorId && p.Item == item && p.Competencia == competencia && p.CustoId == custoId, ct);
        var antes = registro?.Pago;
        if (registro is null) db.CrmCobrancasPagamento.Add(registro = new CrmCobrancaPagamento { UsuarioId = consultorId, Item = item, Competencia = competencia, CustoId = custoId });
        registro.Pago = request.Pago;
        registro.PagoEm = request.Pago ? request.PagoEm ?? registro.DataInformada ?? hoje : null;
        if (request.Pago)
        {
            registro.LembreteEm = null;   // pago: o lembrete de "pagar depois" não faz mais sentido
            registro.RecusadoEm = null;
            registro.MotivoRecusa = null;
        }
        registro.Observacao = FinanceiroValidador.Limpar(request.Observacao) is { } o ? o[..Math.Min(o.Length, 300)] : null;
        registro.RegistradoPorId = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.cobranca.pagamento", nameof(CrmCobrancaPagamento), registro.Id,
            new { consultorId, item = item.ToString(), competencia, antes, depois = request.Pago }, ct);
    }

    /// <summary>Valida e normaliza os dados de um custo (tipo, descrição, valor, parcelas e primeira parcela), na criação e na edição.</summary>
    private static (TipoCustoConsultor Tipo, string? Descricao, decimal Valor) ValidarCusto(CustoConsultorRequest request)
    {
        if (!Enum.TryParse<TipoCustoConsultor>(request.Tipo, ignoreCase: true, out var tipo) || !Enum.IsDefined(tipo))
            throw new CrmBusinessException("Tipo de custo inválido.", "financeiro_invalido");
        var descricao = FinanceiroValidador.Limpar(request.Descricao);
        if (descricao is { Length: > 200 }) descricao = descricao[..200];
        if (tipo == TipoCustoConsultor.Outro && descricao is null) throw new CrmBusinessException("Descreva o custo.", "financeiro_invalido");
        if (request.ValorTotal <= 0 || request.ValorTotal > FinanceiroValidador.ValorMaximo) throw new CrmBusinessException("Informe um valor maior que zero e dentro do limite permitido.", "financeiro_invalido");
        if (request.Parcelas is < 1 or > 120) throw new CrmBusinessException("O número de parcelas deve ficar entre 1 e 120.", "financeiro_invalido");
        if (request.PrimeiraParcela.Year < FinanceiroValidador.AnoMinimo || request.PrimeiraParcela.Year > FinanceiroValidador.AnoMaximo)
            throw new CrmBusinessException("Mês da primeira parcela fora do período permitido.", "financeiro_invalido");
        return (tipo, descricao, FinanceiroValidador.Arredondar(request.ValorTotal));
    }

    /// <summary>
    /// Um custo idêntico (mesmo tipo, descrição, valor, parcelas e vencimento) já lançado para o consultor é quase sempre um clique duplo ou um lançamento repetido:
    /// recusa, a não ser que o financeiro confirme que são dois custos de verdade (<c>Forcar</c>).
    /// </summary>
    private async Task ExigirQueNaoRepeteAsync(Guid consultorId, Guid? ignorar, TipoCustoConsultor tipo, string? descricao, decimal valor, CustoConsultorRequest request, CancellationToken ct)
    {
        if (request.Forcar) return;
        var repetido = await db.CrmCustosConsultor.AsNoTracking().AnyAsync(c =>
            c.UsuarioId == consultorId && c.Id != ignorar && c.Tipo == tipo && c.Descricao == descricao && c.ValorTotal == valor && c.Parcelas == request.Parcelas && c.PrimeiraParcela == request.PrimeiraParcela, ct);
        if (repetido)
            throw new CrmBusinessException("Já existe um custo igual a este para o consultor (mesmo tipo, valor, parcelas e vencimento). Se são dois custos mesmo, confirme para lançar de novo.", "custo_duplicado");
    }

    private async Task<CustoConsultorDto> ParaDtoAsync(CrmCustoConsultor custo, CancellationToken ct)
    {
        var autor = await db.Users.AsNoTracking().Where(u => u.Id == custo.CriadoPorId).Select(u => u.NomeCompleto).FirstOrDefaultAsync(ct);
        var mesAtual = ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Hoje);
        var parcelaAtual = (mesAtual.Year - custo.PrimeiraParcela.Year) * 12 + mesAtual.Month - custo.PrimeiraParcela.Month + 1;
        var pagas = await db.CrmCobrancasPagamento.AsNoTracking().CountAsync(p => p.CustoId == custo.Id && p.Pago, ct);
        return new CustoConsultorDto(custo.Id, custo.Tipo.ToString(), CobrancasCalculo.RotuloDoTipo(custo.Tipo), custo.Descricao, custo.ValorTotal, custo.Parcelas, CobrancasCalculo.ValorDaParcela(custo, 1),
            custo.PrimeiraParcela, pagas, Math.Clamp(parcelaAtual, 0, custo.Parcelas), autor, custo.CriadoEm);
    }

    public async Task<CustoConsultorDto> AdicionarCustoAsync(Guid consultorId, CustoConsultorRequest request, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);
        var (tipo, descricao, valor) = ValidarCusto(request);
        await ExigirQueNaoRepeteAsync(consultorId, null, tipo, descricao, valor, request, ct);

        var custo = new CrmCustoConsultor
        {
            UsuarioId = consultorId, Tipo = tipo, Descricao = descricao, ValorTotal = valor, Parcelas = request.Parcelas,
            PrimeiraParcela = request.PrimeiraParcela, CriadoPorId = currentUser.UserId,
        };
        db.CrmCustosConsultor.Add(custo);
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.custo_consultor.adicionado", nameof(CrmCustoConsultor), custo.Id,
            new { consultorId, tipo = tipo.ToString(), custo.ValorTotal, custo.Parcelas, custo.PrimeiraParcela }, ct);
        return await ParaDtoAsync(custo, ct);
    }

    public async Task<CustoConsultorDto> AtualizarCustoAsync(Guid consultorId, Guid custoId, CustoConsultorRequest request, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);
        var custo = await db.CrmCustosConsultor.FirstOrDefaultAsync(c => c.Id == custoId && c.UsuarioId == consultorId, ct) ?? throw new CrmNotFoundException("Custo", custoId);
        var (tipo, descricao, valor) = ValidarCusto(request);
        await ExigirQueNaoRepeteAsync(consultorId, custoId, tipo, descricao, valor, request, ct);

        // Parcela já paga (ou com pagamento informado) precisa continuar existindo no novo calendário: não se perde o registro de um pagamento.
        var novosMeses = Enumerable.Range(0, request.Parcelas).Select(i => ContratoFinanceiroCalculo.PrimeiroDia(request.PrimeiraParcela.AddMonths(i))).ToHashSet();
        var comRegistro = await db.CrmCobrancasPagamento.AsNoTracking().Where(p => p.CustoId == custoId && (p.Pago || p.InformadoEm != null)).Select(p => p.Competencia).ToListAsync(ct);
        if (comRegistro.Any(c => !novosMeses.Contains(c)))
            throw new CrmBusinessException("Há parcelas já pagas (ou com pagamento informado) fora do novo calendário. Mantenha o mês da primeira parcela e um número de parcelas que as inclua.", "financeiro_invalido");

        var antes = new { Tipo = custo.Tipo.ToString(), custo.Descricao, custo.ValorTotal, custo.Parcelas, custo.PrimeiraParcela };
        var mudouVencimento = custo.PrimeiraParcela != request.PrimeiraParcela;
        custo.Tipo = tipo;
        custo.Descricao = descricao;
        custo.ValorTotal = valor;
        custo.Parcelas = request.Parcelas;
        custo.PrimeiraParcela = request.PrimeiraParcela;

        // Novo dia de vencimento: os avisos de parcelas que ainda não venceram podem ser enviados de novo, na data nova.
        if (mudouVencimento)
        {
            var mesAtual = ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Hoje);
            db.CrmCustosAviso.RemoveRange(db.CrmCustosAviso.Where(a => a.CustoId == custoId && a.Competencia >= mesAtual));
        }
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.custo_consultor.alterado", nameof(CrmCustoConsultor), custo.Id,
            new { consultorId, antes, depois = new { Tipo = tipo.ToString(), custo.Descricao, custo.ValorTotal, custo.Parcelas, custo.PrimeiraParcela } }, ct);
        return await ParaDtoAsync(custo, ct);
    }

    public async Task RemoverCustoAsync(Guid consultorId, Guid custoId, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);
        var custo = await db.CrmCustosConsultor.FirstOrDefaultAsync(c => c.Id == custoId && c.UsuarioId == consultorId, ct) ?? throw new CrmNotFoundException("Custo", custoId);
        var pagamentos = await db.CrmCobrancasPagamento.Where(p => p.CustoId == custoId).ToListAsync(ct);
        if (armazenamento is not null)
            foreach (var p in pagamentos) await armazenamento.ExcluirSeExistirAsync(p.ComprovanteUrl, ct);   // os comprovantes das parcelas saem junto
        db.CrmCobrancasPagamento.RemoveRange(pagamentos);
        db.CrmCustosAviso.RemoveRange(db.CrmCustosAviso.Where(a => a.CustoId == custoId));
        db.CrmCustosConsultor.Remove(custo);
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.custo_consultor.removido", nameof(CrmCustoConsultor), custoId,
            new { consultorId, tipo = custo.Tipo.ToString(), custo.ValorTotal, custo.Parcelas }, ct);
    }

    /// <summary>Acha (ou cria) a linha do pagamento da cobrança, conferindo que a cobrança existe para o consultor.</summary>
    private async Task<(CrmCobrancaPagamento Registro, CobrancaDto Cobranca)> RegistroDaCobrancaAsync(Guid consultorId, string itemTexto, DateOnly competenciaInformada, Guid? custoId, CancellationToken ct)
    {
        if (!Enum.TryParse<ItemCobranca>(itemTexto, ignoreCase: true, out var item) || !Enum.IsDefined(item))
            throw new CrmBusinessException("Item de cobrança inválido.", "financeiro_invalido");
        var competencia = ContratoFinanceiroCalculo.PrimeiroDia(competenciaInformada);
        var id = item == ItemCobranca.Adicional ? custoId : null;
        var carregadas = await CobrancasCalculo.CarregarAsync(db, consultorId, HorarioBrasilia.Hoje, ct);
        var cobranca = carregadas.Cobrancas.FirstOrDefault(c => c.Item == item.ToString() && c.Competencia == competencia && c.CustoId == id)
            ?? throw new CrmBusinessException("Essa cobrança não existe.", "financeiro_invalido");

        var registro = await db.CrmCobrancasPagamento.FirstOrDefaultAsync(p => p.UsuarioId == consultorId && p.Item == item && p.Competencia == competencia && p.CustoId == id, ct);
        if (registro is null) db.CrmCobrancasPagamento.Add(registro = new CrmCobrancaPagamento { UsuarioId = consultorId, Item = item, Competencia = competencia, CustoId = id, RegistradoPorId = currentUser.UserId });
        return (registro, cobranca);
    }

    public Task InformarPagamentoAsync(InformarPagamentoRequest request, IFormFile? comprovante, CancellationToken ct) => SemDuplicidadeAsync(() => InformarPagamentoAsyncCore(request, comprovante, ct));

    private async Task InformarPagamentoAsyncCore(InformarPagamentoRequest request, IFormFile? comprovante, CancellationToken ct)
    {
        var consultorId = currentUser.UserId;
        var hoje = HorarioBrasilia.Hoje;
        if (request.DataPagamento > hoje) throw new CrmBusinessException("A data do pagamento não pode ser no futuro.", "financeiro_invalido");
        var (registro, cobranca) = await RegistroDaCobrancaAsync(consultorId, request.Item, request.Competencia, request.CustoId, ct);
        if (registro.Pago) throw new CrmBusinessException("Essa cobrança já foi confirmada como paga.", "financeiro_invalido");

        if (comprovante is { Length: > 0 })
        {
            if (armazenamento is null) throw new CrmBusinessException("O envio de comprovante não está disponível.", "anexo_invalido");
            if (comprovante.Length > TamanhoMaximoDoComprovante) throw new CrmBusinessException("O comprovante deve ter no máximo 10 MB.", "anexo_invalido");
            var extensao = Path.GetExtension(comprovante.FileName).ToLowerInvariant();
            if (!ExtensoesDeComprovante.Contains(extensao)) throw new CrmBusinessException("Formato inválido. Envie um PDF, JPG, PNG ou WEBP.", "anexo_invalido");
            await using var stream = comprovante.OpenReadStream();
            if (!await AssinaturaDeArquivo.ConfereAsync(stream, extensao, ct))
                throw new CrmBusinessException("O conteúdo do arquivo não corresponde ao formato. Envie um PDF, JPG, PNG ou WEBP válido.", "anexo_invalido");
            await armazenamento.ExcluirSeExistirAsync(registro.ComprovanteUrl, ct);
            registro.ComprovanteUrl = await armazenamento.SalvarAsync($"cobrancas/{consultorId}", $"{Guid.NewGuid():N}{extensao}", stream, comprovante.ContentType, ct);
        }

        registro.InformadoEm = DateTimeOffset.UtcNow;
        registro.DataInformada = request.DataPagamento;
        registro.ObservacaoConsultor = FinanceiroValidador.Limpar(request.Observacao) is { } o ? o[..Math.Min(o.Length, 300)] : null;
        registro.LembreteEm = null;
        registro.RecusadoEm = null;
        registro.MotivoRecusa = null;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.cobranca.informada", nameof(CrmCobrancaPagamento), registro.Id,
            new { consultorId, item = request.Item, cobranca.Competencia, request.DataPagamento, comComprovante = registro.ComprovanteUrl is not null }, ct);
        eventos?.PublicarQuadroAtualizado("avisos");
    }

    public Task AgendarLembreteAsync(LembreteCobrancaRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => AgendarLembreteAsyncCore(request, ct));

    private async Task AgendarLembreteAsyncCore(LembreteCobrancaRequest request, CancellationToken ct)
    {
        var consultorId = currentUser.UserId;
        var hoje = HorarioBrasilia.Hoje;
        if (request.Data is { } dia && (dia < hoje || dia > hoje.AddYears(1)))
            throw new CrmBusinessException("Escolha uma data a partir de hoje, dentro do próximo ano.", "financeiro_invalido");
        var (registro, cobranca) = await RegistroDaCobrancaAsync(consultorId, request.Item, request.Competencia, request.CustoId, ct);
        if (registro.Pago) throw new CrmBusinessException("Essa cobrança já foi confirmada como paga.", "financeiro_invalido");

        registro.LembreteEm = request.Data;
        registro.LembreteEnviadoEm = null;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.cobranca.lembrete", nameof(CrmCobrancaPagamento), registro.Id,
            new { consultorId, item = request.Item, cobranca.Competencia, lembrarEm = request.Data }, ct);
    }

    public Task RecusarPagamentoAsync(Guid consultorId, RecusarPagamentoRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => RecusarPagamentoAsyncCore(consultorId, request, ct));

    private async Task RecusarPagamentoAsyncCore(Guid consultorId, RecusarPagamentoRequest request, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);
        var motivo = FinanceiroValidador.Limpar(request.Motivo);
        if (motivo is null || motivo.Length < 3) throw new CrmBusinessException("Explique por que o pagamento não foi confirmado.", "financeiro_invalido");
        if (motivo.Length > 300) motivo = motivo[..300];
        var (registro, cobranca) = await RegistroDaCobrancaAsync(consultorId, request.Item, request.Competencia, request.CustoId, ct);
        if (registro.InformadoEm is null) throw new CrmBusinessException("O consultor não informou pagamento desta cobrança.", "financeiro_invalido");

        registro.Pago = false;
        registro.PagoEm = null;
        registro.InformadoEm = null;
        registro.DataInformada = null;
        registro.RecusadoEm = DateTimeOffset.UtcNow;
        registro.MotivoRecusa = motivo;
        registro.RegistradoPorId = currentUser.UserId;
        var aviso = new CrmAvisoConsultor
        {
            ConsultorId = consultorId, Titulo = "Pagamento não confirmado",
            Mensagem = $"O financeiro não confirmou o pagamento de {cobranca.Rotulo} ({cobranca.Descricao}): {motivo}",
            Valor = cobranca.Valor, Referencia = cobranca.Rotulo, EnviadoPorId = currentUser.UserId,
        };
        db.CrmAvisosConsultor.Add(aviso);
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.cobranca.recusada", nameof(CrmCobrancaPagamento), registro.Id, new { consultorId, request.Item, cobranca.Competencia, motivo }, ct);
        if (push is not null)
        {
            try
            {
                await push.EnviarAsync(consultorId, new PushMensagem(aviso.Titulo, aviso.Mensagem, "/app/portal/controle-financeiro", $"cobranca-recusada-{registro.Id}"), ct);
            }
            catch
            {
                // O aviso dentro do CRM já foi criado; falha no push não desfaz a recusa.
            }
        }
        eventos?.PublicarQuadroAtualizado("avisos");
    }

    /// <summary>
    /// Dois cliques (ou duas abas) ao mesmo tempo para a mesma cobrança tentam criar a mesma linha de pagamento; o índice único deixa só uma. A perdedora
    /// recomeça do zero e passa a atualizar a linha que a outra criou, em vez de dar erro.
    /// </summary>
    private async Task SemDuplicidadeAsync(Func<Task> operacao)
    {
        try
        {
            await operacao();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            await operacao();
        }
    }

    private async Task ExigirEdicaoAsync(Guid consultorId, CancellationToken ct)
    {
        if (!await PodeEditarAsync(consultorId, ct))
            throw new CrmForbiddenException("Só a gestão e o financeiro registram pagamentos e lançam custos para o consultor.");
        if (!await db.Users.AnyAsync(u => u.Id == consultorId, ct)) throw new CrmNotFoundException("Consultor", consultorId);
    }
}
