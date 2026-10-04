namespace CssVision.Web.Services.Crm;

/// <summary>
/// Dias da semana e faixa de horário (horário de Brasília) em que o consultor entra na distribuição
/// automática de leads. Tudo nulo = sem restrição. Dias da semana são guardados num bitmask
/// (bit <c>1 &lt;&lt; (int)DayOfWeek</c>); a faixa pode atravessar a meia-noite (ex.: 22:00–06:00).
/// </summary>
public static class JanelaRecebimentoLeads
{
    public const int TodosOsDias = 0b1111111;

    public static bool Permite(TimeOnly? inicio, TimeOnly? fim, int? diasSemana, DateTimeOffset agoraUtc)
    {
        var brasilia = agoraUtc.UtcDateTime.AddHours(-3);
        if (diasSemana is { } dias && (dias & TodosOsDias) != TodosOsDias && (dias & (1 << (int)brasilia.DayOfWeek)) == 0) return false;
        if (inicio is not { } i || fim is not { } f || i == f) return true;

        var hora = TimeOnly.FromDateTime(brasilia);
        return i < f ? hora >= i && hora < f : hora >= i || hora < f;
    }

    public static int? DiasParaMascara(IReadOnlyCollection<int>? dias)
    {
        if (dias is null || dias.Count == 0) return null;
        var mascara = dias.Where(d => d is >= 0 and <= 6).Aggregate(0, (m, d) => m | (1 << d));
        return mascara is 0 or TodosOsDias ? null : mascara;
    }

    public static int[]? MascaraParaDias(int? mascara) =>
        mascara is null ? null : Enumerable.Range(0, 7).Where(d => (mascara.Value & (1 << d)) != 0).ToArray();

    public static TimeOnly? ParseHorario(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null
        : TimeOnly.TryParseExact(texto.Trim(), "HH:mm", null, System.Globalization.DateTimeStyles.None, out var h) ? h
        : throw new Api.Contracts.Common.CrmBusinessException("Horário inválido (use HH:mm).", "horario_invalido");
}
