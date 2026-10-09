namespace CssVision.Web.Services.Notion;

/// <summary>Classifica um registro do Notion como "Lead" ou "Indicação" a partir do campo "O que".</summary>
public static class NotionLeadClassifier
{
    // Sem acento e em maiúsculas: o Notion traz "AGV ELETRICO" e o CRM escreve "AGV Elétrico" — são o mesmo produto.
    private static readonly HashSet<string> ValoresDeLead = new(StringComparer.Ordinal)
    {
        "AGV", "AGV TRUCK", "AGV ELETRICO", "APVS", "APVS TRUCK", "LOOVI",
    };

    public static bool EhLead(string? oQue) => oQue is not null && ValoresDeLead.Contains(Normalizar(oQue));

    public static string Classificar(string? oQue) => EhLead(oQue) ? "Lead" : "Indicação";

    /// <summary>
    /// Decide em qual aba do quadro de leads o registro cai (CrmLead.CriadoManualmente): mesma regra
    /// do badge Lead/Indicação, pra as duas classificações baterem sempre juntas — Lead vira aba
    /// "Leads" (false), Indicação vira aba "Indicações" (true, o valor padrão de CrmLead).
    /// </summary>
    public static bool CriadoManualmente(string? oQue) => !EhLead(oQue);

    private static string Normalizar(string texto)
    {
        var decomposto = texto.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var semAcento = new string(decomposto.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return semAcento.ToUpperInvariant();
    }
}
