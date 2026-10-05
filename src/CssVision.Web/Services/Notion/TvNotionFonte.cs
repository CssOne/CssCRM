using System.Globalization;
using System.Text.Json;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Notion;

/// <summary>Venda confirmada que existe só no Notion (base MG134), para o painel da TV.</summary>
public record TvNotionVenda(
    string PageId, string Consultor, string? ConsultorNotionId, string? ConsultorEmail, string? FotoUrl, string? Cliente, string? Placa,
    decimal Adesao, DateTimeOffset DataVenda, DateTimeOffset AtualizadaEm, string Regional);

/// <summary>
/// O que o painel da TV lê direto do Notion, além do CRM: as vendas das bases MG134 (que saíram da sincronização do quadro de leads)
/// e os indicadores administrativos (reintegrações, eventos finalizados, rastreadores). Consulta curta em cache; se o Notion falhar,
/// o painel segue com o que o CRM tem.
/// </summary>
public interface ITvNotionFonte
{
    /// <summary>Sobe a cada atualização vinda do Notion (entra na chave do cache do painel).</summary>
    long Versao { get; }

    Task<IReadOnlyList<TvNotionVenda>> VendasAsync(DateOnly primeiroDiaDoMes, CancellationToken ct);

    /// <summary>Nulo quando o Notion não está configurado ou falhou (a tela mostra "Notion indisponível").</summary>
    Task<TvAdministrativoDto?> AdministrativoAsync(DateOnly primeiroDiaDoMes, CancellationToken ct);
}

public sealed class TvNotionFonte(IOptions<NotionSyncOptions> opcoes, IMemoryCache cache, ILogger<TvNotionFonte> logger) : ITvNotionFonte
{
    /// <summary>MG134 e MG134 Consultores Externos: as duas saíram da sincronização do CRM, mas as vendas delas contam no painel.</summary>
    private static readonly string[] BasesMg134 = ["1a163799-99a8-81e2-82be-000bb2817da0", "31763799-99a8-8118-b573-000b24781bfe"];
    private const string RegionalMg134 = "MG134";
    private const string BaseOperacional = "1fa63799-99a8-8367-af6a-079831d4719c";
    private static readonly TimeSpan Validade = TimeSpan.FromSeconds(45);

    private static readonly (string Id, string Status, string Rotulo, string Acao)[] Indicadores =
    [
        ("reintegration", "REINTEGRACAO", "Reintegrações", "Reintegração realizada"),
        ("claim", "EVENTO FINALIZADO", "Eventos finalizados", "Evento finalizado"),
        ("tracker", "RASTREADOR", "Rastreadores", "Rastreador instalado"),
    ];

    private static readonly SemaphoreSlim Consultando = new(1, 1);
    private long _versao;

    public long Versao => Interlocked.Read(ref _versao);

    private string? Token => string.IsNullOrWhiteSpace(opcoes.Value.Token) ? null : opcoes.Value.Token;

    public Task<IReadOnlyList<TvNotionVenda>> VendasAsync(DateOnly primeiroDiaDoMes, CancellationToken ct) =>
        ObterAsync($"tv-notion-vendas:{primeiroDiaDoMes:yyyy-MM}", (IReadOnlyList<TvNotionVenda>)[], client => ConsultarVendasAsync(client, primeiroDiaDoMes, ct), ct);

    public async Task<TvAdministrativoDto?> AdministrativoAsync(DateOnly primeiroDiaDoMes, CancellationToken ct) =>
        await ObterAsync<TvAdministrativoDto?>($"tv-notion-admin:{primeiroDiaDoMes:yyyy-MM}", null, client => ConsultarAdministrativoAsync(client, primeiroDiaDoMes, ct), ct);

    /// <summary>Guarda o resultado (inclusive "nulo") no cache.</summary>
    private sealed record Caixa<T>(T Valor);

