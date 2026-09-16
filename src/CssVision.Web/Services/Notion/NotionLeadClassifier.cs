namespace CssVision.Web.Services.Notion;

/// <summary>Classifica um registro do Notion como "Lead" ou "Indicação" a partir do campo "O que".</summary>
public static class NotionLeadClassifier
{
    private static readonly HashSet<string> ValoresDeLead = new(StringComparer.OrdinalIgnoreCase)
    {
        "AGV", "AGV TRUCK", "APVS", "APVS TRUCK", "Loovi",
    };

    public static bool EhLead(string? oQue) => oQue is not null && ValoresDeLead.Contains(oQue);

    public static string Classificar(string? oQue) => EhLead(oQue) ? "Lead" : "Indicação";

    /// <summary>
    /// Decide em qual aba do quadro de leads o registro cai (CrmLead.CriadoManualmente): mesma regra
    /// do badge Lead/Indicação, pra as duas classificações baterem sempre juntas — Lead vira aba
    /// "Leads" (false), Indicação vira aba "Indicações" (true, o valor padrão de CrmLead).
    /// </summary>
    public static bool CriadoManualmente(string? oQue) => !EhLead(oQue);
}
