using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Resolve quais vendedores o usuário atual pode enxergar: Admin/GestorMaster/SupervisorComercial veem
/// tudo, o Gestor regional (GestorComercial) vê somente a própria regional (RegionalId) — vale também
/// para regionais criadas depois —, Comercial vê apenas a si mesmo. Gestor sem regional (cadastro
/// antigo) cai na equipe que reporta diretamente a ele. Usado por todos os serviços que aplicam
/// escopo de carteira/equipe.
/// </summary>
public interface IEquipeComercialService
{
    /// <summary>
    /// Retorna null quando o usuário tem visão total (sem necessidade de filtrar por vendedor). Administrador com regionais ocultas recebe
    /// a lista de quem NÃO é das regionais ocultas, mais <see cref="Guid.Empty"/> (leads ainda sem responsável). O painel da TV passa
    /// <paramref name="ignorarRegionaisOcultas"/>, porque ele mostra todas as regionais.
    /// </summary>
    Task<List<Guid>?> ObterVendedoresVisiveisAsync(CancellationToken ct, bool ignorarRegionaisOcultas = false);

    /// <summary>Verifica se o usuário atual pode operar sobre registros do vendedor informado.</summary>
    Task<bool> PodeAcessarVendedorAsync(Guid vendedorId, CancellationToken ct);
}

public sealed class EquipeComercialService(ApplicationDbContext db, ICurrentUserService currentUser) : IEquipeComercialService
{
    public async Task<List<Guid>?> ObterVendedoresVisiveisAsync(CancellationToken ct, bool ignorarRegionaisOcultas = false)
    {
        if (currentUser.TemVisaoTotal)
        {
            var ocultas = ignorarRegionaisOcultas ? [] : await EscopoRegional.OcultasAsync(db, currentUser, ct);
            if (ocultas.Count == 0) return null;

            var visiveis = await db.Users.AsNoTracking()
                .Where(u => u.RegionalId == null || !ocultas.Contains(u.RegionalId.Value))
                .Select(u => u.Id).ToListAsync(ct);
            visiveis.Add(Guid.Empty); // lead sem responsável não pertence a nenhuma regional oculta
            return visiveis;
        }

        if (currentUser.IsGestorComercial)
        {
            var regionalId = await db.Users.AsNoTracking()
                .Where(u => u.Id == currentUser.UserId)
                .Select(u => u.RegionalId)
                .FirstOrDefaultAsync(ct);

            var equipe = await db.Users.AsNoTracking()
                .Where(u => regionalId != null ? u.RegionalId == regionalId : u.GestorComercialId == currentUser.UserId)
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (!equipe.Contains(currentUser.UserId)) equipe.Add(currentUser.UserId);
            return equipe;
        }

        return [currentUser.UserId];
    }

    public async Task<bool> PodeAcessarVendedorAsync(Guid vendedorId, CancellationToken ct)
    {
        var visiveis = await ObterVendedoresVisiveisAsync(ct);
        return visiveis is null || visiveis.Contains(vendedorId);
    }
}
