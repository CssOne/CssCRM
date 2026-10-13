using System.Net;
using System.Text;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.CssOne;
using CssVision.Web.Services.Crm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class CssOneFonteTests
{
    private sealed class Resposta(HttpStatusCode status, string corpo) : HttpMessageHandler
    {
        public int Chamadas { get; private set; }
        public HttpRequestMessage? Ultima { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            Ultima = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
        }
    }

    private static CssOneFonte Fonte(Resposta h, string? url = "https://one.exemplo", string? chave = "segredo") =>
        new(new HttpClient(h), Options.Create(new CssOneOptions { BaseUrl = url, ApiKey = chave }), new MemoryCache(new MemoryCacheOptions()), NullLogger<CssOneFonte>.Instance);

    private const string Json = """{"mes":10,"ano":2026,"operadores":[{"operador":"Bia","notaMedia":3.5,"qtdComNota":10},{"operador":"Ana","notaMedia":3.9,"qtdComNota":4},{"operador":"Caio","notaMedia":3.9,"qtdComNota":12}]}""";

    [Fact]
    public async Task Ordena_pela_maior_nota_media_e_desempata_por_mais_avaliacoes()
    {
        var h = new Resposta(HttpStatusCode.OK, Json);
        var ranking = await Fonte(h).RankingDeOperadoresAsync(10, 2026, default);

        Assert.NotNull(ranking);
        Assert.Equal(["Caio", "Ana", "Bia"], ranking!.Select(r => r.Operador));
        Assert.Equal([1, 2, 3], ranking.Select(r => r.Posicao));
        Assert.Equal("segredo", h.Ultima!.Headers.GetValues("X-Api-Key").Single());
        Assert.Contains("mes=10&ano=2026", h.Ultima.RequestUri!.Query);
    }

    [Fact]
    public async Task Sem_configuracao_fica_desligado_e_nao_chama_o_CSS_One()
    {
        var h = new Resposta(HttpStatusCode.OK, Json);
        Assert.Null(await Fonte(h, url: null).RankingDeOperadoresAsync(10, 2026, default));
        Assert.Null(await Fonte(h, chave: "").RankingDeOperadoresAsync(10, 2026, default));
        Assert.Equal(0, h.Chamadas);
    }

    [Fact]
    public async Task Falha_do_CSS_One_devolve_nulo_sem_derrubar_o_painel()
    {
        var h = new Resposta(HttpStatusCode.Unauthorized, "");
        Assert.Null(await Fonte(h).RankingDeOperadoresAsync(10, 2026, default));
    }

    [Fact]
    public async Task Segunda_leitura_dentro_da_validade_usa_o_cache()
    {
        var h = new Resposta(HttpStatusCode.OK, Json);
        var fonte = Fonte(h);
        await fonte.RankingDeOperadoresAsync(10, 2026, default);
        await fonte.RankingDeOperadoresAsync(10, 2026, default);
        Assert.Equal(1, h.Chamadas);
    }

    [Fact]
    public async Task Rastreadores_instalados_vem_do_CSS_One()
    {
        var h = new Resposta(HttpStatusCode.OK, """{"mes":10,"ano":2026,"instalados":37,"total":90}""");
        Assert.Equal(37, await Fonte(h).RastreadoresInstaladosAsync(10, 2026, default));
        Assert.Contains("rastreadores-instalados?mes=10&ano=2026", h.Ultima!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Rastreadores_instalados_sem_CSS_One_devolve_nulo()
    {
        Assert.Null(await Fonte(new Resposta(HttpStatusCode.OK, "{}"), url: null).RastreadoresInstaladosAsync(10, 2026, default));
        Assert.Null(await Fonte(new Resposta(HttpStatusCode.InternalServerError, "")).RastreadoresInstaladosAsync(10, 2026, default));
    }

    private static TvAdministrativoDto Administrativo() => new([
        new TvAdministrativoIndicadorDto("reintegration", "Reintegrações", "Reintegração realizada", 5, 1, null),
        new TvAdministrativoIndicadorDto("tracker", "Rastreadores", "Rastreador instalado", 2, 0, null),
    ]);

    [Fact]
    public void Cartao_de_rastreadores_passa_a_mostrar_os_instalados_do_CSS_One()
    {
        var r = TvComercialService.ComRastreadoresInstalados(Administrativo(), 37)!;
        var tracker = r.Indicadores.Single(i => i.Id == "tracker");
        Assert.Equal(37, tracker.Total);
        Assert.Equal("Rastreadores instalados", tracker.Rotulo);
        Assert.Equal(5, r.Indicadores.Single(i => i.Id == "reintegration").Total); // os outros ficam como estavam
    }

    [Fact]
    public void Sem_numero_do_CSS_One_o_cartao_fica_como_veio_do_Notion()
    {
        var original = Administrativo();
        Assert.Same(original, TvComercialService.ComRastreadoresInstalados(original, null));
        Assert.Null(TvComercialService.ComRastreadoresInstalados(null, 37));
    }
}
