using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Contrato do grupo (dentro de uma regional): o que vale para todo consultor do grupo — tipo (PJ/CLT), custos fixos, alimentação, passagem... — com
/// vigência mensal e carência. Quem consulta é a gestão financeira; quem define é a gestão com visão ampla (administrador, gestor master e supervisor),
/// porque a mudança vale para todos do grupo. Salário e recorrência não existem aqui: são sempre do consultor.
/// </summary>
public interface IContratoGrupoService
{
    Task<IReadOnlyList<GrupoContratoResumoDto>> ListarAsync(CancellationToken ct);
    Task<GrupoContratoDto> ObterAsync(Guid grupoId, CancellationToken ct);
    Task SalvarPerfilAsync(Guid grupoId, GrupoPerfilRequest request, CancellationToken ct);
    Task DefinirValorAsync(Guid grupoId, GrupoValorRequest request, CancellationToken ct);
    Task RemoverValorAsync(Guid grupoId, Guid valorId, CancellationToken ct);
    Task DefinirPrazoAsync(Guid grupoId, GrupoPrazoRequest request, CancellationToken ct);
}

public sealed class ContratoGrupoService(ApplicationDbContext db, ICurrentUserService currentUser, IAuditSink auditoria) : IContratoGrupoService
{
    /// <summary>Valores que o grupo pode definir para o tipo de contrato dele. Salário e recorrência nunca: são individuais.</summary>
    public static ParametroFinanceiro[] ParametrosDoGrupo(TipoContrato? tipo, ModeloRemuneracaoPj? modelo)
    {
        var lista = new List<ParametroFinanceiro>();
        if (tipo == TipoContrato.Clt) lista.AddRange([ParametroFinanceiro.Alimentacao, ParametroFinanceiro.Passagem]);
        else if (tipo == TipoContrato.Pj)
        {
            if (modelo != ModeloRemuneracaoPj.Recorrencia) lista.Add(ParametroFinanceiro.BonusPorPlaca);
            lista.AddRange(ContratoFinanceiroCalculo.CustosFixosPj);
            lista.Add(ParametroFinanceiro.Transporte);
        }
        return [.. lista];
    }

    private const int CarenciaMaximaMeses = 60;

    private bool EhGestao => Roles.GestaoFinanceira.Any(currentUser.IsInRole);
    private bool PodeEditar => Roles.Administrativos.Any(currentUser.IsInRole);

