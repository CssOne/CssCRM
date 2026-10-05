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
    /// <summary>Retorna null quando o usuário tem visão total (sem necessidade de filtrar por vendedor).</summary>
    Task<List<Guid>?> ObterVendedoresVisiveisAsync(CancellationToken ct);

    /// <summary>Verifica se o usuário atual pode operar sobre registros do vendedor informado.</summary>
    Task<bool> PodeAcessarVendedorAsync(Guid vendedorId, CancellationToken ct);
}

public sealed class EquipeComercialService(ApplicationDbContext db, ICurrentUserService currentUser) : IEquipeComercialService
{
    public async Task<List<Guid>?> ObterVendedoresVisiveisAsync(CancellationToken ct)
    {
        if (currentUser.TemVisaoTotal)
        {
            // Administrador restrito a uma regional: só a equipe dela (e ele mesmo).
            if (await EscopoRegional.RestritaAsync(db, currentUser, ct) is not { } restrita) return null;
            var daRegional = await db.Users.AsNoTracking().Where(u => u.RegionalId == restrita).Select(u => u.Id).ToListAsync(ct);
            if (!daRegional.Contains(currentUser.UserId)) daRegional.Add(currentUser.UserId);
            return daRegional;
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
