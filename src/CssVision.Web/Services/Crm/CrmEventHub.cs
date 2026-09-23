using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Notifica as telas abertas (via Server-Sent Events, ver CrmEventosController) de que o quadro
/// de leads mudou — seja por um usuário do CRM, seja pela sincronização com o Notion.
/// O evento não carrega dados: cada tela recarrega pela API, que já aplica o escopo de carteira.
/// </summary>
public interface ICrmEventHub
{
    void PublicarQuadroAtualizado(string origem);

    /// <summary>Registra um assinante (uma aba aberta). Descartar a assinatura remove o assinante.</summary>
    CrmEventoAssinatura Assinar();
}

public sealed record CrmEvento(string Tipo, string Origem, DateTimeOffset OcorridoEm);

public sealed class CrmEventoAssinatura(ChannelReader<CrmEvento> leitor, Action aoDescartar) : IDisposable
{
    public ChannelReader<CrmEvento> Leitor { get; } = leitor;

    public void Dispose() => aoDescartar();
}

public sealed class CrmEventHub : ICrmEventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<CrmEvento>> _assinantes = new();

    public void PublicarQuadroAtualizado(string origem)
    {
        var evento = new CrmEvento("quadro-atualizado", origem, DateTimeOffset.UtcNow);
        foreach (var canal in _assinantes.Values)
        {
            canal.Writer.TryWrite(evento);
        }
    }

    public CrmEventoAssinatura Assinar()
    {
        // Canal limitado: se uma aba ficar lenta, descarta eventos antigos (todos significam "recarregue").
        var canal = Channel.CreateBounded<CrmEvento>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _assinantes[id] = canal;

        return new CrmEventoAssinatura(canal.Reader, () =>
        {
            if (_assinantes.TryRemove(id, out var removido)) removido.Writer.TryComplete();
        });
    }
}
