using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CssVision.Web.Services.Notion;

/// <summary>Leitura defensiva de propriedades de uma página do Notion — nomes variam levemente entre as bases.</summary>
public static partial class NotionPageExtensions
{
    [GeneratedRegex(@"[^\d,.\-]")]
    private static partial Regex NaoNumericoRegex();

    private static JsonElement? Prop(this JsonElement page, params string[] nomes)
    {
        var properties = page.GetProperty("properties");
        foreach (var nome in nomes)
        {
            if (properties.TryGetProperty(nome, out var valor)) return valor;
        }
        return null;
    }

    public static string PageId(this JsonElement page) => page.GetProperty("id").GetString()!;

    public static DateTimeOffset LastEditedTime(this JsonElement page) =>
        DateTimeOffset.Parse(page.GetProperty("last_edited_time").GetString()!, CultureInfo.InvariantCulture);

    public static string? Text(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor) return null;

        var tipo = valor.GetProperty("type").GetString();
        return tipo switch
        {
            "rich_text" => string.Concat(valor.GetProperty("rich_text").EnumerateArray().Select(t => t.GetProperty("plain_text").GetString())).Trim() is { Length: > 0 } s ? s : null,
            "title" => string.Concat(valor.GetProperty("title").EnumerateArray().Select(t => t.GetProperty("plain_text").GetString())).Trim() is { Length: > 0 } s ? s : null,
            _ => null
        };
    }

    public static double? Number(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor) return null;
        if (valor.GetProperty("type").GetString() == "number" && valor.GetProperty("number").ValueKind == JsonValueKind.Number)
        {
            return valor.GetProperty("number").GetDouble();
        }
        return null;
    }

    /// <summary>
    /// Select cujo nome da propriedade bate com um dos informados ignorando maiúsculas, acentos e
    /// espaços nas pontas — os nomes variam entre as bases ("Tpo de Indicação? ", "Tipo de Indicação?").
    /// </summary>
    public static string? SelectPorNomeAproximado(this JsonElement page, params string[] nomes)
    {
        var procurados = nomes.Select(ChaveDeNome).ToHashSet();
        foreach (var prop in page.GetProperty("properties").EnumerateObject())
        {
            if (!procurados.Contains(ChaveDeNome(prop.Name))) continue;
            var valor = prop.Value;
            if (valor.GetProperty("type").GetString() != "select") continue;
            var select = valor.GetProperty("select");
            if (select.ValueKind == JsonValueKind.Object && select.GetProperty("name").GetString() is { Length: > 0 } nome) return nome.Trim();
        }
        return null;
    }

    private static string ChaveDeNome(string nome)
    {
        var decomposto = nome.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    public static string? Select(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor) return null;
        if (valor.GetProperty("type").GetString() != "select") return null;
        var select = valor.GetProperty("select");
        return select.ValueKind == JsonValueKind.Object ? select.GetProperty("name").GetString() : null;
    }

    public static string? DateStart(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor) return null;
        if (valor.GetProperty("type").GetString() != "date") return null;
        var date = valor.GetProperty("date");
        return date.ValueKind == JsonValueKind.Object && date.TryGetProperty("start", out var start) ? start.GetString() : null;
    }

    public static string? CreatedTime(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor) return null;
        return valor.GetProperty("type").GetString() == "created_time" ? valor.GetProperty("created_time").GetString() : null;
    }

    public static bool HasFiles(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor) return false;
        return valor.GetProperty("type").GetString() == "files" && valor.GetProperty("files").GetArrayLength() > 0;
    }

    public record ArquivoInfo(string Nome, string Url);

    /// <summary>Primeiro arquivo de uma propriedade "files" (URL assinada, temporária — baixar logo após ler).</summary>
    public static ArquivoInfo? PrimeiroArquivo(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor || valor.GetProperty("type").GetString() != "files") return null;
        var arquivos = valor.GetProperty("files");
        if (arquivos.GetArrayLength() == 0) return null;

        var arquivo = arquivos[0];
        var nome = arquivo.TryGetProperty("name", out var n) ? n.GetString() ?? "arquivo" : "arquivo";
        var tipo = arquivo.GetProperty("type").GetString();
        var url = tipo switch
        {
            "file" => arquivo.GetProperty("file").GetProperty("url").GetString(),
            "external" => arquivo.GetProperty("external").GetProperty("url").GetString(),
            _ => null
        };
        return url is null ? null : new ArquivoInfo(nome, url);
    }

    /// <summary>Formula que devolve texto (ex: "R$ 133.32") ou número — sempre lida como decimal quando possível.</summary>
    public static decimal? FormulaDecimal(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor || valor.GetProperty("type").GetString() != "formula") return null;
        var formula = valor.GetProperty("formula");
        var tipo = formula.GetProperty("type").GetString();
        if (tipo == "number" && formula.GetProperty("number").ValueKind == JsonValueKind.Number)
        {
            return (decimal)formula.GetProperty("number").GetDouble();
        }
        if (tipo == "string" && formula.GetProperty("string").ValueKind == JsonValueKind.String)
        {
            var texto = NaoNumericoRegex().Replace(formula.GetProperty("string").GetString() ?? "", "");
            return decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        }
        return null;
    }

    public record VendedorInfo(string Nome, string? Email);

    /// <summary>Primeiro vendedor da propriedade "Vendedor" (people) — a maioria das linhas tem só um.</summary>
    public static VendedorInfo? PrimeiroVendedor(this JsonElement page, params string[] nomes)
    {
        var prop = page.Prop(nomes);
        if (prop is not { } valor || valor.GetProperty("type").GetString() != "people") return null;
        var pessoas = valor.GetProperty("people");
        if (pessoas.GetArrayLength() == 0) return null;

        var pessoa = pessoas[0];
        var nome = pessoa.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(nome)) return null;

        string? email = null;
        if (pessoa.TryGetProperty("person", out var p) && p.TryGetProperty("email", out var e))
        {
            email = e.GetString();
        }
        return new VendedorInfo(nome.Trim(), email);
    }
}
