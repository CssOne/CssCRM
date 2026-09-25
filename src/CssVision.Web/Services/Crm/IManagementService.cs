using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IManagementService
{
    Task<GestaoComercialResumoDto> ObterResumoAsync(DateOnly? dataInicio, DateOnly? dataFim, CancellationToken ct);
    /// <param name="incluirInativos">Também os vendedores com o usuário inativo (filtros que olham a carteira antiga).</param>
    Task<IReadOnlyList<VendedorResumoDto>> ObterVendedoresAsync(CancellationToken ct, bool incluirInativos = false);
    Task<IReadOnlyList<ConsultorDesempenhoDto>> ObterDesempenhoConsultoresAsync(DateOnly? mesReferencia, CancellationToken ct);
    Task AtualizarLimiteMensalAsync(Guid vendedorId, AtualizarLimiteMensalRequest request, CancellationToken ct);
    Task AtualizarLimiteDiarioAsync(Guid vendedorId, AtualizarLimiteDiarioRequest request, CancellationToken ct);
    Task AtualizarRecebeLeadsAsync(Guid vendedorId, AtualizarRecebeLeadsRequest request, CancellationToken ct);
    Task<IReadOnlyList<RedistribuicaoHistoricoDto>> ObterHistoricoRedistribuicoesAsync(CancellationToken ct);
}
