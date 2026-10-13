using CssVision.Web.Services.Crm;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Logo depois de uma mudança o cache está vazio e várias abas pedem o quadro juntas: o cálculo roda uma vez só.</summary>
public class RespostaEmCacheTests
{
    [Fact]
    public async Task PedidosSimultaneosDaMesmaChave_CalculamUmaVezSo()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var eventos = new CrmEventHub();
        var calculos = 0;
        var liberar = new TaskCompletionSource();

        async Task<string> Calcular()
        {
            Interlocked.Increment(ref calculos);
            await liberar.Task;
            return "quadro";
        }

        var pedidos = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => RespostaEmCache.ObterAsync(cache, eventos, "quadro", new { escopo = "todos" }, Calcular)))
            .ToList();

        await Task.Delay(200); // todos chegaram enquanto o primeiro ainda calcula
        liberar.SetResult();
        var resultados = await Task.WhenAll(pedidos);

        Assert.All(resultados, r => Assert.Equal("quadro", r));
        Assert.Equal(1, calculos);
    }

    [Fact]
    public async Task ChavesDiferentes_NaoEsperamUmaPelaOutra()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var eventos = new CrmEventHub();

        var a = await RespostaEmCache.ObterAsync(cache, eventos, "quadro", new { escopo = "a" }, () => Task.FromResult("A"));
        var b = await RespostaEmCache.ObterAsync(cache, eventos, "quadro", new { escopo = "b" }, () => Task.FromResult("B"));

        Assert.Equal("A", a);
        Assert.Equal("B", b);
    }

    [Fact]
    public async Task MudancaNoQuadro_InvalidaOCache()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var eventos = new CrmEventHub();
        var calculos = 0;
        Task<int> Calcular() => Task.FromResult(Interlocked.Increment(ref calculos));

        var primeiro = await RespostaEmCache.ObterAsync(cache, eventos, "quadro", new { }, Calcular);
        var repetido = await RespostaEmCache.ObterAsync(cache, eventos, "quadro", new { }, Calcular);
        eventos.PublicarQuadroAtualizado("teste");
        var depois = await RespostaEmCache.ObterAsync(cache, eventos, "quadro", new { }, Calcular);

        Assert.Equal(primeiro, repetido);
        Assert.NotEqual(primeiro, depois);
        Assert.Equal(2, calculos);
    }

    [Fact]
    public async Task SeOPrimeiroFalha_QuemEsperavaCalculaPorConta()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var eventos = new CrmEventHub();
        var liberar = new TaskCompletionSource();

        var primeiro = Task.Run(() => RespostaEmCache.ObterAsync<string>(cache, eventos, "quadro", new { }, async () =>
        {
            await liberar.Task;
            throw new OperationCanceledException();
        }));
        await Task.Delay(100);
        var segundo = Task.Run(() => RespostaEmCache.ObterAsync(cache, eventos, "quadro", new { }, () => Task.FromResult("ok")));
        await Task.Delay(100);
        liberar.SetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(() => primeiro);
        Assert.Equal("ok", await segundo);
    }
}
