namespace CssVision.Web.Services.Crm;

/// <summary>
/// Configuração do rodízio de leads (seção "Distribuicao"; no servidor, variáveis como
/// <c>Distribuicao__RegionaisExclusivas__0=MG134</c>).
/// </summary>
public class DistribuicaoOptions
{
    public const string Secao = "Distribuicao";

    /// <summary>
    /// Regionais "exclusivas": só recebem leads que chegam marcados com a regional delas. Os consultores dessas regionais ficam de
    /// fora do rodízio geral (leads sem regional), para não pegarem leads de outra equipe.
    /// </summary>
    public string[] RegionaisExclusivas { get; set; } = [];
}

/// <summary>"MG 134", "mg134" e "MG134" são a mesma regional (o campo do lead é texto livre).</summary>
public static class NomeDeRegional
{
    public static string? Normalizar(string? regional) =>
        string.IsNullOrWhiteSpace(regional) ? null : new string(regional.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}
