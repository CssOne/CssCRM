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

    private static readonly DateOnly Hoje = new(2026, 10, 13);

    private static CssOneRegistro Registro(string id, string pessoa, DateTimeOffset data, string? tipo = null) => new(id, pessoa, "Cliente", "ABC1D23", tipo, data);

    [Fact]
    public void Cartao_de_rastreadores_passa_a_mostrar_os_instalados_do_CSS_One()
    {
        var r = TvComercialService.ComDadosDoCssOne(Administrativo(), 37, null, Hoje)!;
        var tracker = r.Indicadores.Single(i => i.Id == "tracker");
        Assert.Equal(37, tracker.Total);
        Assert.Equal("Rastreadores instalados", tracker.Rotulo);
        Assert.Equal(5, r.Indicadores.Single(i => i.Id == "reintegration").Total); // sem dados do CSS One, o resto fica como veio do Notion
    }

    [Fact]
    public void Reintegracoes_e_eventos_finalizados_vem_do_CSS_One_com_total_hoje_e_ultimo()
    {
        var agora = new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(-3));
        var cssOne = new CssOneAdministrativo(
            [Registro("r2", "Ana", agora), Registro("r1", "Bia", agora.AddDays(-2))],
            [Registro("s1", "Caio", agora.AddHours(-1), "Colisão")]);

        var r = TvComercialService.ComDadosDoCssOne(Administrativo(), null, cssOne, Hoje)!;

        var reint = r.Indicadores.Single(i => i.Id == "reintegration");
        Assert.Equal(2, reint.Total);                      // o 5 do Notion é substituído
        Assert.Equal(1, reint.Hoje);
        Assert.Equal("Ana", reint.Ultimo!.Pessoa);
        Assert.Equal(["r2", "r1"], reint.Registros!.Select(x => x.Id));
        var claim = r.Indicadores.Single(i => i.Id == "claim"); // não existia no Notion: nasce com os dados do CSS One
        Assert.Equal(1, claim.Total);
        Assert.Equal("Colisão", claim.Ultimo!.TipoEvento);
        Assert.Equal(2, r.Indicadores.Single(i => i.Id == "tracker").Total); // rastreadores: sem número do CSS One, fica o do Notion
    }

    [Fact]
    public void Sem_nenhuma_fonte_devolve_nulo_e_sem_Notion_o_CSS_One_sozinho_basta()
    {
        Assert.Null(TvComercialService.ComDadosDoCssOne(null, null, null, Hoje));
        var so = TvComercialService.ComDadosDoCssOne(null, 7, new CssOneAdministrativo([], []), Hoje)!;
        Assert.Equal(["reintegration", "claim", "tracker"], so.Indicadores.Select(i => i.Id));
        Assert.Equal(7, so.Indicadores.Single(i => i.Id == "tracker").Total);
        Assert.Equal(0, so.Indicadores.Single(i => i.Id == "reintegration").Total);
    }

    [Fact]
    public void Sem_numero_do_CSS_One_o_cartao_fica_como_veio_do_Notion()
    {
        var r = TvComercialService.ComDadosDoCssOne(Administrativo(), null, null, Hoje)!;
        Assert.Equal(2, r.Indicadores.Single(i => i.Id == "tracker").Total);
        Assert.Equal("Rastreadores", r.Indicadores.Single(i => i.Id == "tracker").Rotulo);
    }

    [Fact]
    public async Task Administrativo_vem_do_CSS_One()
    {
        var corpo = """{"mes":10,"ano":2026,"reintegracoes":[{"id":"reint-X","pessoa":"Ana","cliente":"C","placa":"P","tipoEvento":null,"data":"2026-10-13T10:00:00-03:00"}],"eventosFinalizados":[]}""";
        var h = new Resposta(HttpStatusCode.OK, corpo);
        var r = await Fonte(h).AdministrativoAsync(10, 2026, default);
        Assert.NotNull(r);
        Assert.Equal("reint-X", r!.Reintegracoes.Single().Id);
        Assert.Empty(r.EventosFinalizados);
        Assert.Contains("administrativo?mes=10&ano=2026", h.Ultima!.RequestUri!.ToString());
    }
}
