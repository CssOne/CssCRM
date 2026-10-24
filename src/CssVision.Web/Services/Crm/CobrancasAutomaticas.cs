using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Cobranças que chegam sozinhas, mês a mês, para o consultor que ainda não tem contrato definido: coworking, tráfego pago e custo de lead, com os valores
/// padrão que a gestão definiu para todos. Valem a partir do mês em que o padrão foi definido (e depois da carência, contada da entrada do consultor).
/// Quem tem contrato (PJ) segue o contrato; o CLT não tem essas cobranças.
/// </summary>
public static class CobrancasAutomaticas
{
    /// <summary>Perfil e regras "implícitos" do consultor sem contrato, ou nulo se a gestão ainda não definiu nenhum valor padrão.</summary>
    public static (CrmFinanceiroPerfil Perfil, ContratoFinanceiroCalculo.Regras Regras)? Montar(
        Guid consultorId, DateTimeOffset contaCriadaEm, IReadOnlyDictionary<ParametroFinanceiro, CrmFinanceiroPadrao> padroes,
        IReadOnlyDictionary<ParametroFinanceiro, CrmFinanceiroPrazo> prazos, IReadOnlyDictionary<DateOnly, int>? leadsPorMes)
    {
        var liberadoEm = padroes
            .Where(p => ContratoFinanceiroCalculo.CustosFixosPj.Contains(p.Key) && p.Value.Valor is not null)
            .ToDictionary(p => p.Key, p => ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Dia(p.Value.CriadoEm)));
        if (liberadoEm.Count == 0) return null;

        var perfil = new CrmFinanceiroPerfil
        {
            UsuarioId = consultorId, Tipo = TipoContrato.Pj, ModeloPj = ModeloRemuneracaoPj.BonusPorPlaca,
            InicioContrato = ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Dia(contaCriadaEm)),
        };
        return (perfil, new ContratoFinanceiroCalculo.Regras(padroes, prazos, leadsPorMes, liberadoEm));
    }

    /// <summary>Carrega o necessário para um consultor (ativo, com papel Comercial e sem contrato definido); nulo nos demais casos.</summary>
    public static async Task<(CrmFinanceiroPerfil Perfil, ContratoFinanceiroCalculo.Regras Regras)?> CarregarAsync(ApplicationDbContext db, Guid consultorId, CancellationToken ct)
    {
        var temContrato = await db.CrmFinanceiroPerfis.AsNoTracking().AnyAsync(p => p.UsuarioId == consultorId && p.Tipo != null, ct);
        if (temContrato) return null;
        var conta = await db.Users.AsNoTracking()
            .Where(u => u.Id == consultorId && u.Ativo)
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Comercial)))
            .Select(u => new { u.CriadoEm })
            .FirstOrDefaultAsync(ct);
        if (conta is null) return null;

        var padroes = await db.CrmFinanceiroPadroes.AsNoTracking().ToDictionaryAsync(p => p.Parametro, ct);
        var prazos = await db.CrmFinanceiroPrazos.AsNoTracking().Where(p => p.UsuarioId == consultorId).ToDictionaryAsync(p => p.Parametro, ct);
        var leads = (await LeadsDeTrafegoPorMes.ContarAsync(db, [consultorId], ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Dia(conta.CriadoEm)), ct))[consultorId];
        return Montar(consultorId, conta.CriadoEm, padroes, prazos, leads);
    }
}
