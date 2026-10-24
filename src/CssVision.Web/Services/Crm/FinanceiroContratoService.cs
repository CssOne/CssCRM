using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Contrato do consultor no Controle financeiro: tipo (PJ/CLT), transporte e os valores com histórico mês a mês. Quem DEFINE é a gestão
/// (administrador, gestor master, supervisor, gestor regional e financeiro) dentro do escopo que já enxerga; o consultor só consulta o
/// próprio contrato e o histórico dele.
/// </summary>
public interface IFinanceiroContratoService
{
    Task<FinanceiroContratoDto> ObterAsync(Guid consultorId, int? ano, CancellationToken ct);
    Task SalvarPerfilAsync(Guid consultorId, FinanceiroPerfilRequest request, CancellationToken ct);
    Task DefinirValorAsync(Guid consultorId, FinanceiroValorRequest request, CancellationToken ct);
    Task RemoverValorAsync(Guid consultorId, Guid valorId, CancellationToken ct);
    Task DefinirPrazoAsync(Guid consultorId, FinanceiroPrazoRequest request, CancellationToken ct);
    Task ConfirmarInicioAsync(Guid consultorId, string parametro, CancellationToken ct);
    Task<IReadOnlyList<FinanceiroAlertaCobrancaDto>> ListarAlertasAsync(CancellationToken ct);
    Task<FinanceiroPadroesDto> ObterPadroesAsync(CancellationToken ct);
    Task DefinirPadraoAsync(FinanceiroPadraoRequest request, CancellationToken ct);
}

public sealed class FinanceiroContratoService(ApplicationDbContext db, ICurrentUserService currentUser, IAuditSink auditoria, IEquipeComercialService equipe) : IFinanceiroContratoService
{
    private static readonly ParametroFinanceiro[] CustosFixosPj =
        [.. ContratoFinanceiroCalculo.CustosFixosPj, ParametroFinanceiro.Transporte];

    /// <summary>Valores que existem para o contrato do consultor: PJ tem o do seu modelo (bônus por placa OU recorrência) e os custos fixos; CLT, salário e % da adesão.</summary>
    private static ParametroFinanceiro[] ParametrosDo(TipoContrato tipo, ModeloRemuneracaoPj? modelo) => tipo == TipoContrato.Clt
        ? [ParametroFinanceiro.Salario, ParametroFinanceiro.PercentualAdesao]
        : [modelo == ModeloRemuneracaoPj.Recorrencia ? ParametroFinanceiro.Recorrencia : ParametroFinanceiro.BonusPorPlaca, .. CustosFixosPj];

    private bool EhGestao => Roles.GestaoFinanceira.Any(currentUser.IsInRole);

    private async Task<bool> PodeEditarAsync(Guid consultorId, CancellationToken ct) =>
        EhGestao && await equipe.PodeAcessarVendedorAsync(consultorId, ct);

