using System.Globalization;

namespace CssVision.Web.Services.Notion;

/// <summary>
/// Datas do Notion que vêm só com o dia ("2026-10-05", como "Data da venda"): sem hora, virariam meia-noite UTC, que no
/// horário de Brasília (UTC-3) é 21h do dia ANTERIOR — a venda de hoje aparecia como de ontem (e a do dia 1º, como do mês
/// passado). Ficam ao meio-dia UTC (9h em Brasília), sempre no dia certo. Datas com hora são mantidas como vêm.
/// </summary>
public static class DataDoNotion
{
    public static DateTimeOffset? ParaInstante(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        if (t.Length == 10 && DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
        {
            return new DateTimeOffset(dia.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        }
        return DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor) ? valor.ToUniversalTime() : null;
    }
}
