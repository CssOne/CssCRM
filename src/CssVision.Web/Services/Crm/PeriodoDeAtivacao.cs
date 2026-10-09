namespace CssVision.Web.Services.Crm;

/// <summary>
/// Limites do filtro "Data da venda": a venda conta no dia em que foi <b>ativada</b> ("Ativo em", um dia gravado à meia-noite UTC); sem
/// ativação, vale a data da venda (dia de Brasília). É a mesma regra do painel da TV.
/// </summary>
public readonly record struct PeriodoDeAtivacao(
    DateTimeOffset AtivacaoDe, DateTimeOffset AtivacaoAte, DateTimeOffset VendaDe, DateTimeOffset VendaAte)
{
    /// <summary>"Ativo em" no intervalo [AtivacaoDe, AtivacaoAte); a data da venda, no intervalo [VendaDe, VendaAte] (só sem ativação).</summary>
    public static PeriodoDeAtivacao De(DateOnly? inicio, DateOnly? fim) => new(
        inicio is { } i ? new DateTimeOffset(i.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : DateTimeOffset.MinValue,
        fim is { } f ? new DateTimeOffset(f.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : DateTimeOffset.MaxValue,
        inicio is { } vi ? HorarioBrasilia.Inicio(vi) : DateTimeOffset.MinValue,
        fim is { } vf ? HorarioBrasilia.Fim(vf) : DateTimeOffset.MaxValue);
}
