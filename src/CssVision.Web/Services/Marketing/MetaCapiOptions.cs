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

    /// <summary>
    /// Pixels extras por "O que" do lead (ex.: regional com pixel próprio). Lead cujo ProdutoInteresse
    /// bate com <see cref="MetaCapiPixelPorOQue.OQue"/> usa esse pixel; os demais usam PixelId/AccessToken acima.
    /// Env: MetaCapi__PixelsPorOQue__0__OQue / __PixelId / __AccessToken.
    /// </summary>
    public List<MetaCapiPixelPorOQue> PixelsPorOQue { get; set; } = [];

    /// <summary>Opções efetivas para o lead: troca só o par PixelId/AccessToken quando há pixel dedicado.</summary>
    public MetaCapiOptions ParaLead(string? oQue)
    {
        if (string.IsNullOrWhiteSpace(oQue)) return this;

        var dedicado = PixelsPorOQue.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.OQue) && string.Equals(p.OQue.Trim(), oQue.Trim(), StringComparison.OrdinalIgnoreCase));
        if (dedicado is null) return this;

        return new MetaCapiOptions
        {
            PixelId = dedicado.PixelId,
            AccessToken = dedicado.AccessToken,
            GraphApiVersion = GraphApiVersion,
            EventoCustomizadoNome = EventoCustomizadoNome,
            EventSourceLabel = EventSourceLabel,
        };
    }
}

public class MetaCapiPixelPorOQue
{
    public string OQue { get; set; } = string.Empty;
    public string PixelId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}
