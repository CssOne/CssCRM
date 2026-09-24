namespace CssVision.Web.Services.Crm;

/// <summary>
/// Origem do lead. Para leads do Notion, a Origem é a tag de campanha do card (as mesmas opções do
/// campo Origem na tela — ver ClientApp/src/lib/opcoesLead.ts); sem tag, fica a origem técnica.
/// Para saber se um lead veio do Notion use os marcadores de <see cref="Domain.Crm.CrmLead.ConsentimentoOrigem"/>,
/// e não o texto da Origem, que pode ser trocado pela tag.
/// </summary>
public static class OrigemLead
{
    /// <summary>Marcador gravado pela migração histórica (tools/NotionMigration) — identifica os leads migrados.</summary>
    public const string MarcadorMigracaoNotion = "Migração da base histórica (Notion)";

    /// <summary>Marcador gravado pela sincronização periódica (NotionSyncService).</summary>
    public const string MarcadorSincronizacaoNotion = "Sincronização automática (Notion)";

    /// <summary>Marcador gravado pela entrada de leads do site (PublicLeadIntakeService).</summary>
    public const string MarcadorFormularioSite = "Formulário do site";

    public const string OrigemMigracaoNotion = "Migração Notion";
    public const string OrigemSincronizacaoNotion = "Sincronização Notion";

    public static readonly IReadOnlyList<string> Tags =
    [
        "Lookalike", "UGC VENDA", "UGC CAMINHÃO", "Pesquisa", "Pmax",
        "Demand Gen", "UGC", "INFLUENCERS", "Lead convertido", "Caixa de pergunta",
    ];

    /// <summary>A tag correspondente à campanha do card (sem diferenciar maiúsculas: "Demand gen" → "Demand Gen"), ou null.</summary>
    public static string? TagDaCampanha(string? campanha)
    {
        if (string.IsNullOrWhiteSpace(campanha)) return null;
        var valor = campanha.Trim();
        return Tags.FirstOrDefault(t => string.Equals(t, valor, StringComparison.OrdinalIgnoreCase));
    }
}
