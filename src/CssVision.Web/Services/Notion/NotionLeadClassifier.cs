namespace CssVision.Web.Services.Notion;

/// <summary>Classifica um registro do Notion como "Lead" ou "Indicação" a partir do campo "O que".</summary>
public static class NotionLeadClassifier
{
    private static readonly HashSet<string> ValoresDeLead = new(StringComparer.OrdinalIgnoreCase)
    {
        "AGV", "AGV TRUCK", "APVS", "APVS TRUCK", "Loovi",
    };

    public static string Classificar(string? oQue) =>
        oQue is not null && ValoresDeLead.Contains(oQue) ? "Lead" : "Indicação";
}
