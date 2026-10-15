using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Notion;

/// <summary>
/// A mesma venda em dois leads: o consultor fechou a venda de um lead do tráfego pago (Meta/formulário do site) no CRM e, depois, registrou a venda no Notion num card
/// novo e quase vazio (só nome e "VENDA CONCLUIDA", sem tipo, sem "Indicação?", sem tag/campanha/ID do Meta). A sincronização cria um lead para o card; quando a placa é
/// preenchida depois, o card já está ligado ao lead novo e ninguém junta os dois. O lead do card ficava como "Indicação" (card sem tipo é indicação por padrão) e a venda
/// contava duas vezes: uma como tráfego pago, outra como indicação.
/// Aqui: o lead do Notion sem nenhum sinal de anúncio, com a mesma placa, o mesmo consultor e a venda na mesma época de um lead do tráfego pago com venda ganha, é o duplicado —
/// é arquivado (pelo sistema) junto com a venda dele. O lead do tráfego, que tem os dados reais, continua. A sincronização respeita lead arquivado, então o card não volta.
/// </summary>
public static class DuplicidadeDeVendaDoNotion
{
    private static readonly TimeSpan JanelaEntreAsVendas = TimeSpan.FromDays(7);

    private static bool VeioDoNotion(CrmLead l) =>
        l.ConsentimentoOrigem is OrigemLead.MarcadorMigracaoNotion or OrigemLead.MarcadorSincronizacaoNotion;

    private static DateTimeOffset? DataDaVenda(CrmOpportunity o) => o.AtivoEm ?? o.DataEfetivaFechamento;

    /// <summary>O lead do tráfego pago que tem a mesma venda do lead do Notion (um só), ou nulo.</summary>
    public static async Task<CrmLead?> LeadDeTrafegoDaMesmaVendaAsync(ApplicationDbContext db, CrmLead leadDoNotion, CancellationToken ct)
    {
        if (!VeioDoNotion(leadDoNotion) || leadDoNotion.Arquivado || leadDoNotion.TipoIndicacaoEditadoNoCrm) return null;
        if (leadDoNotion.Placa is not { Length: >= 6 } placa || leadDoNotion.ResponsavelId is not { } responsavelId) return null;
        // Já tem sinal próprio de anúncio: não é o card vazio.
        if (leadDoNotion.MetaLeadId is not null || leadDoNotion.Gclid is not null || leadDoNotion.UtmSource is not null
            || leadDoNotion.TagDeAnuncioNotion || !string.IsNullOrWhiteSpace(leadDoNotion.Campanha)) return null;

        var vendasDoNotion = await db.CrmOpportunities.AsNoTracking()
            .Where(o => o.LeadId == leadDoNotion.Id && !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho)
            .ToListAsync(ct);
        if (vendasDoNotion.Count == 0) return null;

        var candidatos = await db.CrmLeads.AsNoTracking()
            .Where(l => l.Id != leadDoNotion.Id && !l.Arquivado && l.VeiculoAdicionalDeLeadId == null && l.Placa == placa && l.ResponsavelId == responsavelId
                && l.ConsentimentoOrigem != OrigemLead.MarcadorMigracaoNotion && l.ConsentimentoOrigem != OrigemLead.MarcadorSincronizacaoNotion
                && (l.MetaLeadId != null || l.ConsentimentoOrigem == OrigemLead.MarcadorFormularioSite))
            .Select(l => new { Lead = l, Vendas = l.Oportunidades.Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho).ToList() })
            .ToListAsync(ct);
        var mesmaVenda = candidatos
            .Where(c => c.Vendas.Any(v => vendasDoNotion.Any(n => DataDaVenda(n) is { } dn && DataDaVenda(v) is { } dv && (dn - dv).Duration() <= JanelaEntreAsVendas)))
            .Select(c => c.Lead)
            .ToList();
        return mesmaVenda.Count == 1 ? mesmaVenda[0] : null;
    }

    /// <summary>Arquiva o lead do Notion (e as vendas dele) se ele duplica a venda de um lead do tráfego pago. Salva. Devolve true se arquivou.</summary>
    public static async Task<bool> ArquivarSeDuplicadoDeTrafegoAsync(ApplicationDbContext db, CrmLead leadDoNotion, CancellationToken ct)
    {
        if (await LeadDeTrafegoDaMesmaVendaAsync(db, leadDoNotion, ct) is null) return false;

        var agora = DateTimeOffset.UtcNow;
        var lead = await db.CrmLeads.FirstAsync(l => l.Id == leadDoNotion.Id, ct);
        lead.Arquivado = true;
        lead.ArquivadoEm = agora;
        lead.ArquivadoPorId = null; // pelo sistema: a sincronização respeita e não recria
        foreach (var venda in await db.CrmOpportunities.Where(o => o.LeadId == lead.Id && !o.Arquivado).ToListAsync(ct))
        {
            venda.Arquivado = true;
            venda.ArquivadoEm = agora;
            venda.ArquivadoPorId = null;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}
