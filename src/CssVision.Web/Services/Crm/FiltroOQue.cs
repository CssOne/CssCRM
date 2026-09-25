using System.Globalization;
using System.Text;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// "O que?" aceitos por um consultor (ApplicationUser.RecebeSomenteOQue), guardados como texto
/// separado por vírgula. A comparação ignora maiúsculas e acentos ("AGV ELETRICO" = "AGV ELÉTRICO").
/// </summary>
public static class FiltroOQue
{
    public static string? Juntar(IEnumerable<string>? valores)
    {
        var lista = Limpar(valores);
        return lista.Count == 0 ? null : string.Join(",", lista);
    }

    public static IReadOnlyList<string>? Separar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : Limpar(valor.Split(','));

    /// <summary>Consultor sem restrição aceita qualquer lead; com restrição, só leads com um dos "O que?" dele.</summary>
    public static bool Aceita(string? recebeSomente, string? oQue)
    {
        var permitidos = Separar(recebeSomente);
        if (permitidos is null) return true;
        if (string.IsNullOrWhiteSpace(oQue)) return false;
        var chave = Normalizar(oQue);
        return permitidos.Any(p => Normalizar(p) == chave);
    }

    private static List<string> Limpar(IEnumerable<string>? valores) =>
        (valores ?? []).Select(v => v.Trim()).Where(v => v.Length > 0)
            .DistinctBy(Normalizar).ToList();

    private static string Normalizar(string valor)
    {
        var decomposto = valor.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString();
    }
}
