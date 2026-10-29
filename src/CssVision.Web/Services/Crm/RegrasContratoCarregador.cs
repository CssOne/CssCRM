using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>Carrega do banco o que vale além do histórico de valores do consultor: o padrão da gestão para todos os PJ e os prazos próprios dele.</summary>
public static class RegrasContratoCarregador
{
    public static async Task<ContratoFinanceiroCalculo.Regras> CarregarAsync(ApplicationDbContext db, Guid consultorId, CancellationToken ct)
    {
        var padroes = await db.CrmFinanceiroPadroes.AsNoTracking().ToDictionaryAsync(p => p.Parametro, ct);
        var prazos = await db.CrmFinanceiroPrazos.AsNoTracking().Where(p => p.UsuarioId == consultorId).ToDictionaryAsync(p => p.Parametro, ct);
        var inicio = await db.CrmFinanceiroPerfis.AsNoTracking().Where(p => p.UsuarioId == consultorId).Select(p => p.InicioContrato).FirstOrDefaultAsync(ct);
        // Quem herda o contrato do grupo conta os leads desde que entrou nele.
        var entradaNoGrupo = await db.CrmGrupoMembros.AsNoTracking().Where(m => m.UsuarioId == consultorId && m.GrupoId != null).Select(m => (DateOnly?)m.DesdeMes).MinAsync(ct);
        if (entradaNoGrupo is { } entrada && (inicio is null || entrada < inicio)) inicio = entrada;
        var leads = inicio is { } desde ? (await LeadsDeTrafegoPorMes.ContarAsync(db, [consultorId], desde, ct))[consultorId] : null;
        return new ContratoFinanceiroCalculo.Regras(padroes, prazos, leads);
    }
}
