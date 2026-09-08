using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LeadKanbanService(ApplicationDbContext db, IEquipeComercialService equipe) : ILeadKanbanService
{
    public async Task<LeadKanbanBoardDto> ObterBoardAsync(LeadKanbanFilterRequest filtro, CancellationToken ct)
    {
        var etapas = await db.CrmLeadStages.AsNoTracking()
            .Where(s => s.Ativa)
            .OrderBy(s => s.Ordem)
            .ToListAsync(ct);

        var query = db.CrmLeads.AsNoTracking()
            .Include(l => l.Responsavel)
            .Include(l => l.LeadTags).ThenInclude(lt => lt.Tag)
            .Where(l => !l.Arquivado);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null)
        {
            query = query.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));
        }

        if (filtro.ResponsavelId.HasValue) query = query.Where(l => l.ResponsavelId == filtro.ResponsavelId);
        if (!string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(l => l.Origem == filtro.Origem);
        if (!string.IsNullOrWhiteSpace(filtro.Regional)) query = query.Where(l => l.Regional == filtro.Regional);

        var leads = await query.ToListAsync(ct);

        var colunas = etapas.Select(etapa =>
        {
            var cartoes = leads
                .Where(l => l.EtapaId == etapa.Id)
                .OrderByDescending(l => l.CriadoEm)
                .Select(l => new LeadKanbanCardDto(
                    l.Id, l.NomeOuRazaoSocial, l.Telefone, l.Email, l.Origem, l.Campanha,
                    l.ResponsavelId, l.Responsavel?.NomeCompleto,
                    l.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
                    l.CriadoEm, l.UltimoContatoEm, l.UltimoContatoEm == null, l.RowVersion))
                .ToList();

            var etapaDto = new LeadStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Cor, etapa.Fechada, etapa.Ativa);
            return new LeadKanbanColumnDto(etapaDto, cartoes);
        }).ToList();

        return new LeadKanbanBoardDto(colunas);
    }
}
