using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// O contrato que vale para o consultor em cada mês, juntando o que é dele com o que vem do grupo em que ele estava naquele mês. Ordem de precedência,
/// valor por valor: o do próprio consultor, depois o do grupo, depois o padrão geral da gestão, depois o do sistema (as duas últimas camadas continuam
/// em <see cref="ContratoFinanceiroCalculo.Regras"/>). Salário e recorrência são sempre individuais: o grupo nunca os define.
/// Sem nenhum grupo no histórico (<see cref="TemGrupo"/> falso) o resultado é exatamente o contrato próprio, como sempre foi.
/// </summary>
public sealed class ContratoEfetivo
{
    private static readonly ParametroFinanceiro[] SoIndividuais = [ParametroFinanceiro.Salario, ParametroFinanceiro.Recorrencia];

    private readonly List<CrmGrupoMembro> _membros;
    private readonly IReadOnlyCollection<CrmGrupoContratoPerfil> _perfisDoGrupo;
    private readonly IReadOnlyCollection<CrmGrupoContratoValor> _valoresDoGrupo;
    private readonly IReadOnlyCollection<CrmGrupoContratoPrazo> _prazosDoGrupo;
    private readonly IReadOnlyDictionary<ParametroFinanceiro, CrmFinanceiroPrazo>? _prazosProprios;

    public ContratoEfetivo(
        CrmFinanceiroPerfil? proprio,
        IReadOnlyCollection<CrmFinanceiroValor> valoresProprios,
        IEnumerable<CrmGrupoMembro>? membros = null,
        IReadOnlyCollection<CrmGrupoContratoPerfil>? perfisDoGrupo = null,
        IReadOnlyCollection<CrmGrupoContratoValor>? valoresDoGrupo = null,
        IReadOnlyCollection<CrmGrupoContratoPrazo>? prazosDoGrupo = null,
        IReadOnlyDictionary<ParametroFinanceiro, CrmFinanceiroPrazo>? prazosProprios = null)
    {
        Proprio = proprio;
        ValoresProprios = valoresProprios;
        _membros = (membros ?? []).OrderBy(m => m.DesdeMes).ToList();
        _perfisDoGrupo = perfisDoGrupo ?? [];
        _valoresDoGrupo = valoresDoGrupo ?? [];
        _prazosDoGrupo = prazosDoGrupo ?? [];
        _prazosProprios = prazosProprios;
    }

    public CrmFinanceiroPerfil? Proprio { get; }
    public IReadOnlyCollection<CrmFinanceiroValor> ValoresProprios { get; }

    /// <summary>Histórico de grupos, do mais antigo para o mais novo (uma linha sem grupo = saiu de qualquer grupo).</summary>
    public IReadOnlyList<CrmGrupoMembro> Membros => _membros;

    /// <summary>O consultor esteve em algum grupo? Se não, nada daqui muda o cálculo.</summary>
    public bool TemGrupo => _membros.Any(m => m.GrupoId is not null);

    /// <summary>Primeiro mês em que o consultor tem contrato: o início do contrato próprio ou a entrada no primeiro grupo, o que vier antes.</summary>
    public DateOnly? PrimeiroMes()
    {
        var datas = _membros.Where(m => m.GrupoId is not null).Select(m => m.DesdeMes)
            .Concat(Proprio?.InicioContrato is { } i ? [i] : []).ToList();
        return datas.Count == 0 ? null : ContratoFinanceiroCalculo.PrimeiroDia(datas.Min());
    }

    /// <summary>Em que grupo (e desde quando) o consultor estava no mês: a última linha do histórico que começou até ele.</summary>
    public CrmGrupoMembro? MembroNoMes(DateOnly mes)
    {
        var inicio = ContratoFinanceiroCalculo.PrimeiroDia(mes);
        return _membros.LastOrDefault(m => m.DesdeMes <= inicio);
    }

    /// <summary>
    /// Perfil do mês. O consultor com contrato próprio (tipo definido) mantém o dele; sem ele, vale o do grupo em que estava, com o início do contrato
    /// contado da entrada no grupo (ou o início que a gestão ajustou para ele).
    /// </summary>
    public CrmFinanceiroPerfil? PerfilNoMes(DateOnly mes)
    {
        if (Proprio?.Tipo is not null) return Proprio;
        var membro = MembroNoMes(mes);
        if (membro?.GrupoId is not { } grupoId) return Proprio;

        var inicio = ContratoFinanceiroCalculo.PrimeiroDia(mes);
        var doGrupo = _perfisDoGrupo.Where(p => p.GrupoId == grupoId && p.VigenteDesde <= inicio).OrderByDescending(p => p.VigenteDesde).FirstOrDefault();
        if (doGrupo?.Tipo is null) return Proprio;

        return new CrmFinanceiroPerfil
        {
            UsuarioId = Proprio?.UsuarioId ?? membro.UsuarioId,
            Tipo = doGrupo.Tipo,
            ModeloPj = doGrupo.Tipo == TipoContrato.Pj ? doGrupo.ModeloPj : null,
            Transporte = doGrupo.Tipo == TipoContrato.Clt ? ModalidadeTransporte.Nenhum : doGrupo.Transporte,
            InicioContrato = Proprio?.InicioContrato ?? membro.DesdeMes,
        };
    }

