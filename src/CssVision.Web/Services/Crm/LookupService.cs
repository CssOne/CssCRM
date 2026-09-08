using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LookupService(ApplicationDbContext db, ICurrentUserService currentUser) : ILookupService
{
    public async Task<IReadOnlyList<PipelineStageDto>> ObterEtapasAsync(CancellationToken ct) =>
        await db.CrmPipelineStages.AsNoTracking()
            .OrderBy(s => s.Ordem)
            .Select(s => new PipelineStageDto(s.Id, s.Nome, s.Ordem, s.Tipo, s.Cor, s.Ativa))
            .ToListAsync(ct);

    public async Task<PipelineStageDto> CriarEtapaAsync(CreateStageRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (await db.CrmPipelineStages.AnyAsync(s => s.Nome == request.Nome, ct))
        {
            throw new CrmBusinessException("Já existe uma etapa com este nome.", "etapa_duplicada");
        }

        var etapa = new CrmPipelineStage
        {
            Nome = request.Nome.Trim(),
            Ordem = request.Ordem,
            Tipo = TipoEtapaPipeline.Aberta,
            Cor = request.Cor
        };

        db.CrmPipelineStages.Add(etapa);
        await db.SaveChangesAsync(ct);

        return new PipelineStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Tipo, etapa.Cor, etapa.Ativa);
    }

    public async Task<IReadOnlyList<LossReasonDto>> ObterMotivosPerdaAsync(CancellationToken ct) =>
        await db.CrmLossReasons.AsNoTracking()
            .Where(m => m.Ativo)
            .OrderBy(m => m.Descricao)
            .Select(m => new LossReasonDto(m.Id, m.Descricao, m.Ativo))
            .ToListAsync(ct);

    public async Task<LossReasonDto> CriarMotivoPerdaAsync(CreateLossReasonRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (await db.CrmLossReasons.AnyAsync(m => m.Descricao == request.Descricao, ct))
        {
            throw new CrmBusinessException("Já existe um motivo de perda com esta descrição.", "motivo_duplicado");
        }

        var motivo = new CrmLossReason { Descricao = request.Descricao.Trim() };
        db.CrmLossReasons.Add(motivo);
        await db.SaveChangesAsync(ct);

        return new LossReasonDto(motivo.Id, motivo.Descricao, motivo.Ativo);
    }

    public async Task<IReadOnlyList<LeadStageDto>> ObterEtapasLeadAsync(CancellationToken ct) =>
        await db.CrmLeadStages.AsNoTracking()
            .Where(s => s.Ativa)
            .OrderBy(s => s.Ordem)
            .Select(s => new LeadStageDto(s.Id, s.Nome, s.Ordem, s.Cor, s.Fechada, s.Ativa))
            .ToListAsync(ct);

    public async Task<LeadStageDto> CriarEtapaLeadAsync(CreateLeadStageRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (await db.CrmLeadStages.AnyAsync(s => s.Nome == request.Nome, ct))
        {
            throw new CrmBusinessException("Já existe uma etapa de lead com este nome.", "etapa_duplicada");
        }

        var etapa = new CrmLeadStage
        {
            Nome = request.Nome.Trim(),
            Ordem = request.Ordem,
            Cor = request.Cor,
            Fechada = request.Fechada
        };

        db.CrmLeadStages.Add(etapa);
        await db.SaveChangesAsync(ct);

        return new LeadStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Cor, etapa.Fechada, etapa.Ativa);
    }

    private void ExigirGestaoComercial()
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem alterar as configurações do funil.");
        }
    }
}
