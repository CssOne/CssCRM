using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

/// <summary>Ponto de entrada pra leads vindos de formulários públicos do site (sem usuário autenticado).</summary>
public interface IPublicLeadIntakeService
{
    Task<PublicLeadResultDto> CriarAsync(PublicLeadCreateRequest request, CancellationToken ct);
}
