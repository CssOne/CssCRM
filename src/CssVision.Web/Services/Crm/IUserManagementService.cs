using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IUserManagementService
{
    Task<PagedResult<UserSummaryDto>> ListarAsync(UserFilterRequest filtro, CancellationToken ct);
    Task<UserSummaryDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<UserSummaryDto> CriarAsync(UserCreateRequest request, CancellationToken ct);
    Task<UserSummaryDto> AtualizarAsync(Guid id, UserUpdateRequest request, CancellationToken ct);
    Task RedefinirSenhaAsync(Guid id, ResetPasswordRequest request, CancellationToken ct);
    Task ExcluirAsync(Guid id, CancellationToken ct);
}
