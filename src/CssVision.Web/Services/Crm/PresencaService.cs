using System.Collections.Concurrent;

namespace CssVision.Web.Services.Crm;

/// <summary>Quem está com o CRM aberto agora. Guardado em memória: reiniciar o servidor zera, e em segundos todo mundo volta a aparecer.</summary>
public interface IPresencaService
{
    /// <summary>Registra que a pessoa está com o CRM aberto neste instante (a tela chama a cada minuto, só com a aba visível).</summary>
    void Marcar(Guid usuarioId);

    bool EstaOnline(Guid usuarioId);

    /// <summary>Quem, dentre os informados, está online.</summary>
    IReadOnlySet<Guid> Online(IEnumerable<Guid> usuarioIds);

    /// <summary>Todas as pessoas online agora.</summary>
    IReadOnlyList<Guid> TodosOnline();
}

/// <summary>
/// "Online" = o CRM da pessoa avisou que está aberto (e visível) há menos de <see cref="Validade"/>. Não é a presença do Discord: ela só existe
/// numa conexão permanente com o Discord, que o CRM não mantém. Para conversar pelo CRM, estar com o CRM aberto é o que importa.
/// </summary>
public sealed class PresencaService(TimeProvider? relogio = null) : IPresencaService
{
    /// <summary>A tela avisa a cada 60 s; dá folga para uma consulta atrasada sem piscar "offline".</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromSeconds(150);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _vistos = new();

    private DateTimeOffset Agora => (relogio ?? TimeProvider.System).GetUtcNow();

    public void Marcar(Guid usuarioId) => _vistos[usuarioId] = Agora;

    public bool EstaOnline(Guid usuarioId) => _vistos.TryGetValue(usuarioId, out var visto) && Agora - visto <= Validade;

    public IReadOnlySet<Guid> Online(IEnumerable<Guid> usuarioIds) => usuarioIds.Where(EstaOnline).ToHashSet();

    public IReadOnlyList<Guid> TodosOnline()
    {
        var agora = Agora;
        return _vistos.Where(v => agora - v.Value <= Validade).Select(v => v.Key).ToList();
    }
}
