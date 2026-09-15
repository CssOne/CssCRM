namespace CssVision.Web.Services.Marketing;

/// <summary>
/// Configuração da Conversions API (CAPI) — retorno de conversões offline pro Facebook otimizar
/// campanhas por quem realmente compra, não só por quem preenche formulário. Vinculada à seção
/// "MetaCapi" do appsettings — em produção, defina via variáveis de ambiente
/// (MetaCapi__PixelId, MetaCapi__AccessToken), nunca commitando os valores reais.
/// </summary>
public class MetaCapiOptions
{
    public const string SectionName = "MetaCapi";

    public string PixelId { get; set; } = string.Empty;

    /// <summary>Token de acesso do Pixel (Events Manager -> Configurações -> Conversions API) — diferente do Page Access Token usado pra ler leads.</summary>
    public string AccessToken { get; set; } = string.Empty;

    public string GraphApiVersion { get; set; } = "v21.0";

    /// <summary>
    /// Nome do evento customizado enviado junto do Purchase padrão (sempre funciona, mesmo sem
    /// valor — o Purchase exige valor+moeda e é rejeitado se faltar). Crie uma "Conversão
    /// Personalizada" com este nome no Events Manager pra ele virar selecionável como otimização.
    /// </summary>
    public string EventoCustomizadoNome { get; set; } = "LeadConvertido";

    public string EventSourceLabel { get; set; } = "CssVision CRM";
}
