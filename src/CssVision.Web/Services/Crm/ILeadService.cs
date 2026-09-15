using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public record CriarLeadResultado(LeadDetailDto? Lead, LeadDuplicateWarningDto? Duplicidade);

public interface ILeadService
{
    Task<PagedResult<LeadListItemDto>> ListarAsync(LeadFilterRequest filtro, CancellationToken ct);
    Task<LeadDetailDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<LeadTimelineItemDto>> ObterTimelineAsync(Guid id, CancellationToken ct);
    Task<CriarLeadResultado> CriarAsync(LeadCreateRequest request, CancellationToken ct);
    Task<LeadDetailDto> AtualizarAsync(Guid id, LeadUpdateRequest request, CancellationToken ct);
    Task AtribuirAsync(Guid id, LeadAssignRequest request, CancellationToken ct);
    Task<LeadDetailDto> MudarEtapaAsync(Guid id, ChangeLeadStageRequest request, CancellationToken ct);
    Task<int> AtribuirEmLoteAsync(LeadBulkAssignRequest request, CancellationToken ct);
    Task<LeadImportResultDto> ImportarAsync(Stream planilha, CancellationToken ct);
    Task<byte[]> ExportarAsync(LeadFilterRequest filtro, CancellationToken ct);
    Task<Guid> AdicionarNotaAsync(Guid leadId, string texto, CancellationToken ct);
}
