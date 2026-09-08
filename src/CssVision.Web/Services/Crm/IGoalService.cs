using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IGoalService
{
    Task<IReadOnlyList<SalesGoalDto>> ListarAsync(DateOnly? mesReferencia, CancellationToken ct);
    Task<SalesGoalDto> DefinirMetaAsync(SalesGoalUpsertRequest request, CancellationToken ct);
}
