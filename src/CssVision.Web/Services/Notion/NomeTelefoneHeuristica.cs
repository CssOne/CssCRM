using System.Text.RegularExpressions;

namespace CssVision.Web.Services.Notion;

/// <summary>
/// Alguns registros migrados/sincronizados do Notion têm o nome e o telefone trocados na própria
/// origem (a pessoa que cadastrou digitou o telefone no campo "Name"/título e o nome no campo
/// "WhatsApp") — não é um bug de mapeamento nosso, é dado errado no Notion. Essa heurística detecta
/// o padrão (nome parece telefone, telefone não parece telefone) pra corrigir automaticamente.
/// </summary>
public static partial class NomeTelefoneHeuristica
{
    [GeneratedRegex(@"^\+?[0-9()\s-]+$")]
    private static partial Regex PadraoTelefone();

    /// <summary>Só considera "parece telefone" com pelo menos 8 dígitos — evita falso positivo em
    /// nomes curtos numéricos improváveis.</summary>
    public static bool PareceTelefone(string? valor) =>
        !string.IsNullOrWhiteSpace(valor)
        && valor.Count(char.IsDigit) >= 8
        && PadraoTelefone().IsMatch(valor.Trim());

    /// <summary>True quando nome e telefone estão claramente trocados: o nome é numérico (parece
    /// telefone) e o telefone tem texto (não parece telefone).</summary>
    public static bool EstaoTrocados(string? nome, string? telefone) =>
        PareceTelefone(nome) && !string.IsNullOrWhiteSpace(telefone) && !PareceTelefone(telefone);

    /// <summary>
    /// Alguns leads têm dois telefones colados no mesmo campo, sem separador (ex: a pessoa digitou o
    /// número cru e depois colou de novo com o "+55" na frente: "21964132942+5521964132942") — um "+"
    /// que não está bem no início do texto sempre marca o começo do segundo número, já que números de
    /// telefone nunca têm "+" no meio. Retorna (null, null) quando não há separação a fazer.
    /// </summary>
    public static (string? Primeiro, string? Segundo) SepararTelefones(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return (null, null);
        var indice = telefone.IndexOf('+', 1);
        if (indice < 0) return (null, null);

        var primeiro = telefone[..indice].Trim();
        var segundo = telefone[indice..].Trim();
        return primeiro.Length == 0 || segundo.Length == 0 ? (null, null) : (primeiro, segundo);
    }
}
