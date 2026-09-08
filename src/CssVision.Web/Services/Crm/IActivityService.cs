using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IActivityService
{
    Task<PagedResult<ActivityDto>> ListarAsync(ActivityFilterRequest filtro, CancellationToken ct);
    Task<ActivityDto> CriarAsync(ActivityCreateRequest request, CancellationToken ct);
    Task<ActivityDto> AtualizarAsync(Guid id, ActivityUpdateRequest request, CancellationToken ct);
    Task<ActivityDto> ConcluirAsync(Guid id, ActivityCompleteRequest request, CancellationToken ct);
}