    public async Task<FinanceiroContratoDto> ObterAsync(Guid consultorId, int? ano, CancellationToken ct)
    {
        var a = FinanceiroValidador.ValidarAno(ano ?? HorarioBrasilia.Hoje.Year);
        var ehEu = consultorId == currentUser.UserId;
        var podeEditar = await PodeEditarAsync(consultorId, ct);
        // O consultor vê o próprio contrato; a gestão vê o dos consultores do seu escopo; mais ninguém.
        if (!ehEu && !podeEditar) throw new CrmForbiddenException("Você não pode ver o contrato deste consultor.");

        var nome = await db.Users.AsNoTracking().Where(u => u.Id == consultorId).Select(u => u.NomeCompleto).FirstOrDefaultAsync(ct)
            ?? throw new CrmNotFoundException("Consultor", consultorId);
        var perfil = await db.CrmFinanceiroPerfis.AsNoTracking().FirstOrDefaultAsync(p => p.UsuarioId == consultorId, ct);
        var valores = await db.CrmFinanceiroValores.AsNoTracking().Where(v => v.UsuarioId == consultorId).ToListAsync(ct);
        var regras = await RegrasContratoCarregador.CarregarAsync(db, consultorId, ct);

        var autores = valores.Select(v => v.DefinidoPorId).Distinct().ToList();
        var nomes = await db.Users.AsNoTracking().Where(u => autores.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);

        var hoje = HorarioBrasilia.Hoje;
        var mesAtual = ContratoFinanceiroCalculo.PrimeiroDia(hoje);
        var atuais = new List<FinanceiroParametroAtualDto>();
        if (perfil?.Tipo is { } tipo)
        {
            foreach (var p in ParametrosDo(tipo, perfil.ModeloPj))
            {
                var v = ContratoFinanceiroCalculo.Vigente(valores, p, mesAtual, soNoMes: p == ParametroFinanceiro.Recorrencia);
                var padraoDoPj = ContratoFinanceiroCalculo.CustosFixosPj.Contains(p);
                var padrao = v is null && padraoDoPj;
                var valor = v?.Valor ?? (padraoDoPj ? regras.ValorPadrao(p) : (decimal?)null);
                if (!padraoDoPj || tipo != TipoContrato.Pj)
                {
                    atuais.Add(new FinanceiroParametroAtualDto(p.ToString(), valor, v?.VigenteDesde, padrao));
                    continue;
                }
                var carencia = regras.Carencia(p);
                var inicioCobranca = perfil.InicioContrato is { } ic ? ContratoFinanceiroCalculo.InicioCobranca(ic, carencia) : (DateOnly?)null;
                // Custo de lead é o valor por lead: o custo real do mês é esse valor vezes os leads de tráfego pago e do Notion que o consultor pegou.
                int? leadsNoMes = p == ParametroFinanceiro.CustoLead ? regras.LeadsNoMes(mesAtual) : null;
                var custoReal = leadsNoMes is { } n && (inicioCobranca is null || mesAtual >= inicioCobranca) ? Math.Round((valor ?? 0m) * n, 2) : (decimal?)null;
                atuais.Add(new FinanceiroParametroAtualDto(p.ToString(), valor, v?.VigenteDesde, padrao, carencia, regras.CarenciaPropria(p), regras.CarenciaPadrao(p), inicioCobranca, leadsNoMes, custoReal));
            }
        }

        return new FinanceiroContratoDto(
            consultorId, nome,
            new FinanceiroPerfilDto(perfil?.Tipo?.ToString(), (perfil?.Transporte ?? ModalidadeTransporte.Nenhum).ToString(), perfil?.InicioContrato, perfil?.ModeloPj?.ToString()),
            podeEditar,
            atuais,
            valores.OrderBy(v => v.Parametro).ThenByDescending(v => v.VigenteDesde)
                .Select(v => new FinanceiroValorDto(v.Id, v.Parametro.ToString(), v.VigenteDesde, v.Valor, v.Observacao, nomes.GetValueOrDefault(v.DefinidoPorId), v.AtualizadoEm ?? v.CriadoEm))
                .ToList(),
            await ComComissaoDoCltAsync(consultorId, a, perfil, ContratoFinanceiroCalculo.Meses(perfil, valores, a, hoje, regras), ct));
    }