    /// <summary>Valores do mês: os do consultor e, para cada parâmetro que ele não definiu até o mês, o que o grupo dele tinha definido.</summary>
    public IReadOnlyCollection<CrmFinanceiroValor> ValoresNoMes(DateOnly mes)
    {
        if (MembroNoMes(mes)?.GrupoId is not { } grupoId) return ValoresProprios;

        var inicio = ContratoFinanceiroCalculo.PrimeiroDia(mes);
        var usuarioId = Proprio?.UsuarioId ?? _membros[0].UsuarioId;
        var herdados = _valoresDoGrupo
            .Where(v => v.GrupoId == grupoId && v.VigenteDesde <= inicio && !SoIndividuais.Contains(v.Parametro))
            .GroupBy(v => v.Parametro)
            .Where(g => !ValoresProprios.Any(p => p.Parametro == g.Key && p.VigenteDesde <= inicio))
            .Select(g => g.OrderByDescending(v => v.VigenteDesde).First())
            .Select(v => new CrmFinanceiroValor
            {
                UsuarioId = usuarioId, Parametro = v.Parametro, VigenteDesde = v.VigenteDesde, Valor = v.Valor, Observacao = v.Observacao, DefinidoPorId = v.DefinidoPorId,
            });
        return [.. ValoresProprios, .. herdados];
    }

    /// <summary>Carências do mês: a do consultor, quando ele tem uma, e senão a do grupo em que estava. Mantém a confirmação de aviso do consultor.</summary>
    public IReadOnlyDictionary<ParametroFinanceiro, CrmFinanceiroPrazo>? PrazosNoMes(DateOnly mes)
    {
        if (MembroNoMes(mes)?.GrupoId is not { } grupoId) return _prazosProprios;

        var prazos = _prazosProprios is null ? [] : new Dictionary<ParametroFinanceiro, CrmFinanceiroPrazo>(_prazosProprios);
        foreach (var g in _prazosDoGrupo.Where(p => p.GrupoId == grupoId && p.CarenciaMeses is not null))
        {
            if (prazos.TryGetValue(g.Parametro, out var proprio) && proprio.CarenciaMeses is not null) continue;
            prazos[g.Parametro] = new CrmFinanceiroPrazo
            {
                UsuarioId = Proprio?.UsuarioId ?? Guid.Empty, Parametro = g.Parametro, CarenciaMeses = g.CarenciaMeses,
                AlertaConfirmadoPara = proprio?.AlertaConfirmadoPara, DefinidoPorId = g.DefinidoPorId,
            };
        }
        return prazos;
    }

    /// <summary>
    /// O perfil que representa o ano: o do mês atual (ou de dezembro, em ano passado) e, se nesse mês não houver contrato, o do último mês do ano que
    /// tenha. Para quem nunca esteve em grupo é sempre o perfil próprio.
    /// </summary>
    public CrmFinanceiroPerfil? PerfilDoAno(int ano, DateOnly hoje)
    {
        var ref_ = ano == hoje.Year ? hoje.Month : ano < hoje.Year ? 12 : 1;
        var doMes = PerfilNoMes(new DateOnly(ano, ref_, 1));
        if (doMes?.Tipo is not null || !TemGrupo) return doMes;
        for (var m = 12; m >= 1; m--)
            if (PerfilNoMes(new DateOnly(ano, m, 1)) is { Tipo: not null } p) return p;
        return doMes;
    }

    /// <summary>Perfil, valores e prazos de hoje: o que as telas e os avisos de "começa a ser cobrado" usam.</summary>
    public (CrmFinanceiroPerfil? Perfil, IReadOnlyCollection<CrmFinanceiroValor> Valores, IReadOnlyDictionary<ParametroFinanceiro, CrmFinanceiroPrazo>? Prazos) Atual(DateOnly hoje) =>
        (PerfilNoMes(hoje), ValoresNoMes(hoje), PrazosNoMes(hoje));
}

/// <summary>Carrega do banco o contrato do consultor e, se ele já esteve em grupo, o contrato desses grupos.</summary>
public static class ContratoEfetivoCarregador
{
    public static async Task<ContratoEfetivo> CarregarAsync(ApplicationDbContext db, Guid consultorId, CancellationToken ct)
    {
        var perfil = await db.CrmFinanceiroPerfis.AsNoTracking().FirstOrDefaultAsync(p => p.UsuarioId == consultorId, ct);
        var valores = await db.CrmFinanceiroValores.AsNoTracking().Where(v => v.UsuarioId == consultorId).ToListAsync(ct);
        var prazos = await db.CrmFinanceiroPrazos.AsNoTracking().Where(p => p.UsuarioId == consultorId).ToDictionaryAsync(p => p.Parametro, ct);
        var membros = await db.CrmGrupoMembros.AsNoTracking().Where(m => m.UsuarioId == consultorId).ToListAsync(ct);

        var grupos = membros.Where(m => m.GrupoId is not null).Select(m => m.GrupoId!.Value).Distinct().ToList();
        if (grupos.Count == 0) return new ContratoEfetivo(perfil, valores, membros, prazosProprios: prazos);

        return new ContratoEfetivo(perfil, valores, membros,
            await db.CrmGrupoContratoPerfis.AsNoTracking().Where(p => grupos.Contains(p.GrupoId)).ToListAsync(ct),
            await db.CrmGrupoContratoValores.AsNoTracking().Where(v => grupos.Contains(v.GrupoId)).ToListAsync(ct),
            await db.CrmGrupoContratoPrazos.AsNoTracking().Where(p => grupos.Contains(p.GrupoId)).ToListAsync(ct),
            prazos);
    }
}
