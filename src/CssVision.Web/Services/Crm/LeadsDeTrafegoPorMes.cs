using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Quantos leads de tráfego pago (sistema novo + Notion) cada consultor pegou em cada mês — a mesma conta de "leads pegos no mês" da Gestão comercial
/// (lead com sinal de anúncio, fora da lixeira e não arquivado, no mês em que passou para o responsável atual, horário de Brasília). Base do custo
/// real do lead: valor por lead do mês vezes esta quantidade.
/// </summary>
public static class LeadsDeTrafegoPorMes
{
    public static async Task<Dictionary<Guid, Dictionary<DateOnly, int>>> ContarAsync(ApplicationDbContext db, IReadOnlyCollection<Guid> consultorIds, DateOnly desde, CancellationToken ct)
    {
        var resultado = consultorIds.ToDictionary(id => id, _ => new Dictionary<DateOnly, int>());
        if (consultorIds.Count == 0) return resultado;

        var inicio = HorarioBrasilia.Inicio(ContratoFinanceiroCalculo.PrimeiroDia(desde));
        var ids = consultorIds.Select(i => (Guid?)i).ToList();
        // Só a data e o responsável de cada lead: o agrupamento por mês de Brasília é feito aqui, numa consulta só para todo o período.
        var datas = await db.CrmLeads.AsNoTracking()
            .Where(LixeiraDoQuadro.ForaDaLixeira)
            .Where(l => ids.Contains(l.ResponsavelId) && !l.Arquivado)
            .Where(OrigemLead.DeTrafegoPagoInclusiveNotion)
            .Where(OrigemLead.ComPosseDesde(inicio))
            .Select(l => new
            {
                Responsavel = l.ResponsavelId!.Value,
                Data = l.ConsentimentoOrigem == OrigemLead.MarcadorMigracaoNotion || l.ConsentimentoOrigem == OrigemLead.MarcadorSincronizacaoNotion
                    ? l.CriadoEm
                    : (l.ResponsavelAtribuidoEm ?? l.CriadoEm),
            })
            .ToListAsync(ct);

        foreach (var d in datas)
        {
            var mes = ContratoFinanceiroCalculo.PrimeiroDia(HorarioBrasilia.Dia(d.Data));
            var porMes = resultado[d.Responsavel];
            porMes[mes] = porMes.GetValueOrDefault(mes) + 1;
        }
        return resultado;
    }
}
