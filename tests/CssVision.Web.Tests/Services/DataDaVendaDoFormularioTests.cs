using CssVision.Web.Services.Crm;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class DataDaVendaDoFormularioTests
{
    [Theory]
    [InlineData(2026, 10, 5)]
    [InlineData(2026, 10, 1)]
    [InlineData(2026, 12, 31)]
    public void DataDaVenda_cai_no_mesmo_dia_em_Brasilia(int ano, int mes, int dia)
    {
        var data = new DateOnly(ano, mes, dia);

        Assert.Equal(data, HorarioBrasilia.Dia(OpportunityService.DataDaVendaAoMeioDia(data)));
    }
}
