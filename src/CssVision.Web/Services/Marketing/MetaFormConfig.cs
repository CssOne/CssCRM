namespace CssVision.Web.Services.Marketing;

/// <summary>Overrides de Campanha/Tags/Produto para um formulário específico do Meta Lead Ads.</summary>
public sealed record MetaFormOverride(string Campanha, IReadOnlyList<string> Tags, string? ProdutoInteresse);

/// <summary>
/// Mapeamento por formulário (form_id do webhook/Graph API) — mesma lógica do FORM_CONFIG em
/// notion-lead-automation/src/config.ts. Formulários sem entrada aqui usam os defaults
/// (DefaultCampanha/DefaultTags), então nenhum lead é descartado por o form ainda não estar
/// mapeado. Mantenha os dois arquivos em sincronia manualmente ao adicionar um formulário novo.
/// </summary>
public static class MetaFormConfig
{
    public const string DefaultCampanha = "Lead convertido";
    public static readonly IReadOnlyList<string> DefaultTags = ["Meta ads"];

    public static readonly IReadOnlyDictionary<string, MetaFormOverride> Overrides = new Dictionary<string, MetaFormOverride>
    {
        // Página "Universo AGV" — campanha UGC
        ["1544241000014231"] = new("UGC", ["Meta ads", "UGC"], "AGV"),
        // Página "Universo AGV" — form original, tag adicional "UGC VENDA"
        ["1050561764291664"] = new("Lead convertido", ["Meta ads", "UGC VENDA"], "AGV"),
    };
}
