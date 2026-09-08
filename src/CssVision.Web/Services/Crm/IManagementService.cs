using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IManagementService
{
    Task<GestaoComercialResumoDto> ObterResumoAsync(DateOnly? dataInicio, DateOnly? dataFim, CancellationToken ct);
    Task<IReadOnlyList<VendedorResumoDto>> ObterVendedoresAsync(CancellationToken ct);
    Task<IReadOnlyList<RedistribuicaoHistoricoDto>> ObterHistoricoRedistribuicoesAsync(CancellationToken ct);
}
