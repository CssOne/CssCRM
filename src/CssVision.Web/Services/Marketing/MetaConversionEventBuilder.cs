using System.Security.Cryptography;
using System.Text;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Marketing;

/// <summary>
/// Monta o payload da Conversions API para uma oportunidade ganha — lógica pura, sem HTTP, fácil
/// de testar. Manda dois eventos em paralelo: o customizado (sempre funciona, mesmo sem valor) e
/// o padrão "Purchase" (exige valor+moeda, mas é melhor pra otimização de campanha) — assim nada
/// quebra enquanto o Purchase acumula volume suficiente pra virar a otimização principal.
/// </summary>
public static class MetaConversionEventBuilder
{
    private const string ActionSource = "system_generated";
    private const string PurchaseEventName = "Purchase";
    private const string DefaultCurrency = "BRL";

    public static MetaCapiPayload BuildVendaGanhaPayload(CrmLead lead, CrmOpportunity opportunity, MetaCapiOptions options)
    {
        var eventTime = ToUnixTime(opportunity.DataEfetivaFechamento ?? DateTimeOffset.UtcNow);
        var userData = BuildUserData(lead);

        var eventoCustomizado = new MetaCapiEvent(
            options.EventoCustomizadoNome,
            eventTime,
            ActionSource,
            $"custom_{opportunity.Id}",
            userData,
            new MetaCapiCustomData(options.EventSourceLabel, options.EventSourceLabel, opportunity.ValorFinal, DefaultCurrency));

        var eventoPurchase = new MetaCapiEvent(
            PurchaseEventName,
            eventTime,
            ActionSource,
            $"purchase_{opportunity.Id}",
            userData,
            new MetaCapiCustomData(options.EventSourceLabel, options.EventSourceLabel, opportunity.ValorFinal, DefaultCurrency));

        return new MetaCapiPayload([eventoCustomizado, eventoPurchase]);
    }

    private static MetaCapiUserData BuildUserData(CrmLead lead)
    {
        var email = HashEmail(lead.EmailNormalizado ?? lead.Email);
        var telefone = HashPhone(lead.WhatsApp ?? lead.Telefone);
        long? leadId = long.TryParse(lead.MetaLeadId, out var parsed) ? parsed : null;

        return new MetaCapiUserData(
            email is null ? null : [email],
            telefone is null ? null : [telefone],
            leadId);
    }

    /// <summary>SHA256 em hex minúsculo do e-mail normalizado (minúsculo, sem espaços) — exigência de privacidade do Meta.</summary>
    public static string? HashEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        return Sha256Hex(email.Trim().ToLowerInvariant());
    }

    /// <summary>SHA256 em hex minúsculo do telefone com DDI (garante o "55" na frente — diferente da normalização interna do CRM, que remove o DDI).</summary>
    public static string? HashPhone(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;

        var digitos = new string(telefone.Where(char.IsDigit).ToArray());
        if (digitos.Length == 0) return null;

        if (digitos.Length is 10 or 11) digitos = "55" + digitos;

        return Sha256Hex(digitos);
    }

    private static string Sha256Hex(string valor)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(valor));
        return Convert.ToHexStringLower(hash);
    }

    private static long ToUnixTime(DateTimeOffset momento) => momento.ToUnixTimeSeconds();
}