    private async Task<T> ObterAsync<T>(string chave, T padrao, Func<NotionClient, Task<T>> consultar, CancellationToken ct)
    {
        if (Token is not { } token) return padrao;
        if (cache.TryGetValue(chave, out Caixa<T>? pronto) && pronto is not null) return pronto.Valor;

        await Consultando.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(chave, out pronto) && pronto is not null) return pronto.Valor;
            try
            {
                var valor = await consultar(new NotionClient(token));
                cache.Set(chave, new Caixa<T>(valor), Validade);
                cache.Set(chave + ":ultimo", new Caixa<T>(valor), TimeSpan.FromHours(6));
                Interlocked.Increment(ref _versao);
                return valor;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao consultar o Notion para o painel da TV ({Chave}).", chave);
                // Mantém o último resultado bom, para o painel não "piscar" a cada falha; sem ele, usa o padrão. Tenta de novo em instantes.
                var fallback = cache.TryGetValue(chave + ":ultimo", out Caixa<T>? ultimo) && ultimo is not null ? ultimo.Valor : padrao;
                cache.Set(chave, new Caixa<T>(fallback), TimeSpan.FromSeconds(20));
                return fallback;
            }
        }
        finally
        {
            Consultando.Release();
        }
    }

    // ---------- vendas (MG134) ----------

    private static async Task<IReadOnlyList<TvNotionVenda>> ConsultarVendasAsync(NotionClient client, DateOnly primeiro, CancellationToken ct)
    {
        var de = primeiro.ToString("yyyy-MM-dd");
        var ate = primeiro.AddMonths(1).ToString("yyyy-MM-dd");
        // "Venda concluída" com Data da venda no mês — ou ainda sem Data da venda mas com o card criado no mês (cadastro recente).
        object filtro = new
        {
            or = new object[]
            {
                new { and = new object[]
                {
                    new { property = "Status", select = new { equals = "VENDA CONCLUIDA" } },
                    new { property = "Data da venda", date = new { on_or_after = de } },
                    new { property = "Data da venda", date = new { before = ate } },
                } },
                new { and = new object[]
                {
                    new { property = "Status", select = new { equals = "VENDA CONCLUIDA" } },
                    new { property = "Data da venda", date = new { is_empty = true } },
                    new { property = "Data de chegada", created_time = new { on_or_after = de } },
                    new { property = "Data de chegada", created_time = new { before = ate } },
                } },
            }
        };

        var vistos = new HashSet<string>();
        var vendas = new List<TvNotionVenda>();
        foreach (var fonte in BasesMg134)
        {
            await foreach (var pagina in client.QueryAsync(fonte, filtro, ct))
            {
                if (Mapear(pagina) is { } venda && vistos.Add(venda.PageId)) vendas.Add(venda);
            }
        }
        return vendas;
    }

    private static TvNotionVenda? Mapear(JsonElement pagina)
    {
        // Sem vendedor com nome, a venda não tem a quem ser atribuída no ranking.
        if (pagina.PrimeiroVendedor("Vendedor") is not { NomeDesconhecido: false, EhBot: false } vendedor) return null;
        var adesao = (decimal)(pagina.Number("Adesão") ?? 0);
        var criada = pagina.TryGetProperty("created_time", out var c) && c.GetString() is { } cs
            ? DateTimeOffset.Parse(cs, CultureInfo.InvariantCulture).ToUniversalTime()
            : DateTimeOffset.UtcNow;
        var dataVenda = DataDoNotion.ParaInstante(pagina.DateStart("Data da venda")) ?? criada;
        return new TvNotionVenda(
            pagina.PageId(), vendedor.Nome, vendedor.Id, vendedor.Email, FotoDoVendedor(pagina), pagina.Text("Name"), Normalizar(pagina.Text("Placa")),
            adesao, dataVenda, pagina.LastEditedTime().ToUniversalTime(), RegionalMg134);
    }

    private static string? FotoDoVendedor(JsonElement pagina)
    {
        if (!pagina.GetProperty("properties").TryGetProperty("Vendedor", out var prop) || prop.GetProperty("type").GetString() != "people") return null;
        var pessoas = prop.GetProperty("people");
        return pessoas.GetArrayLength() > 0 && pessoas[0].TryGetProperty("avatar_url", out var url) && url.ValueKind == JsonValueKind.String && url.GetString() is { Length: > 0 } u ? u : null;
    }

    /// <summary>Placa em maiúsculas, só letras e números ("abc-1d23" = "ABC1D23") — chave para não contar a mesma venda duas vezes.</summary>
    public static string? Normalizar(string? placa)
    {
        if (string.IsNullOrWhiteSpace(placa)) return null;
        var limpa = new string(placa.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return limpa.Length >= 5 ? limpa : null;
    }

    // ---------- administrativo ----------

    private static async Task<TvAdministrativoDto?> ConsultarAdministrativoAsync(NotionClient client, DateOnly primeiro, CancellationToken ct)
    {
        var filtro = new { property = "Data de chegada", created_time = new { on_or_after = primeiro.ToString("yyyy-MM-dd") } };
        var registros = new List<(string Indicador, TvAdministrativoRegistroDto Registro, DateTimeOffset Criado)>();
        await foreach (var pagina in client.QueryAsync(BaseOperacional, filtro, ct))
        {
            if (MapearRegistro(pagina) is { } item) registros.Add(item);
        }

        var diaBrasilia = HorarioBrasilia.Hoje;
        var indicadores = Indicadores.Select(def =>
        {
            var doIndicador = registros.Where(r => r.Indicador == def.Id).OrderByDescending(r => r.Criado).ToList();
            return new TvAdministrativoIndicadorDto(def.Id, def.Rotulo, def.Acao, doIndicador.Count,
                doIndicador.Count(r => HorarioBrasilia.Dia(r.Criado) == diaBrasilia), doIndicador.FirstOrDefault().Registro);
        }).ToList();
        return new TvAdministrativoDto(indicadores);
    }

    private static (string Indicador, TvAdministrativoRegistroDto Registro, DateTimeOffset Criado)? MapearRegistro(JsonElement pagina)
    {
        var status = StatusDe(pagina);
        var definicao = Indicadores.FirstOrDefault(d => d.Status == status);
        if (definicao.Id is null) return null;

        var criado = DateTimeOffset.Parse(pagina.GetProperty("created_time").GetString()!, CultureInfo.InvariantCulture).ToUniversalTime();
        var data = definicao.Id == "reintegration" ? pagina.DateStart("Data da reintegração") : pagina.DateStart("Data da finalização");
        var vendedor = pagina.PrimeiroVendedor("Vendedor");
        var registro = new TvAdministrativoRegistroDto(
            vendedor?.Nome ?? "Não informado", vendedor is null ? null : FotoDoVendedor(pagina), pagina.Text("Name"), pagina.Text("Placa"),
            pagina.Text("Tipo de evento"), data ?? criado.ToString("O", CultureInfo.InvariantCulture));
        return (definicao.Id, registro, criado);
    }

    /// <summary>Status do card (select ou status), sem acento e em maiúsculas.</summary>
    private static string StatusDe(JsonElement pagina)
    {
        if (!pagina.GetProperty("properties").TryGetProperty("Status", out var prop)) return "";
        var tipo = prop.GetProperty("type").GetString();
        var valor = tipo is "select" or "status" ? prop.GetProperty(tipo) : default;
        var nome = valor.ValueKind == JsonValueKind.Object && valor.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var decomposto = nome.Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposto.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant().Trim();
    }
}
