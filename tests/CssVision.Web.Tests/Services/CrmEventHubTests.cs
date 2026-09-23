using CssVision.Web.Services.Crm;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class CrmEventHubTests
{
    [Fact]
    public async Task Publicar_EntregaOEventoATodosOsAssinantes()
    {
        var hub = new CrmEventHub();
        using var aba1 = hub.Assinar();
        using var aba2 = hub.Assinar();

        hub.PublicarQuadroAtualizado("notion");

        var evento1 = await aba1.Leitor.ReadAsync();
        var evento2 = await aba2.Leitor.ReadAsync();
        Assert.Equal("quadro-atualizado", evento1.Tipo);
        Assert.Equal("notion", evento2.Origem);
    }

    [Fact]
    public async Task AssinaturaDescartada_ParaDeReceber_ELiberaQuemEstavaEsperando()
    {
        var hub = new CrmEventHub();
        var aba = hub.Assinar();
        var esperando = aba.Leitor.WaitToReadAsync().AsTask();

        aba.Dispose();
        hub.PublicarQuadroAtualizado("crm");

        // Canal completado: a espera termina com "sem mais eventos", sem exceção.
        Assert.False(await esperando.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
