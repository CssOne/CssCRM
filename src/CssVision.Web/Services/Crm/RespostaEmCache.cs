using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Cache curto de respostas pesadas de leitura (quadro de leads, painel, gestão comercial).
/// A chave leva a versão do quadro (<see cref="ICrmEventHub.Versao"/>): qualquer mudança em um lead
/// ou oportunidade gera uma versão nova e as respostas antigas deixam de ser usadas na hora; o prazo
/// só limita o que muda sem evento (ex.: foto ou nome de um usuário). Com 50 pessoas no quadro, uma
/// mudança fazia todas recarregarem juntas — agora quem tem o mesmo escopo e filtro divide uma consulta.
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

        var valor = await calcular();
        cache.Set(chaveCompleta, valor, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Prazo });
        return valor;
    }

    /// <summary>Escopo de visibilidade na chave: "todos" para visão total, senão os vendedores visíveis.</summary>
    public static string Escopo(List<Guid>? visiveis) =>
        visiveis is null ? "todos" : string.Join(",", visiveis.OrderBy(v => v));
}
