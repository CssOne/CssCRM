using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CssVision.Web.Services.Notion;

/// <summary>Cliente mínimo para consultar data sources do Notion (API 2025-09-03, bases multi-fonte).</summary>
public sealed class NotionClient(string token)
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.notion.com/v1/") };

    public IAsyncEnumerable<JsonElement> QueryVendaConcluidaAsync(string dataSourceId, CancellationToken ct = default) =>
        QueryAsync(dataSourceId, new { property = "Status", select = new { equals = "VENDA CONCLUIDA" } }, ct);

    /// <summary>Igual a QueryVendaConcluidaAsync, mas restrita a cards com "Data de chegada" a partir da data informada.</summary>
    public IAsyncEnumerable<JsonElement> QueryVendaConcluidaAsync(string dataSourceId, DateOnly criadoApartirDe, CancellationToken ct = default) =>
        QueryAsync(dataSourceId, new
        {
            and = new object[]
            {
                new { property = "Status", select = new { equals = "VENDA CONCLUIDA" } },
                new { property = "Data de chegada", created_time = new { on_or_after = criadoApartirDe.ToString("yyyy-MM-dd") } },
            }
        }, ct);

    /// <summary>Todas as linhas cujo Status não é "VENDA CONCLUIDA" (essas já foram migradas por QueryVendaConcluidaAsync).</summary>
    public IAsyncEnumerable<JsonElement> QueryNaoVendaConcluidaAsync(string dataSourceId, CancellationToken ct = default) =>
        QueryAsync(dataSourceId, new { property = "Status", select = new { does_not_equal = "VENDA CONCLUIDA" } }, ct);

    /// <summary>
    /// Igual a QueryNaoVendaConcluidaAsync, mas com fatiamento opcional por Status exato e por
    /// intervalo de "Data de chegada" (created_time) — necessário porque uma única consulta tem um
    /// teto de resultados imposto pelo plano do workspace (na prática, ~10 mil linhas por consulta,
    /// mesmo com paginação por cursor correta): bases grandes (e etapas isoladas como "EM ATENDIMENTO"
    /// com mais de 10 mil linhas) precisam ser varridas em fatias menores que esse teto.
    /// </summary>
    public IAsyncEnumerable<JsonElement> QueryNaoVendaConcluidaAsync(
        string dataSourceId, string? statusExato, DateOnly? criadoAntesDe, DateOnly? criadoApartirDe, CancellationToken ct = default)
    {
        var condicoes = new List<object>
        {
            statusExato is not null
                ? new { property = "Status", select = new { equals = statusExato } }
                : new { property = "Status", select = new { does_not_equal = "VENDA CONCLUIDA" } }
        };
        if (criadoAntesDe.HasValue)
        {
            condicoes.Add(new { property = "Data de chegada", created_time = new { before = criadoAntesDe.Value.ToString("yyyy-MM-dd") } });
        }
        if (criadoApartirDe.HasValue)
        {
            condicoes.Add(new { property = "Data de chegada", created_time = new { on_or_after = criadoApartirDe.Value.ToString("yyyy-MM-dd") } });
        }

        object filtro = condicoes.Count == 1 ? condicoes[0] : new { and = condicoes };
        return QueryAsync(dataSourceId, filtro, ct);
    }

    /// <summary>
    /// Todas as linhas editadas após o instante informado — base da sincronização incremental
    /// periódica. Com <paramref name="criadoApartirDe"/>, restringe também pela "Data de chegada"
    /// (created_time) do card — usado pra parar de importar cards antigos que alguém ainda edita
    /// no Notion.
    /// </summary>
    public IAsyncEnumerable<JsonElement> QueryEditadasDesdeAsync(string dataSourceId, DateTimeOffset desde, DateOnly? criadoApartirDe = null, CancellationToken ct = default)
    {
        var filtroEditadas = new { timestamp = "last_edited_time", last_edited_time = new { after = desde.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") } };
        if (!criadoApartirDe.HasValue) return QueryAsync(dataSourceId, filtroEditadas, ct);

        object filtro = new
        {
            and = new object[]
            {
                filtroEditadas,
                new { property = "Data de chegada", created_time = new { on_or_after = criadoApartirDe.Value.ToString("yyyy-MM-dd") } },
            }
        };
        return QueryAsync(dataSourceId, filtro, ct);
    }

    /// <summary>Linhas com "Data de chegada" (created_time) no intervalo [de, ate) — usado no realinhamento em fatias mensais.</summary>
    public IAsyncEnumerable<JsonElement> QueryCriadasEntreAsync(string dataSourceId, DateOnly de, DateOnly ate, CancellationToken ct = default) =>
        QueryAsync(dataSourceId, new
        {
            and = new object[]
            {
                new { property = "Data de chegada", created_time = new { on_or_after = de.ToString("yyyy-MM-dd") } },
                new { property = "Data de chegada", created_time = new { before = ate.ToString("yyyy-MM-dd") } },
            }
        }, ct);

    public async IAsyncEnumerable<JsonElement> QueryAsync(string dataSourceId, object? filter, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? cursor = null;
        do
        {
            var body = new Dictionary<string, object?> { ["page_size"] = 100 };
            if (filter is not null) body["filter"] = filter;
            if (cursor is not null) body["start_cursor"] = cursor;

            using var request = new HttpRequestMessage(HttpMethod.Post, $"data_sources/{dataSourceId}/query")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Notion-Version", "2025-09-03");

            using var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Notion API retornou {(int)response.StatusCode} para {dataSourceId}: {json}");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var page in root.GetProperty("results").EnumerateArray())
            {
                yield return page.Clone();
            }

            var hasMore = root.TryGetProperty("has_more", out var hasMoreEl) && hasMoreEl.GetBoolean();
            cursor = hasMore && root.TryGetProperty("next_cursor", out var cursorEl) && cursorEl.ValueKind == JsonValueKind.String
                ? cursorEl.GetString()
                : null;
        } while (cursor is not null);
    }

    /// <summary>Baixa um arquivo hospedado no Notion (URL assinada, já vem pronta pra GET direto, sem header de auth).</summary>
    public async Task<byte[]?> DownloadFileAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch
        {
            return null;
        }
    }
}
