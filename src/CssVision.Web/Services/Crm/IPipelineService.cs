using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IPipelineService
{
    Task<PipelineBoardDto> ObterBoardAsync(PipelineFilterRequest filtro, CancellationToken ct);
}
