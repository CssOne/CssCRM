using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface ILookupService
{
    Task<IReadOnlyList<PipelineStageDto>> ObterEtapasAsync(CancellationToken ct);
    Task<PipelineStageDto> CriarEtapaAsync(CreateStageRequest request, CancellationToken ct);
    Task<IReadOnlyList<LossReasonDto>> ObterMotivosPerdaAsync(CancellationToken ct);
    Task<LossReasonDto> CriarMotivoPerdaAsync(CreateLossReasonRequest request, CancellationToken ct);
    Task<IReadOnlyList<LeadStageDto>> ObterEtapasLeadAsync(CancellationToken ct);
    Task<LeadStageDto> CriarEtapaLeadAsync(CreateLeadStageRequest request, CancellationToken ct);
    Task<IReadOnlyList<string>> ObterOrigensAsync(CancellationToken ct);
    Task<IReadOnlyList<RegionalDto>> ObterRegionaisAsync(CancellationToken ct);
    Task<RegionalDto> CriarRegionalAsync(CreateRegionalRequest request, CancellationToken ct);
    Task<RegionalDto> AtualizarRegionalAsync(Guid id, UpdateRegionalRequest request, CancellationToken ct);
    Task<IReadOnlyList<GrupoDto>> ObterGruposAsync(Guid? regionalId, CancellationToken ct);
    Task<GrupoDto> CriarGrupoAsync(CreateGrupoRequest request, CancellationToken ct);
    Task<GrupoDto> AtualizarGrupoAsync(Guid id, UpdateGrupoRequest request, CancellationToken ct);
    Task<GrupoDto> AtualizarMembrosGrupoAsync(Guid id, UpdateGrupoMembrosRequest request, CancellationToken ct);
}