    /// <summary>Grupos que a pessoa enxerga: com visão total, os das regionais que ela não ocultou; senão, só os da própria regional.</summary>
    private async Task<IQueryable<CrmGrupo>> GruposVisiveisAsync(CancellationToken ct)
    {
        if (!EhGestao) throw new CrmForbiddenException("Só a gestão e o financeiro veem o contrato dos grupos.");
        if (currentUser.TemVisaoTotal)
        {
            var ocultas = await EscopoRegional.OcultasAsync(db, currentUser, ct);
            return db.CrmGrupos.AsNoTracking().Where(g => !ocultas.Contains(g.RegionalId));
        }
        var regional = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.RegionalId).FirstOrDefaultAsync(ct);
        return db.CrmGrupos.AsNoTracking().Where(g => regional != null && g.RegionalId == regional);
    }

    private async Task<CrmGrupo> ExigirGrupoAsync(Guid grupoId, bool paraEditar, CancellationToken ct)
    {
        var visiveis = await GruposVisiveisAsync(ct);
        if (!await visiveis.AnyAsync(g => g.Id == grupoId, ct)) throw new CrmNotFoundException("Grupo", grupoId);
        if (paraEditar && !PodeEditar)
            throw new CrmForbiddenException("Só administrador, gestor master e supervisor definem o contrato de um grupo: a mudança vale para todos os consultores dele.");
        return await db.CrmGrupos.Include(g => g.Regional).FirstAsync(g => g.Id == grupoId, ct);
    }

    public async Task<IReadOnlyList<GrupoContratoResumoDto>> ListarAsync(CancellationToken ct)
    {
        var grupos = await (await GruposVisiveisAsync(ct)).Include(g => g.Regional)
            .OrderBy(g => g.Regional.Nome).ThenBy(g => g.Nome).ToListAsync(ct);
        var ids = grupos.Select(g => g.Id).ToList();
        var perfis = (await db.CrmGrupoContratoPerfis.AsNoTracking().Where(p => ids.Contains(p.GrupoId)).ToListAsync(ct)).ToLookup(p => p.GrupoId);
        var consultores = await db.Users.AsNoTracking().Where(u => u.GrupoId != null && ids.Contains(u.GrupoId.Value))
            .GroupBy(u => u.GrupoId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var hoje = ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Hoje);

        return grupos.Select(g =>
        {
            var vigente = PerfilVigente(perfis[g.Id], hoje);
            return new GrupoContratoResumoDto(g.Id, g.Nome, g.RegionalId, g.Regional.Nome, g.Ativo, consultores.GetValueOrDefault(g.Id), vigente?.Tipo?.ToString(), vigente?.ModeloPj?.ToString());
        }).ToList();
    }

    private static CrmGrupoContratoPerfil? PerfilVigente(IEnumerable<CrmGrupoContratoPerfil> perfis, DateOnly mes) =>
        perfis.Where(p => p.VigenteDesde <= mes).OrderByDescending(p => p.VigenteDesde).FirstOrDefault();

    public async Task<GrupoContratoDto> ObterAsync(Guid grupoId, CancellationToken ct)
    {
        var grupo = await ExigirGrupoAsync(grupoId, paraEditar: false, ct);
        var hoje = ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Hoje);

        var perfis = await db.CrmGrupoContratoPerfis.AsNoTracking().Where(p => p.GrupoId == grupoId).OrderByDescending(p => p.VigenteDesde).ToListAsync(ct);
        var valores = await db.CrmGrupoContratoValores.AsNoTracking().Where(v => v.GrupoId == grupoId).ToListAsync(ct);
        var prazos = await db.CrmGrupoContratoPrazos.AsNoTracking().Where(p => p.GrupoId == grupoId).ToDictionaryAsync(p => p.Parametro, ct);
        var padroes = await db.CrmFinanceiroPadroes.AsNoTracking().ToDictionaryAsync(p => p.Parametro, ct);
        var regras = new ContratoFinanceiroCalculo.Regras(padroes);

        var autores = perfis.Select(p => p.DefinidoPorId).Concat(valores.Select(v => v.DefinidoPorId)).Distinct().ToList();
        var nomes = await db.Users.AsNoTracking().Where(u => autores.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);

        var vigente = PerfilVigente(perfis, hoje);
        var atuais = new List<FinanceiroParametroAtualDto>();
        foreach (var p in ParametrosDoGrupo(vigente?.Tipo, vigente?.ModeloPj))
        {
            var v = ContratoFinanceiroCalculo.Vigente(valores.Select(x => new CrmFinanceiroValor { Parametro = x.Parametro, VigenteDesde = x.VigenteDesde, Valor = x.Valor }).ToList(), p, hoje);
            var custoFixo = ContratoFinanceiroCalculo.CustosFixosPj.Contains(p);
            var valor = v?.Valor ?? (custoFixo ? regras.ValorPadrao(p) : (decimal?)null);
            var carencia = prazos.TryGetValue(p, out var z) && z.CarenciaMeses is { } c ? c : regras.CarenciaPadrao(p);
            atuais.Add(custoFixo
                ? new FinanceiroParametroAtualDto(p.ToString(), valor, v?.VigenteDesde, v is null, carencia, prazos.TryGetValue(p, out var zp) && zp.CarenciaMeses is not null, regras.CarenciaPadrao(p))
                : new FinanceiroParametroAtualDto(p.ToString(), valor, v?.VigenteDesde, false));
        }

        var membros = await db.Users.AsNoTracking().Where(u => u.GrupoId == grupoId).OrderBy(u => u.NomeCompleto).Select(u => new { u.Id, u.NomeCompleto }).ToListAsync(ct);
        var ids = membros.Select(m => m.Id).ToList();
        var desde = (await db.CrmGrupoMembros.AsNoTracking().Where(m => m.GrupoId == grupoId && ids.Contains(m.UsuarioId)).ToListAsync(ct))
            .GroupBy(m => m.UsuarioId).ToDictionary(g => g.Key, g => g.Max(m => m.DesdeMes));
        var proprios = await db.CrmFinanceiroPerfis.AsNoTracking().Where(p => ids.Contains(p.UsuarioId) && p.Tipo != null).ToDictionaryAsync(p => p.UsuarioId, p => p.Tipo, ct);

        return new GrupoContratoDto(
            grupo.Id, grupo.Nome, grupo.RegionalId, grupo.Regional.Nome, PodeEditar,
            vigente is null ? null : ParaDto(vigente, nomes),
            perfis.Select(p => ParaDto(p, nomes)).ToList(),
            atuais,
            valores.OrderBy(v => v.Parametro).ThenByDescending(v => v.VigenteDesde)
                .Select(v => new GrupoContratoValorDto(v.Id, v.Parametro.ToString(), v.VigenteDesde, v.Valor, v.Observacao, nomes.GetValueOrDefault(v.DefinidoPorId))).ToList(),
            ContratoFinanceiroCalculo.CustosFixosPj.Select(p => new GrupoContratoPrazoDto(p.ToString(), prazos.TryGetValue(p, out var z) ? z.CarenciaMeses : null, regras.CarenciaPadrao(p))).ToList(),
            membros.Select(m => new GrupoContratoConsultorDto(m.Id, m.NomeCompleto, desde.GetValueOrDefault(m.Id), (proprios.GetValueOrDefault(m.Id) ?? vigente?.Tipo)?.ToString())).ToList());
    }

    private static GrupoContratoPerfilDto ParaDto(CrmGrupoContratoPerfil p, IReadOnlyDictionary<Guid, string> nomes) =>
        new(p.VigenteDesde, p.Tipo?.ToString(), p.ModeloPj?.ToString(), p.Transporte.ToString(), nomes.GetValueOrDefault(p.DefinidoPorId));

    public Task SalvarPerfilAsync(Guid grupoId, GrupoPerfilRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => SalvarPerfilCoreAsync(grupoId, request, ct));

    private async Task SalvarPerfilCoreAsync(Guid grupoId, GrupoPerfilRequest request, CancellationToken ct)
    {
        await ExigirGrupoAsync(grupoId, paraEditar: true, ct);
        ValidarMes(request.VigenteDesde);

        TipoContrato? tipo = null;
        if (!string.IsNullOrWhiteSpace(request.Tipo))
        {
            if (!Enum.TryParse<TipoContrato>(request.Tipo, ignoreCase: true, out var t) || !Enum.IsDefined(t))
                throw new CrmBusinessException("Tipo de contrato inválido (use PJ ou CLT).", "financeiro_invalido");
            tipo = t;
        }
        ModeloRemuneracaoPj? modelo = null;
        if (!string.IsNullOrWhiteSpace(request.ModeloPj))
        {
            if (!Enum.TryParse<ModeloRemuneracaoPj>(request.ModeloPj, ignoreCase: true, out var m) || !Enum.IsDefined(m))
                throw new CrmBusinessException("Modelo de remuneração inválido (bônus por placa ou recorrência).", "financeiro_invalido");
            modelo = m;
        }
        if (tipo == TipoContrato.Pj && modelo is null)
            throw new CrmBusinessException("Escolha como o consultor PJ do grupo é remunerado: bônus por placa ou recorrência.", "financeiro_invalido");
        if (tipo != TipoContrato.Pj) modelo = null;
        var transporte = ModalidadeTransporte.Nenhum;
        if (!string.IsNullOrWhiteSpace(request.Transporte) && (!Enum.TryParse(request.Transporte, ignoreCase: true, out transporte) || !Enum.IsDefined(transporte)))
            throw new CrmBusinessException("Modalidade de transporte inválida.", "financeiro_invalido");
        if (tipo != TipoContrato.Pj) transporte = ModalidadeTransporte.Nenhum;

        var mes = ContratoFinanceiroCalculo.PrimeiroDia(request.VigenteDesde);
        var existente = await db.CrmGrupoContratoPerfis.FirstOrDefaultAsync(p => p.GrupoId == grupoId && p.VigenteDesde == mes, ct);
        var antes = existente is null ? null : new { Tipo = existente.Tipo?.ToString(), Modelo = existente.ModeloPj?.ToString(), Transporte = existente.Transporte.ToString() };
        if (existente is null) db.CrmGrupoContratoPerfis.Add(existente = new CrmGrupoContratoPerfil { GrupoId = grupoId, VigenteDesde = mes });
        existente.Tipo = tipo;
        existente.ModeloPj = modelo;
        existente.Transporte = transporte;
        existente.DefinidoPorId = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.grupo.perfil", nameof(CrmGrupoContratoPerfil), existente.Id,
            new { grupoId, mes, antes, depois = new { Tipo = tipo?.ToString(), Modelo = modelo?.ToString(), Transporte = transporte.ToString() } }, ct);
    }

    public Task DefinirValorAsync(Guid grupoId, GrupoValorRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => DefinirValorCoreAsync(grupoId, request, ct));

    private async Task DefinirValorCoreAsync(Guid grupoId, GrupoValorRequest request, CancellationToken ct)
    {
        await ExigirGrupoAsync(grupoId, paraEditar: true, ct);
        ValidarMes(request.VigenteDesde);
        if (!Enum.TryParse<ParametroFinanceiro>(request.Parametro, ignoreCase: true, out var parametro) || !Enum.IsDefined(parametro))
            throw new CrmBusinessException("Parâmetro financeiro inválido.", "financeiro_invalido");
        if (parametro is ParametroFinanceiro.Salario or ParametroFinanceiro.Recorrencia)
            throw new CrmBusinessException("Salário e recorrência são sempre individuais: defina no contrato do consultor.", "financeiro_invalido");
        if (request.Valor < 0 || request.Valor > FinanceiroValidador.ValorMaximo)
            throw new CrmBusinessException("Valor fora do limite permitido.", "financeiro_invalido");

        var mes = ContratoFinanceiroCalculo.PrimeiroDia(request.VigenteDesde);
        var vigente = PerfilVigente(await db.CrmGrupoContratoPerfis.AsNoTracking().Where(p => p.GrupoId == grupoId).ToListAsync(ct), mes);
        if (vigente?.Tipo is null)
            throw new CrmBusinessException("Defina primeiro se o contrato do grupo é PJ ou CLT.", "financeiro_sem_contrato");
        if (!ParametrosDoGrupo(vigente.Tipo, vigente.ModeloPj).Contains(parametro))
            throw new CrmBusinessException($"Este valor não se aplica a um grupo {(vigente.Tipo == TipoContrato.Pj ? "PJ" : "CLT")}.", "financeiro_invalido");

        var valor = FinanceiroValidador.Arredondar(request.Valor);
        var existente = await db.CrmGrupoContratoValores.FirstOrDefaultAsync(v => v.GrupoId == grupoId && v.Parametro == parametro && v.VigenteDesde == mes, ct);
        var anterior = existente?.Valor;
        if (existente is null) db.CrmGrupoContratoValores.Add(existente = new CrmGrupoContratoValor { GrupoId = grupoId, Parametro = parametro, VigenteDesde = mes });
        existente.Valor = valor;
        existente.Observacao = FinanceiroValidador.Limpar(request.Observacao) is { } o ? o[..Math.Min(o.Length, 300)] : null;
        existente.DefinidoPorId = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.grupo.valor", nameof(CrmGrupoContratoValor), existente.Id,
            new { grupoId, parametro = parametro.ToString(), mes, anterior, depois = valor }, ct);
    }

    public async Task RemoverValorAsync(Guid grupoId, Guid valorId, CancellationToken ct)
    {
        await ExigirGrupoAsync(grupoId, paraEditar: true, ct);
        var v = await db.CrmGrupoContratoValores.FirstOrDefaultAsync(x => x.Id == valorId && x.GrupoId == grupoId, ct)
            ?? throw new CrmNotFoundException("Valor", valorId);
        db.CrmGrupoContratoValores.Remove(v);
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.grupo.valor.removido", nameof(CrmGrupoContratoValor), valorId,
            new { grupoId, parametro = v.Parametro.ToString(), mes = v.VigenteDesde, valor = v.Valor }, ct);
    }

    public Task DefinirPrazoAsync(Guid grupoId, GrupoPrazoRequest request, CancellationToken ct) => SemDuplicidadeAsync(() => DefinirPrazoCoreAsync(grupoId, request, ct));

    private async Task DefinirPrazoCoreAsync(Guid grupoId, GrupoPrazoRequest request, CancellationToken ct)
    {
        await ExigirGrupoAsync(grupoId, paraEditar: true, ct);
        if (!Enum.TryParse<ParametroFinanceiro>(request.Parametro, ignoreCase: true, out var parametro) || !ContratoFinanceiroCalculo.CustosFixosPj.Contains(parametro))
            throw new CrmBusinessException("Este valor não tem prazo para começar a ser cobrado.", "financeiro_invalido");
        if (request.CarenciaMeses is < 0 or > CarenciaMaximaMeses)
            throw new CrmBusinessException($"O prazo deve ficar entre 0 e {CarenciaMaximaMeses} meses.", "financeiro_invalido");

        var prazo = await db.CrmGrupoContratoPrazos.FirstOrDefaultAsync(p => p.GrupoId == grupoId && p.Parametro == parametro, ct);
        var antes = prazo?.CarenciaMeses;
        if (prazo is null) db.CrmGrupoContratoPrazos.Add(prazo = new CrmGrupoContratoPrazo { GrupoId = grupoId, Parametro = parametro });
        prazo.CarenciaMeses = request.CarenciaMeses;
        prazo.DefinidoPorId = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("financeiro.grupo.prazo", nameof(CrmGrupoContratoPrazo), prazo.Id,
            new { grupoId, parametro = parametro.ToString(), antes, depois = request.CarenciaMeses }, ct);
    }

    private static void ValidarMes(DateOnly mes)
    {
        if (mes.Year < FinanceiroValidador.AnoMinimo || mes.Year > FinanceiroValidador.AnoMaximo)
            throw new CrmBusinessException("Mês de vigência fora do período permitido.", "financeiro_invalido");
    }

    /// <summary>Dois cliques (ou duas abas) ao mesmo tempo tentam criar a mesma linha (índice único): a que perde recomeça e atualiza a linha da outra.</summary>
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
}
