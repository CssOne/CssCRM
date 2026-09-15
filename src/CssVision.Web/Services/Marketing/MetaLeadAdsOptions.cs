namespace CssVision.Web.Services.Marketing;

/// <summary>
/// Configuração da integração com Facebook/Meta Lead Ads. Vinculada à seção "MetaLeadAds" do
/// appsettings (ver Program.cs) — em produção, defina via variáveis de ambiente
/// (MetaLeadAds__AppSecret, MetaLeadAds__VerifyToken, MetaLeadAds__PageAccessToken) ou
/// `dotnet user-secrets`, nunca commitando os valores reais.
/// </summary>
public class MetaLeadAdsOptions
{
    public const string SectionName = "MetaLeadAds";

    /// <summary>App Secret do app da Meta — usado para validar X-Hub-Signature-256.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Token escolhido por você para o handshake de verificação do webhook (hub.verify_token).</summary>
    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>Page Access Token de longa duração — ver notion-lead-automation/README.md, seção "Causa raiz".</summary>
    public string PageAccessToken { get; set; } = string.Empty;

    public string GraphApiVersion { get; set; } = "v21.0";
}
