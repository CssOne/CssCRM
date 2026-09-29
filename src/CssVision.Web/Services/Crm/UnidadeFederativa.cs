using System.Globalization;
using System.Text;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Converte o estado vindo de formulários externos na sigla da UF (a coluna Estado tem 2 letras).
/// O site às vezes manda o nome ("Minas Gerais"), sigla com nome ("MG - Minas Gerais") ou lixo — antes
/// isso estourava a coluna e o lead inteiro era perdido com erro 500.
/// </summary>
public static class UnidadeFederativa
{
    private static readonly Dictionary<string, string> PorNome = new()
    {
        ["ACRE"] = "AC", ["ALAGOAS"] = "AL", ["AMAPA"] = "AP", ["AMAZONAS"] = "AM", ["BAHIA"] = "BA",
        ["CEARA"] = "CE", ["DISTRITO FEDERAL"] = "DF", ["ESPIRITO SANTO"] = "ES", ["GOIAS"] = "GO",
        ["MARANHAO"] = "MA", ["MATO GROSSO"] = "MT", ["MATO GROSSO DO SUL"] = "MS", ["MINAS GERAIS"] = "MG",
        ["PARA"] = "PA", ["PARAIBA"] = "PB", ["PARANA"] = "PR", ["PERNAMBUCO"] = "PE", ["PIAUI"] = "PI",
        ["RIO DE JANEIRO"] = "RJ", ["RIO GRANDE DO NORTE"] = "RN", ["RIO GRANDE DO SUL"] = "RS",
        ["RONDONIA"] = "RO", ["RORAIMA"] = "RR", ["SANTA CATARINA"] = "SC", ["SAO PAULO"] = "SP",
        ["SERGIPE"] = "SE", ["TOCANTINS"] = "TO",
    };

    private static readonly HashSet<string> Siglas = [.. PorNome.Values];

    /// <summary>Sigla da UF ("MG"), ou null quando não dá pra reconhecer.</summary>
    public static string? Sigla(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado)) return null;
        var texto = SemAcentos(estado).Trim().ToUpperInvariant();

        if (Siglas.Contains(texto)) return texto;
        if (PorNome.TryGetValue(texto, out var porNome)) return porNome;

        // "MG - Minas Gerais", "MG/Minas Gerais", "Minas Gerais (MG)"...
        foreach (var parte in texto.Split([' ', '-', '/', '(', ')', ',', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Siglas.Contains(parte)) return parte;
        }
        // Nome no meio do texto; "Mato Grosso do Sul" contém "Mato Grosso": fica com o nome mais longo.
        var contidos = PorNome.Where(p => texto.Contains(p.Key)).ToList();
        return contidos.Count == 0 ? null : contidos.MaxBy(p => p.Key.Length).Value;
    }

    private static string SemAcentos(string texto) =>
        new(texto.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
}
