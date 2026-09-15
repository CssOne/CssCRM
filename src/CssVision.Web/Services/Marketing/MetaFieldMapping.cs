using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CssVision.Web.Services.Marketing;

/// <summary>
/// Aliases de campos de formulário do Meta Lead Ads + normalização — porta direta de
/// notion-lead-automation/src/fieldMapping.ts. Para adicionar um formulário/pergunta nova, só
/// acrescente um alias abaixo.
/// </summary>
public static partial class MetaFieldMapping
{
    private static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>
    {
        ["full_name"] = ["full_name", "nome_completo", "nome", "name"],
        ["first_name"] = ["first_name", "primeiro_nome", "nome_1"],
        ["last_name"] = ["last_name", "sobrenome", "ultimo_nome"],
        ["email"] = ["email", "e_mail", "seu_email"],
        ["phone_number"] =
        [
            "phone_number", "phone", "telefone", "celular", "whatsapp",
            "numero", "numero_de_telefone", "seu_telefone",
        ],
    };

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex EspacoOuTracoRegex();

    /// <summary>Remove acentos, baixa a caixa e troca espaços/traços por "_". Usado para casar nomes de campo.</summary>
    public static string NormalizeKey(string rawName)
    {
        var normalizado = rawName.Normalize(NormalizationForm.FormD);
        var semAcentos = new StringBuilder();
        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) semAcentos.Append(c);
        }
        return EspacoOuTracoRegex().Replace(semAcentos.ToString().Trim().ToLowerInvariant(), "_");
    }

    /// <summary>Procura, entre as chaves de <paramref name="fields"/>, a primeira que corresponda a algum alias de <paramref name="internalKey"/>.</summary>
    public static string? GetMappedValue(IReadOnlyDictionary<string, string> fields, string internalKey)
    {
        if (!Aliases.TryGetValue(internalKey, out var aliases)) return null;
        foreach (var alias in aliases)
        {
            if (fields.TryGetValue(alias, out var valor) && !string.IsNullOrEmpty(valor)) return valor;
        }
        return null;
    }

    /// <summary>Monta o nome completo a partir de full_name, ou first_name + last_name como alternativa.</summary>
    public static string? ResolveName(IReadOnlyDictionary<string, string> fields)
    {
        var nomeCompleto = GetMappedValue(fields, "full_name");
        if (!string.IsNullOrEmpty(nomeCompleto)) return nomeCompleto;

        var primeiro = GetMappedValue(fields, "first_name");
        var ultimo = GetMappedValue(fields, "last_name");
        var combinado = string.Join(" ", new[] { primeiro, ultimo }.Where(s => !string.IsNullOrEmpty(s)));
        return combinado.Length == 0 ? null : combinado;
    }

    /// <summary>Transforma o field_data da Graph API num dicionário simples: chave normalizada -> valor.</summary>
    public static Dictionary<string, string> NormalizeFieldData(IReadOnlyList<MetaFieldDatum>? fieldData)
    {
        var resultado = new Dictionary<string, string>();
        if (fieldData is null) return resultado;

        foreach (var campo in fieldData)
        {
            if (string.IsNullOrEmpty(campo.Name) || campo.Values is not { Count: > 0 }) continue;
            resultado[NormalizeKey(campo.Name)] = string.Join(", ", campo.Values);
        }
        return resultado;
    }
}
