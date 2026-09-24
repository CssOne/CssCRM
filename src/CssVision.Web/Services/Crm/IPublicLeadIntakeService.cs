using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

/// <summary>Ponto de entrada pra leads vindos de formulários públicos do site (sem usuário autenticado).</summary>
public interface IPublicLeadIntakeService
{
    Task<PublicLeadResultDto> CriarAsync(PublicLeadCreateRequest request, CancellationToken ct);

    /// <summary>
    /// Devolve o consultor sorteado pra um lead já criado, buscando por MetaLeadId (o lead_id do
    /// Meta). Null se não achar — usado pela página de obrigado do formulário instantâneo, que só
    /// recebe o lead_id (via parâmetro dinâmico {{leadgen_id}} no botão "Ver site") e precisa
    /// consultar depois quem foi atribuído, já que o rodízio roda no webhook, antes da pessoa
    /// clicar nesse botão.
    /// </summary>
    Task<PublicLeadResultDto?> ObterPorMetaLeadIdAsync(string metaLeadId, CancellationToken ct);
}
