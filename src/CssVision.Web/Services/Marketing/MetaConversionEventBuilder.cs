using System.Security.Cryptography;
using System.Text;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Marketing;

/// <summary>
/// Monta payloads da Conversions API — lógica pura, sem HTTP, fácil de testar. Dois casos:
/// (1) oportunidade ganha: evento customizado + "Purchase" padrão em paralelo (Purchase exige
/// valor+moeda, mas é melhor pra otimização; o customizado sempre funciona, mesmo sem valor);
/// (2) mudança de etapa do lead no quadro: um evento customizado nomeado com a própria etapa,
/// enviado pra toda mudança — a escolha de qual etapa vira otimização de campanha é feita no
/// Gerenciador de Anúncios, não aqui.
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

    /// <summary>
    /// Monta um evento customizado único pra qualquer mudança de etapa do lead no quadro — o
    /// nome do evento é o próprio nome da etapa (ex: "Cotação", "Venda concluída"). Manda pra
    /// todas as etapas de propósito: qual delas efetivamente vira otimização de campanha é
    /// escolhido no Gerenciador de Anúncios (Conversões Personalizadas), não aqui no código.
    /// </summary>
    public static MetaCapiPayload BuildEtapaEventPayload(CrmLead lead, Guid etapaId, string etapaNome, MetaCapiOptions options)
    {
        var userData = BuildUserData(lead);
        var evento = new MetaCapiEvent(
            etapaNome,
            ToUnixTime(DateTimeOffset.UtcNow),
            ActionSource,
            $"etapa_{lead.Id}_{etapaId}",
            userData,
            new MetaCapiCustomData(options.EventSourceLabel, options.EventSourceLabel, null, null));

        return new MetaCapiPayload([evento]);
    }

    private static MetaCapiUserData BuildUserData(CrmLead lead)
    {
        var email = HashEmail(lead.EmailNormalizado ?? lead.Email);
        var telefone = HashPhone(lead.WhatsApp ?? lead.Telefone);
        long? leadId = long.TryParse(lead.MetaLeadId, out var parsed) ? parsed : null;
        var (nome, sobrenome) = SplitNome(lead.NomeOuRazaoSocial);

        return new MetaCapiUserData(
            email is null ? null : [email],
            telefone is null ? null : [telefone],
            leadId,
            nome is null ? null : [nome],
            sobrenome is null ? null : [sobrenome]);
    }

    /// <summary>SHA256 em hex minúsculo de nome/sobrenome — mesma exigência de normalização do Meta usada pra e-mail (minúsculo, sem espaço nas pontas).</summary>
    private static (string? Nome, string? Sobrenome) SplitNome(string nomeCompleto)
    {
        var partes = nomeCompleto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return (null, null);

        var nome = Sha256Hex(partes[0].ToLowerInvariant());
        var sobrenome = partes.Length > 1 ? Sha256Hex(string.Join(' ', partes[1..]).ToLowerInvariant()) : null;
        return (nome, sobrenome);
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
