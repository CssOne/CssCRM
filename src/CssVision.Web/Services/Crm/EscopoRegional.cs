using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// "Administrador com regionais ocultas": quem tem visão total (Admin/Gestor master/Supervisor) pode ter uma ou mais regionais
/// ocultas (ex.: MG134) e deixa de ver os dados delas — quadro e lista de leads, usuários, gestão, relatórios, metas, grupos,
/// regionais e Tráfego pago. As demais regionais, inclusive as criadas depois, continuam visíveis. O painel da TV segue mostrando
/// todas. Gestor regional não usa isso: ele já enxerga só a regional do próprio cadastro.
/// </summary>
public static class EscopoRegional
{
    /// <summary>Regionais ocultas do usuário atual; vazio se ele não tem visão total ou não tem nenhuma oculta.</summary>
    public static async Task<HashSet<Guid>> OcultasAsync(ApplicationDbContext db, ICurrentUserService currentUser, CancellationToken ct)
    {
        if (!currentUser.TemVisaoTotal) return [];
        var texto = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.RegionaisOcultas).FirstOrDefaultAsync(ct);
        return Ler(texto);
    }

    /// <summary>Lê a lista guardada no cadastro (ids separados por vírgula); ignora o que não for um id.</summary>
    public static HashSet<Guid> Ler(string? texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? []
            : texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => Guid.TryParse(t, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();

    public static string? Gravar(IEnumerable<Guid>? ids)
    {
        var lista = (ids ?? []).Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToList();
        return lista.Count == 0 ? null : string.Join(',', lista);
    }
}
