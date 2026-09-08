using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface ILookupService
{
    Task<IReadOnlyList<PipelineStageDto>> ObterEtapasAsync(CancellationToken ct);
    Task<PipelineStageDto> CriarEtapaAsync(CreateStageRequest request, CancellationToken ct);
    Task<IReadOnlyList<LossReasonDto>> ObterMotivosPerdaAsync(CancellationToken ct);
    Task<LossReasonDto> CriarMotivoPerdaAsync(CreateLossReasonRequest request, CancellationToken ct);
}
