using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class PipelineService(ApplicationDbContext db, IEquipeComercialService equipe) : IPipelineService
{
    public async Task<PipelineBoardDto> ObterBoardAsync(PipelineFilterRequest filtro, CancellationToken ct)
    {
        var etapas = await db.CrmPipelineStages.AsNoTracking()
            .Where(s => s.Ativa || (filtro.IncluirFechadas && s.Tipo != TipoEtapaPipeline.Aberta))
            .OrderBy(s => s.Ordem)
            .ToListAsync(ct);

        var query = db.CrmOpportunities.AsNoTracking()
            .Include(o => o.Lead)
            .Include(o => o.Responsavel)
            .Where(o => !o.Arquivado);

        if (!filtro.IncluirFechadas) query = query.Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Aberta);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null) query = query.Where(o => visiveis.Contains(o.ResponsavelId));

        if (filtro.ResponsavelId.HasValue) query = query.Where(o => o.ResponsavelId == filtro.ResponsavelId);
        if (!string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(o => o.Lead.Origem == filtro.Origem);
        if (!string.IsNullOrWhiteSpace(filtro.ProdutoOuServico)) query = query.Where(o => o.ProdutoOuServico == filtro.ProdutoOuServico);
        if (!string.IsNullOrWhiteSpace(filtro.Regional)) query = query.Where(o => o.Lead.Regional == filtro.Regional);
        if (filtro.DataInicio.HasValue)
        {
            var inicio = filtro.DataInicio.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(o => o.CriadoEm >= inicio);
        }
        if (filtro.DataFim.HasValue)
        {
            var fim = filtro.DataFim.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(o => o.CriadoEm <= fim);
        }

        var oportunidades = await query.ToListAsync(ct);
        var opportunityIds = oportunidades.Select(o => o.Id).ToList();

        var proximasAtividades = await db.CrmActivities.AsNoTracking()
            .Where(a => a.OpportunityId != null && opportunityIds.Contains(a.OpportunityId.Value) && a.Status == StatusAtividade.Pendente)
            .GroupBy(a => a.OpportunityId!.Value)
            .Select(g => g.OrderBy(a => a.DataHoraPrevista).First())
            .ToListAsync(ct);
        var proximaPorOportunidade = proximasAtividades.ToDictionary(a => a.OpportunityId!.Value);

        var agora = DateTimeOffset.UtcNow;
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        var colunas = etapas.Select(etapa =>
        {
            var cartoes = oportunidades
                .Where(o => o.EtapaId == etapa.Id)
                .OrderByDescending(o => o.EtapaDesde)
                .Select(o =>
                {
                    proximaPorOportunidade.TryGetValue(o.Id, out var proxima);
                    var atrasada = (o.DataPrevistaFechamento.HasValue && o.DataPrevistaFechamento.Value < hoje && etapa.Tipo == TipoEtapaPipeline.Aberta)
                        || (proxima is not null && proxima.DataHoraPrevista < agora);

                    return new PipelineCardDto(
                        o.Id, o.LeadId, o.Lead.NomeOuRazaoSocial, o.Titulo, o.ProdutoOuServico, o.ValorEstimado,
                        o.ResponsavelId, o.Responsavel.NomeCompleto, o.Lead.Origem,
                        proxima?.DataHoraPrevista, proxima?.Assunto, o.EtapaDesde, atrasada, o.RowVersion);
                })
                .ToList();

            var etapaDto = new PipelineStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Tipo, etapa.Cor, etapa.Ativa);
            return new PipelineColumnDto(etapaDto, cartoes, cartoes.Sum(c => c.ValorEstimado));
        }).ToList();

        return new PipelineBoardDto(colunas);
    }
}
