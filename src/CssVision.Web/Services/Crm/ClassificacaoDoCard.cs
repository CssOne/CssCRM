using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>Perguntas sobre o tipo do cartão que precisam do banco (o card de "Outro veículo" não carrega os sinais de anúncio).</summary>
public static class ClassificacaoDoCard
{
    /// <summary>
    /// O card é de tráfego pago (<see cref="OrigemLead.DeTrafegoPago"/>). O card de "Outro veículo" não carrega os sinais de rastreio do anúncio (não é um
    /// lead novo do tráfego), então vale o do card principal do cliente.
    /// </summary>
    public static async Task<bool> DeTrafegoPagoAsync(ApplicationDbContext db, CrmLead lead, CancellationToken ct)
    {
        if (OrigemLead.DeTrafegoPago(lead)) return true;
        if (lead.VeiculoAdicionalDeLeadId is not { } principalId) return false;

        var principal = await db.CrmLeads.AsNoTracking().Where(l => l.Id == principalId)
            .Select(l => new CrmLead
            {
                MetaLeadId = l.MetaLeadId, Gclid = l.Gclid, UtmSource = l.UtmSource, Campanha = l.Campanha,
                ConsentimentoOrigem = l.ConsentimentoOrigem, TagDeAnuncioNotion = l.TagDeAnuncioNotion,
            }).FirstOrDefaultAsync(ct);
        return principal is not null && OrigemLead.DeTrafegoPago(principal);
    }
}
