using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Corpo enviado pelos formulários públicos do site (fora do CRM autenticado).</summary>
public record PublicLeadCreateRequest(
    string Nome,
    string? WhatsApp,
    string? Email,
    string? Estado,
    string? Placa,
    string? Veiculo,
    string? Gclid,
    string? ClickId,
    string? MetaEmail,
    string? MetaLeadId,
    string? Fonte,
    string? Campanha,
    string? Oque,
    string? Projeto);

/// <summary>Devolvido ao formulário pra montar a página de obrigado com foto + WhatsApp do consultor sorteado.</summary>
public record PublicLeadResultDto(
    Guid LeadId,
    string? ConsultorNome,
    string? ConsultorFotoUrl,
    string? ConsultorWhatsApp);
