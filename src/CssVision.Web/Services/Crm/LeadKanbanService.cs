using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
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
            .AsQueryable();

        if (!filtro.IncluirArquivados) query = query.Where(l => !l.Arquivado);
        query = query.Where(l => l.CriadoManualmente == filtro.CriadoManualmente);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null)
        {
            query = query.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();
            var buscaDigitos = DocumentValidation.SomenteDigitos(busca);
            query = query.Where(l =>
                EF.Functions.ILike(l.NomeOuRazaoSocial, $"%{busca}%") ||
                (l.Email != null && EF.Functions.ILike(l.Email, $"%{busca}%")) ||
                (buscaDigitos != "" && l.DocumentoNormalizado != null && l.DocumentoNormalizado.Contains(buscaDigitos)) ||
                (buscaDigitos != "" && l.TelefoneNormalizado != null && l.TelefoneNormalizado.Contains(buscaDigitos)));
        }

        if (filtro.ResponsavelId.HasValue) query = query.Where(l => l.ResponsavelId == filtro.ResponsavelId);
        if (!string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(l => l.Origem == filtro.Origem);
        if (!string.IsNullOrWhiteSpace(filtro.Regional)) query = query.Where(l => l.Regional == filtro.Regional);

        var leads = await query.ToListAsync(ct);

        LeadKanbanCardDto ParaCartao(CrmLead l) => new(
            l.Id, l.NomeOuRazaoSocial, l.Telefone, l.Email, l.Estado, l.Origem, l.Campanha,
            l.Placa, l.TemSeguro, l.UtilidadeVeiculo,
            l.ResponsavelId, l.Responsavel?.NomeCompleto,
            l.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
            l.CriadoEm, l.UltimoContatoEm, l.UltimoContatoEm == null, l.Arquivado, l.RowVersion);

        // Coluna virtual (sem linha em CrmLeadStage): leads que ainda não foram trabalhados por
        // ninguém. Fica sempre em primeiro, pra vendedora enxergar de cara quem ainda não pegou.
        var semEtapa = new LeadKanbanColumnDto(
            new LeadStageDto(null, "Sem etapa", -1, "#94a3b8", false, true),
            leads.Where(l => l.EtapaId is null).OrderByDescending(l => l.CriadoEm).Select(ParaCartao).ToList());

        var colunas = new List<LeadKanbanColumnDto> { semEtapa };
        colunas.AddRange(etapas.Select(etapa =>
        {
            var cartoes = leads
                .Where(l => l.EtapaId == etapa.Id)
                .OrderByDescending(l => l.CriadoEm)
                .Select(ParaCartao)
                .ToList();

            var etapaDto = new LeadStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Cor, etapa.Fechada, etapa.Ativa);
            return new LeadKanbanColumnDto(etapaDto, cartoes);
        }));

        return new LeadKanbanBoardDto(colunas);
    }
}
