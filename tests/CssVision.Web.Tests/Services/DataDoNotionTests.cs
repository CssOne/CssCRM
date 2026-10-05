using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class DataDoNotionTests
{
    [Theory]
    [InlineData("2026-10-05")]
    [InlineData("2026-10-01")]
    [InlineData("2026-12-31")]
    public void DataSoComDia_ficaNoMesmoDiaEmBrasilia(string texto)
    {
        var instante = DataDoNotion.ParaInstante(texto)!.Value;

        Assert.Equal(DateOnly.Parse(texto), HorarioBrasilia.Dia(instante));
        Assert.Equal(DateOnly.Parse(texto).Month, HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Dia(instante)).Month);
    }

    [Fact]
    public void DataComHora_eMantidaComoVem()
    {
        var instante = DataDoNotion.ParaInstante("2026-10-05T14:30:00-03:00")!.Value;

        Assert.Equal(new DateTimeOffset(2026, 10, 5, 17, 30, 0, TimeSpan.Zero), instante);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("texto qualquer")]
    public void VazioOuInvalido_ficaNulo(string? texto) => Assert.Null(DataDoNotion.ParaInstante(texto));
}
