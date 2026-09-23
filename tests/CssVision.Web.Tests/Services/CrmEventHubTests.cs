using CssVision.Web.Services.Crm;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class CrmEventHubTests
{
    [Fact]
    public async Task Publicar_EntregaOEventoATodosOsAssinantes()
    {
        var hub = new CrmEventHub();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var aba1 = hub.AssinarAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var aba2 = hub.AssinarAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var proximo1 = aba1.MoveNextAsync().AsTask();
        var proximo2 = aba2.MoveNextAsync().AsTask();

        hub.PublicarQuadroAtualizado("notion");

        Assert.True(await proximo1);
        Assert.True(await proximo2);
        Assert.Equal("quadro-atualizado", aba1.Current.Tipo);
        Assert.Equal("notion", aba2.Current.Origem);

        await aba1.DisposeAsync();
        await aba2.DisposeAsync();
    }
}
