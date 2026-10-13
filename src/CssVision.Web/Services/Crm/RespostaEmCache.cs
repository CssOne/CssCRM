using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Cache curto de respostas pesadas de leitura (quadro de leads, painel, gestão comercial).
/// A chave leva a versão do quadro (<see cref="ICrmEventHub.Versao"/>): qualquer mudança em um lead
/// ou oportunidade gera uma versão nova e as respostas antigas deixam de ser usadas na hora; o prazo
/// só limita o que muda sem evento (ex.: foto ou nome de um usuário). Com 50 pessoas no quadro, uma
/// mudança fazia todas recarregarem juntas — agora quem tem o mesmo escopo e filtro divide uma consulta.
/// Enquanto a resposta ainda está sendo calculada, quem pede a mesma chave espera esse cálculo em vez de rodar a mesma consulta
/// de novo: logo depois de uma mudança o cache está vazio e várias abas chegam juntas.
/// </summary>
public static class RespostaEmCache
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(60);

    public static async Task<T> ObterAsync<T>(
        IMemoryCache? cache, ICrmEventHub? eventos, string nome, object chave, Func<Task<T>> calcular)
    {
        if (cache is null || eventos is null) return await calcular();

        var chaveCompleta = $"{nome}:{eventos.Versao}:{JsonSerializer.Serialize(chave)}";
        if (cache.TryGetValue(chaveCompleta, out T? pronto) && pronto is not null) return pronto;

        var meu = new Lazy<Task<object?>>(async () =>
        {
            try
            {
                var valor = await calcular();
                cache.Set(chaveCompleta, valor, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Prazo });
                return valor;
            }
            finally
            {
                EmAndamento.TryRemove(chaveCompleta, out _);
            }
        });

        // Quem registra primeiro faz o cálculo; os demais esperam por ele.
        if (EmAndamento.TryAdd(chaveCompleta, meu)) return (T)(await meu.Value)!;

        if (EmAndamento.TryGetValue(chaveCompleta, out var dele))
        {
            try
            {
                return (T)(await dele.Value)!;
            }
            catch
            {
                // O cálculo de quem chegou primeiro falhou (ex.: ele fechou a aba e cancelou a consulta): não derruba quem esperava.
            }
        }
        return await calcular();
    }

    private static readonly ConcurrentDictionary<string, Lazy<Task<object?>>> EmAndamento = new();

    /// <summary>Escopo de visibilidade na chave: "todos" para visão total, senão os vendedores visíveis.</summary>
    public static string Escopo(List<Guid>? visiveis) =>
        visiveis is null ? "todos" : string.Join(",", visiveis.OrderBy(v => v));
}
