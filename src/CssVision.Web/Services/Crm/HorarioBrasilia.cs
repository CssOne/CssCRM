namespace CssVision.Web.Services.Crm;

/// <summary>
/// Dia e mês "de hoje" no horário de Brasília (UTC-3, sem horário de verão). Os períodos dos painéis
/// (mês atual, ranking, metas) viram à meia-noite de Brasília — com UTC o mês novo só aparecia às 21h
/// do último dia e as 3 primeiras horas do dia 1 ainda contavam para o mês anterior.
/// </summary>
public static class HorarioBrasilia
{
    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);

    public static DateOnly Dia(DateTimeOffset instante) => DateOnly.FromDateTime(instante.UtcDateTime.Add(Fuso));

    public static DateOnly Hoje => Dia(DateTimeOffset.UtcNow);

    public static DateOnly PrimeiroDiaDoMes(DateOnly dia) => new(dia.Year, dia.Month, 1);

    /// <summary>Meia-noite de Brasília do dia informado, como instante (UTC).</summary>
    public static DateTimeOffset Inicio(DateOnly dia) => new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).Subtract(Fuso);

    /// <summary>Último instante do dia em Brasília.</summary>
    public static DateTimeOffset Fim(DateOnly dia) => Inicio(dia.AddDays(1)).AddTicks(-1);
}