    /// <summary>
    /// Consultor CLT: a comissão do mês é calculada sozinha — a porcentagem da adesão do mês vezes o valor de adesão de cada venda dele (as do quadro de
    /// leads e as lançadas à mão). É a mesma conta que o Controle financeiro faz em cada venda, arredondada venda a venda.
    /// </summary>
    private async Task<IReadOnlyList<FinanceiroMesContratoDto>> ComComissaoDoCltAsync(Guid consultorId, int ano, CrmFinanceiroPerfil? perfil, IReadOnlyList<FinanceiroMesContratoDto> meses, CancellationToken ct)
    {
        if (perfil?.Tipo != TipoContrato.Clt || meses.Count == 0) return meses;
        var de = new DateOnly(ano, 1, 1);
        var ate = new DateOnly(ano + 1, 1, 1);
        var doQuadro = await db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.ResponsavelId == consultorId && o.Etapa.Tipo == TipoEtapaPipeline.Ganho
                        && o.DataEfetivaFechamento >= HorarioBrasilia.Inicio(de) && o.DataEfetivaFechamento < HorarioBrasilia.Inicio(ate))
            .Select(o => new { Data = o.DataEfetivaFechamento!.Value, Valor = o.PagamentoAdesao ?? 0m })
            .ToListAsync(ct);
        var manuais = await db.CrmFinanceiroVendas.AsNoTracking()
            .Where(v => v.UsuarioId == consultorId && v.OportunidadeId == null && v.DataVenda >= de && v.DataVenda < ate)
            .Select(v => new { v.DataVenda, v.ValorAdesao })
            .ToListAsync(ct);
        var vendas = doQuadro.Select(o => (Mes: HorarioBrasilia.Dia(o.Data).Month, o.Valor)).Concat(manuais.Select(v => (Mes: v.DataVenda.Month, Valor: v.ValorAdesao))).ToList();

