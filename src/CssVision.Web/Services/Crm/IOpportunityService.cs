using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using Microsoft.AspNetCore.Http;

namespace CssVision.Web.Services.Crm;

public interface IOpportunityService
{
    Task<PagedResult<OpportunityDto>> ListarAsync(OpportunityFilterRequest filtro, CancellationToken ct);
    Task<OpportunityDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<OpportunityDto> CriarAsync(OpportunityCreateRequest request, CancellationToken ct);
    Task<OpportunityDto> AtualizarAsync(Guid id, OpportunityUpdateRequest request, CancellationToken ct);
    Task<OpportunityDto> MudarEtapaAsync(Guid id, ChangeStageRequest request, CancellationToken ct);
    Task ExcluirAsync(Guid id, CancellationToken ct);
    Task<OpportunityDto> AnexarArquivoAsync(Guid id, string tipo, IFormFile arquivo, CancellationToken ct);
}
