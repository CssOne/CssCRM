namespace CssVision.Web.Services.Crm;

/// <summary>
/// Etiqueta "Indicação Lead": lead (que chegou pelo tráfego, tipo "Lead") cuja venda foi uma
/// indicação. Fica na coluna "Venda concluída (Indicação)" — como qualquer tipo diferente de "Lead",
/// o quadro o trata como indicação — mas com uma etiqueta própria, para não se misturar às indicações comuns.
/// </summary>
public static class TipoIndicacaoLead
{
    public const string IndicacaoLead = "Indicação Lead";

    public static bool EhLead(string? tipo) => string.Equals(tipo?.Trim(), "Lead", StringComparison.OrdinalIgnoreCase);

    public static bool EhIndicacaoLead(string? tipo) =>
        string.Equals(tipo?.Trim(), IndicacaoLead, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A sincronização com o Notion não desfaz a etiqueta: o card no Notion continua "Lead", mas no
    /// CRM a venda já foi marcada como indicação.
    /// </summary>
    public static string? ManterIndicacaoLead(string? atual, string? novo) =>
        EhIndicacaoLead(atual) && (EhLead(novo) || string.IsNullOrWhiteSpace(novo)) ? atual : novo;
}
