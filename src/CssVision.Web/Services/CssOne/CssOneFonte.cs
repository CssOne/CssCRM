using System.Net.Http.Json;
using CssVision.Web.Api.Contracts.Crm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.CssOne;

public sealed class CssOneOptions
{
    /// <summary>Endereço do CSS One (ex.: https://css-one.exemplo.com). Vazio = integração desligada.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Mesma chave de "Crm:ApiKey" no CSS One.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>O que o painel da TV lê do CSS One (outro sistema, com banco próprio).</summary>
public interface ICssOneFonte
{
    /// <summary>
    /// Ranking de operadores pela nota média de "Satisfação do operador" no mês, da maior nota para a menor. Nulo quando o CSS One não está
    /// configurado ou não respondeu (a tela mostra "indisponível" e segue com o resto).
    /// </summary>
    Task<IReadOnlyList<TvOperadorDto>?> RankingDeOperadoresAsync(int mes, int ano, CancellationToken ct);

    /// <summary>Rastreadores instalados no mês (card "Instalados" do Controle de Rastreador). Nulo quando o CSS One não está configurado ou não respondeu.</summary>
    Task<int?> RastreadoresInstaladosAsync(int mes, int ano, CancellationToken ct);

    /// <summary>Reintegrações (Controle de Cobrança) e eventos finalizados (Registros de Sinistros) do mês. Nulo quando o CSS One não está configurado ou não respondeu.</summary>
    Task<CssOneAdministrativo?> AdministrativoAsync(int mes, int ano, CancellationToken ct);
}

/// <summary>Um registro de reintegração ou de evento finalizado do CSS One. <c>Id</c> identifica o registro entre uma consulta e outra.</summary>
public sealed record CssOneRegistro(string Id, string Pessoa, string? Cliente, string? Placa, string? TipoEvento, DateTimeOffset Data);

/// <summary>Do mais recente para o mais antigo.</summary>
public sealed record CssOneAdministrativo(IReadOnlyList<CssOneRegistro> Reintegracoes, IReadOnlyList<CssOneRegistro> EventosFinalizados);

public sealed class CssOneFonte(HttpClient http, IOptions<CssOneOptions> opcoes, IMemoryCache cache, ILogger<CssOneFonte> logger) : ICssOneFonte
{
    private static readonly TimeSpan Validade = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim Consultando = new(1, 1);

    /// <summary>Guarda o resultado (inclusive "nulo") no cache.</summary>
    private sealed record Caixa<T>(T? Valor);

    internal sealed record RespostaDoCssOne(IReadOnlyList<LinhaDoCssOne>? Operadores);

    internal sealed record LinhaDoCssOne(string Operador, double NotaMedia, int QtdComNota);

    private sealed record InstaladosDoCssOne(int Instalados);

    public Task<IReadOnlyList<TvOperadorDto>?> RankingDeOperadoresAsync(int mes, int ano, CancellationToken ct) =>
        ObterAsync($"operadores:{ano}-{mes}", $"satisfacao-operadores?mes={mes}&ano={ano}",
            (RespostaDoCssOne? r) => Ordenar(r?.Operadores ?? []), ct);

    public Task<int?> RastreadoresInstaladosAsync(int mes, int ano, CancellationToken ct) =>
        ObterAsync($"instalados:{ano}-{mes}", $"rastreadores-instalados?mes={mes}&ano={ano}",
            (InstaladosDoCssOne? r) => r is null ? (int?)null : r.Instalados, ct);

    public Task<CssOneAdministrativo?> AdministrativoAsync(int mes, int ano, CancellationToken ct) =>
        ObterAsync($"administrativo:{ano}-{mes}", $"administrativo?mes={mes}&ano={ano}",
            (CssOneAdministrativoResposta? r) => r is null ? null : new CssOneAdministrativo(r.Reintegracoes ?? [], r.EventosFinalizados ?? []), ct);

    private sealed record CssOneAdministrativoResposta(IReadOnlyList<CssOneRegistro>? Reintegracoes, IReadOnlyList<CssOneRegistro>? EventosFinalizados);

    private async Task<TSaida?> ObterAsync<TResposta, TSaida>(string nome, string rota, Func<TResposta?, TSaida?> mapear, CancellationToken ct)
    {
        var o = opcoes.Value;
        if (string.IsNullOrWhiteSpace(o.BaseUrl) || string.IsNullOrWhiteSpace(o.ApiKey)) return default;

        var chave = $"cssone:{nome}";
        if (cache.TryGetValue(chave, out Caixa<TSaida>? pronto) && pronto is not null) return pronto.Valor;

        await Consultando.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(chave, out pronto) && pronto is not null) return pronto.Valor;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{o.BaseUrl.TrimEnd('/')}/api/integracoes/crm/{rota}");
                req.Headers.Add("X-Api-Key", o.ApiKey);
                using var resp = await http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                var valor = mapear(await resp.Content.ReadFromJsonAsync<TResposta>(cancellationToken: ct));
                cache.Set(chave, new Caixa<TSaida>(valor), Validade);
                cache.Set(chave + ":ultimo", new Caixa<TSaida>(valor), TimeSpan.FromHours(6));
                return valor;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao consultar o CSS One para o painel da TV ({Rota}).", rota);
                // Mantém o último resultado bom, para o painel não "piscar" a cada falha; tenta de novo em instantes.
                var fallback = cache.TryGetValue(chave + ":ultimo", out Caixa<TSaida>? ultimo) && ultimo is not null ? ultimo.Valor : default;
                cache.Set(chave, new Caixa<TSaida>(fallback), TimeSpan.FromSeconds(20));
                return fallback;
            }
        }
        finally
        {
            Consultando.Release();
        }
    }

    /// <summary>Maior nota média primeiro; empate fica com quem tem mais avaliações, depois por nome (ordem estável entre leituras).</summary>
    internal static IReadOnlyList<TvOperadorDto> Ordenar(IEnumerable<LinhaDoCssOne> linhas) =>
        linhas.Where(l => !string.IsNullOrWhiteSpace(l.Operador) && l.QtdComNota > 0)
            .OrderByDescending(l => l.NotaMedia).ThenByDescending(l => l.QtdComNota).ThenBy(l => l.Operador, StringComparer.OrdinalIgnoreCase)
            .Select((l, i) => new TvOperadorDto(i + 1, l.Operador.Trim(), l.NotaMedia, l.QtdComNota))
            .ToList();
}