        return meses.Select(m =>
        {
            var doMes = vendas.Where(v => v.Mes == m.Mes).ToList();
            return m with
            {
                AdesoesDoMes = doMes.Sum(v => v.Valor),
                Comissao = doMes.Sum(v => FinanceiroValidador.Arredondar(v.Valor * m.PercentualAdesao)),
                VendasDoMes = doMes.Count,
            };
        }).ToList();
    }

    public Task SalvarPerfilAsync(Guid consultorId, FinanceiroPerfilRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => SalvarPerfilAsyncCore(consultorId, request, ct));

    private async Task SalvarPerfilAsyncCore(Guid consultorId, FinanceiroPerfilRequest request, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);

        TipoContrato? tipo = null;
        if (!string.IsNullOrWhiteSpace(request.Tipo))
        {
            if (!Enum.TryParse<TipoContrato>(request.Tipo, ignoreCase: true, out var t) || !Enum.IsDefined(t))
                throw new CrmBusinessException("Tipo de contrato inválido (use PJ ou CLT).", "financeiro_invalido");
            tipo = t;
        }
        var transporte = ModalidadeTransporte.Nenhum;
        if (!string.IsNullOrWhiteSpace(request.Transporte) && (!Enum.TryParse(request.Transporte, ignoreCase: true, out transporte) || !Enum.IsDefined(transporte)))
            throw new CrmBusinessException("Modalidade de transporte inválida.", "financeiro_invalido");
        ModeloRemuneracaoPj? modelo = null;
        if (!string.IsNullOrWhiteSpace(request.ModeloPj))
        {
            if (!Enum.TryParse<ModeloRemuneracaoPj>(request.ModeloPj, ignoreCase: true, out var m) || !Enum.IsDefined(m))
                throw new CrmBusinessException("Modelo de remuneração inválido (bônus por placa ou recorrência).", "financeiro_invalido");
            modelo = m;
        }
        if (tipo == TipoContrato.Pj && modelo is null)
            throw new CrmBusinessException("Escolha como o consultor PJ é remunerado: bônus por placa ou recorrência (um dos dois).", "financeiro_invalido");
        if (tipo != TipoContrato.Pj) modelo = null;
        if (tipo == TipoContrato.Clt) transporte = ModalidadeTransporte.Nenhum; // CLT não tem custos fixos
        if (tipo is not null && request.InicioContrato is null)
            throw new CrmBusinessException("Informe o mês de início do contrato.", "financeiro_invalido");
        if (request.InicioContrato is { } inicio && (inicio.Year < FinanceiroValidador.AnoMinimo || inicio.Year > FinanceiroValidador.AnoMaximo))
            throw new CrmBusinessException("Mês de início fora do período permitido.", "financeiro_invalido");

        var perfil = await db.CrmFinanceiroPerfis.FirstOrDefaultAsync(p => p.UsuarioId == consultorId, ct);
        var antes = perfil is null ? null : new { Tipo = perfil.Tipo?.ToString(), Modelo = perfil.ModeloPj?.ToString(), Transporte = perfil.Transporte.ToString(), perfil.InicioContrato };
        if (perfil is null) db.CrmFinanceiroPerfis.Add(perfil = new CrmFinanceiroPerfil { UsuarioId = consultorId });
        perfil.Tipo = tipo;
        perfil.ModeloPj = modelo;
        perfil.Transporte = transporte;
        perfil.InicioContrato = request.InicioContrato is { } d ? ContratoFinanceiroCalculo.PrimeiroDia(d) : null;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.contrato.perfil", nameof(CrmFinanceiroPerfil), perfil.Id,
            new { consultorId, antes, depois = new { Tipo = tipo?.ToString(), Modelo = modelo?.ToString(), Transporte = transporte.ToString(), perfil.InicioContrato } }, ct);
    }

    public Task DefinirValorAsync(Guid consultorId, FinanceiroValorRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => DefinirValorAsyncCore(consultorId, request, ct));

    private async Task DefinirValorAsyncCore(Guid consultorId, FinanceiroValorRequest request, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);

        if (!Enum.TryParse<ParametroFinanceiro>(request.Parametro, ignoreCase: true, out var parametro) || !Enum.IsDefined(parametro))
            throw new CrmBusinessException("Parâmetro financeiro inválido.", "financeiro_invalido");
        var perfil = await db.CrmFinanceiroPerfis.AsNoTracking().FirstOrDefaultAsync(p => p.UsuarioId == consultorId, ct);
        if (perfil?.Tipo is not { } tipo)
            throw new CrmBusinessException("Defina primeiro se o consultor é PJ ou CLT.", "financeiro_sem_contrato");
        if (!ParametrosDo(tipo, perfil.ModeloPj).Contains(parametro))
        {
            var motivo = tipo == TipoContrato.Pj && parametro is ParametroFinanceiro.BonusPorPlaca or ParametroFinanceiro.Recorrencia
                ? "Este consultor PJ é remunerado por " + (perfil.ModeloPj == ModeloRemuneracaoPj.Recorrencia ? "recorrência" : "bônus por placa") + " — não os dois."
                : $"Este valor não se aplica a consultor {(tipo == TipoContrato.Pj ? "PJ" : "CLT")}.";
            throw new CrmBusinessException(motivo, "financeiro_invalido");
        }
        if (request.VigenteDesde.Year < FinanceiroValidador.AnoMinimo || request.VigenteDesde.Year > FinanceiroValidador.AnoMaximo)
            throw new CrmBusinessException("Mês de vigência fora do período permitido.", "financeiro_invalido");

        var valor = request.Valor;
        if (valor < 0 || valor > FinanceiroValidador.ValorMaximo) throw new CrmBusinessException("Valor fora do limite permitido.", "financeiro_invalido");
        if (parametro == ParametroFinanceiro.PercentualAdesao)
        {
            // Informado como fração (0,30) ou como porcentagem (30): até 1 é fração, acima disso é porcentagem. Máximo 100%.
            valor = valor > 1 ? valor / 100m : valor;
            if (valor > 1) throw new CrmBusinessException("A porcentagem da adesão deve ficar entre 0% e 100%.", "financeiro_invalido");
            valor = Math.Round(valor, 4);
        }
        else
        {
            valor = FinanceiroValidador.Arredondar(valor);
        }

        var mes = ContratoFinanceiroCalculo.PrimeiroDia(request.VigenteDesde);
        var existente = await db.CrmFinanceiroValores.FirstOrDefaultAsync(v => v.UsuarioId == consultorId && v.Parametro == parametro && v.VigenteDesde == mes, ct);
        var anterior = existente?.Valor;
        if (existente is null)
            db.CrmFinanceiroValores.Add(existente = new CrmFinanceiroValor { UsuarioId = consultorId, Parametro = parametro, VigenteDesde = mes });
        existente.Valor = valor;
        existente.Observacao = FinanceiroValidador.Limpar(request.Observacao) is { } o ? o[..Math.Min(o.Length, 300)] : null;
        existente.DefinidoPorId = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.contrato.valor", nameof(CrmFinanceiroValor), existente.Id,
            new { consultorId, parametro = parametro.ToString(), mes, anterior, depois = valor }, ct);
    }

    public async Task RemoverValorAsync(Guid consultorId, Guid valorId, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);
        var v = await db.CrmFinanceiroValores.FirstOrDefaultAsync(x => x.Id == valorId && x.UsuarioId == consultorId, ct)
            ?? throw new CrmNotFoundException("Valor", valorId);
        db.CrmFinanceiroValores.Remove(v);
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.contrato.valor.removido", nameof(CrmFinanceiroValor), valorId,
            new { consultorId, parametro = v.Parametro.ToString(), mes = v.VigenteDesde, valor = v.Valor }, ct);
    }

    /// <summary>Só os custos fixos do PJ (coworking, MEI, tráfego pago, custo de lead) têm prazo.</summary>
    private static ParametroFinanceiro ParseCustoFixo(string texto)
    {
        if (!Enum.TryParse<ParametroFinanceiro>(texto, ignoreCase: true, out var p) || !ContratoFinanceiroCalculo.CustosFixosPj.Contains(p))
            throw new CrmBusinessException("Este valor não tem prazo para começar a ser cobrado.", "financeiro_invalido");
        return p;
    }

    private const int CarenciaMaximaMeses = 60;

    public Task DefinirPrazoAsync(Guid consultorId, FinanceiroPrazoRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => DefinirPrazoAsyncCore(consultorId, request, ct));

    private async Task DefinirPrazoAsyncCore(Guid consultorId, FinanceiroPrazoRequest request, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);
        var parametro = ParseCustoFixo(request.Parametro);
        if (request.CarenciaMeses is < 0 or > CarenciaMaximaMeses)
            throw new CrmBusinessException($"O prazo deve ficar entre 0 e {CarenciaMaximaMeses} meses.", "financeiro_invalido");

        var prazo = await db.CrmFinanceiroPrazos.FirstOrDefaultAsync(p => p.UsuarioId == consultorId && p.Parametro == parametro, ct);
        var antes = prazo?.CarenciaMeses;
        if (prazo is null) db.CrmFinanceiroPrazos.Add(prazo = new CrmFinanceiroPrazo { UsuarioId = consultorId, Parametro = parametro });
        prazo.CarenciaMeses = request.CarenciaMeses;
        prazo.AlertaConfirmadoPara = null; // prazo novo = o aviso volta quando chegar perto do novo início
        prazo.DefinidoPorId = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.contrato.prazo", nameof(CrmFinanceiroPrazo), prazo.Id,
            new { consultorId, parametro = parametro.ToString(), antes, depois = request.CarenciaMeses }, ct);
    }

    public Task ConfirmarInicioAsync(Guid consultorId, string parametroTexto, CancellationToken ct) => SemDuplicidadeAsync(() => ConfirmarInicioAsyncCore(consultorId, parametroTexto, ct));

    private async Task ConfirmarInicioAsyncCore(Guid consultorId, string parametroTexto, CancellationToken ct)
    {
        await ExigirEdicaoAsync(consultorId, ct);
        var parametro = ParseCustoFixo(parametroTexto);
        var perfil = await db.CrmFinanceiroPerfis.AsNoTracking().FirstOrDefaultAsync(p => p.UsuarioId == consultorId, ct);
        if (perfil is not { Tipo: TipoContrato.Pj, InicioContrato: { } inicioContrato })
            throw new CrmBusinessException("O consultor não tem contrato PJ com início definido.", "financeiro_sem_contrato");

        var regras = await RegrasContratoCarregador.CarregarAsync(db, consultorId, ct);
        var inicio = ContratoFinanceiroCalculo.InicioCobranca(inicioContrato, regras.Carencia(parametro));
        var prazo = await db.CrmFinanceiroPrazos.FirstOrDefaultAsync(p => p.UsuarioId == consultorId && p.Parametro == parametro, ct);
        if (prazo is null) db.CrmFinanceiroPrazos.Add(prazo = new CrmFinanceiroPrazo { UsuarioId = consultorId, Parametro = parametro });
        prazo.AlertaConfirmadoPara = inicio;
        prazo.DefinidoPorId = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.contrato.inicio_confirmado", nameof(CrmFinanceiroPrazo), prazo.Id,
            new { consultorId, parametro = parametro.ToString(), inicioCobranca = inicio }, ct);
    }

    public async Task<IReadOnlyList<FinanceiroAlertaCobrancaDto>> ListarAlertasAsync(CancellationToken ct)
    {
        var eu = currentUser.UserId;
        List<Guid>? visiveis = null;
        if (EhGestao) visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);

        var perfis = await db.CrmFinanceiroPerfis.AsNoTracking()
            .Where(p => p.Tipo == TipoContrato.Pj && p.InicioContrato != null)
            .Where(p => p.UsuarioId == eu || (EhGestao && (visiveis == null || visiveis.Contains(p.UsuarioId))))
            .Where(p => db.Users.Any(u => u.Id == p.UsuarioId && u.Ativo))
            // Cobrança é do consultor: administradores (que não têm o papel de consultor) não entram nos avisos de início de cobrança.
            .Where(p => db.UserRoles.Any(ur => ur.UserId == p.UsuarioId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Comercial)))
            .ToListAsync(ct);
        if (perfis.Count == 0) return [];

        var ids = perfis.Select(p => p.UsuarioId).ToList();
        var nomes = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);
        var valoresPorUsuario = (await db.CrmFinanceiroValores.AsNoTracking().Where(v => ids.Contains(v.UsuarioId)).ToListAsync(ct))
            .GroupBy(v => v.UsuarioId).ToDictionary(g => g.Key, g => g.ToList());
        var prazosPorUsuario = (await db.CrmFinanceiroPrazos.AsNoTracking().Where(p => ids.Contains(p.UsuarioId)).ToListAsync(ct))
            .GroupBy(p => p.UsuarioId).ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Parametro));
        var padroes = await db.CrmFinanceiroPadroes.AsNoTracking().ToDictionaryAsync(p => p.Parametro, ct);

        var hoje = HorarioBrasilia.Hoje;
        var alertas = new List<FinanceiroAlertaCobrancaDto>();
        foreach (var perfil in perfis)
        {
            var regras = new ContratoFinanceiroCalculo.Regras(padroes, prazosPorUsuario.GetValueOrDefault(perfil.UsuarioId));
            var valores = valoresPorUsuario.GetValueOrDefault(perfil.UsuarioId) ?? [];
            var podeEditar = EhGestao && (visiveis == null || visiveis.Contains(perfil.UsuarioId));
            foreach (var (parametro, inicio, valor, dias) in ContratoFinanceiroCalculo.AlertasDeInicio(perfil, valores, regras, hoje))
                alertas.Add(new FinanceiroAlertaCobrancaDto(perfil.UsuarioId, nomes.GetValueOrDefault(perfil.UsuarioId) ?? "Consultor", parametro.ToString(),
                    ContratoFinanceiroCalculo.Rotulo(parametro), valor, inicio, dias, regras.Carencia(parametro), podeEditar));
        }
        return alertas.OrderBy(a => a.DiasRestantes).ThenBy(a => a.ConsultorNome).ToList();
    }

    /// <summary>Padrões que valem para todos os consultores PJ afetam todas as regionais: só administrador, gestor master e supervisor definem.</summary>
    private bool PodeDefinirPadroes => Roles.Administrativos.Any(currentUser.IsInRole);

    public async Task<FinanceiroPadroesDto> ObterPadroesAsync(CancellationToken ct)
    {
        if (!EhGestao) throw new CrmForbiddenException("Só a gestão vê os valores padrão dos consultores PJ.");
        var salvos = await db.CrmFinanceiroPadroes.AsNoTracking().ToDictionaryAsync(p => p.Parametro, ct);
        var autores = salvos.Values.Select(p => p.DefinidoPorId).Distinct().ToList();
        var nomes = await db.Users.AsNoTracking().Where(u => autores.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);
        var regras = new ContratoFinanceiroCalculo.Regras(salvos);
        return new FinanceiroPadroesDto(PodeDefinirPadroes, ContratoFinanceiroCalculo.CustosFixosPj.Select(p =>
        {
            salvos.TryGetValue(p, out var s);
            return new FinanceiroPadraoDto(p.ToString(), regras.ValorPadrao(p), regras.CarenciaPadrao(p), s?.Valor is not null, s?.CarenciaMeses is not null,
                s is null ? null : nomes.GetValueOrDefault(s.DefinidoPorId));
        }).ToList());
    }

    public Task DefinirPadraoAsync(FinanceiroPadraoRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => DefinirPadraoAsyncCore(request, ct));

    private async Task DefinirPadraoAsyncCore(FinanceiroPadraoRequest request, CancellationToken ct)
    {
        if (!PodeDefinirPadroes)
            throw new CrmForbiddenException("Só administrador, gestor master e supervisor definem o valor padrão de todos os consultores PJ. Para um consultor só, use o contrato dele.");
        var parametro = ParseCustoFixo(request.Parametro);
        if (request.Valor is < 0 or > FinanceiroValidador.ValorMaximo) throw new CrmBusinessException("Valor fora do limite permitido.", "financeiro_invalido");
        if (request.CarenciaMeses is < 0 or > CarenciaMaximaMeses)
            throw new CrmBusinessException($"O prazo deve ficar entre 0 e {CarenciaMaximaMeses} meses.", "financeiro_invalido");

        var padrao = await db.CrmFinanceiroPadroes.FirstOrDefaultAsync(p => p.Parametro == parametro, ct);
        var antes = padrao is null ? null : new { padrao.Valor, padrao.CarenciaMeses };
        if (padrao is null) db.CrmFinanceiroPadroes.Add(padrao = new CrmFinanceiroPadrao { Parametro = parametro });
        padrao.Valor = request.Valor is { } v ? FinanceiroValidador.Arredondar(v) : null;
        padrao.CarenciaMeses = request.CarenciaMeses;
        padrao.DefinidoPorId = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.contrato.padrao", nameof(CrmFinanceiroPadrao), padrao.Id,
            new { parametro = parametro.ToString(), antes, depois = new { padrao.Valor, padrao.CarenciaMeses } }, ct);
    }

    /// <summary>
    /// Cada consultor tem um perfil, um valor por parâmetro e mês, um prazo por custo e há um padrão por custo (índices únicos). Dois cliques (ou duas abas) ao mesmo
    /// tempo tentam criar a mesma linha: a que perde recomeça do zero e passa a atualizar a linha que a outra criou, em vez de dar erro.
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
            throw new CrmForbiddenException("Só a gestão (administrador, gestor ou financeiro) define o contrato e os valores do consultor.");
        if (!await db.Users.AnyAsync(u => u.Id == consultorId, ct)) throw new CrmNotFoundException("Consultor", consultorId);
    }
}
