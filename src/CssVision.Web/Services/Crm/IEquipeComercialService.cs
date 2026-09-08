using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Resolve quais vendedores o usuário atual pode enxergar: Admin/GestorMaster veem tudo,
/// GestorComercial vê a própria equipe (vendedores com GestorComercialId apontando para ele),
/// Comercial vê apenas a si mesmo. Usado por todos os serviços que aplicam escopo de carteira/equipe.
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
        if (currentUser.TemVisaoTotal) return null;

        if (currentUser.IsGestorComercial)
        {
            var equipe = await db.Users.AsNoTracking()
                .Where(u => u.GestorComercialId == currentUser.UserId)
                .Select(u => u.Id)
                .ToListAsync(ct);
            equipe.Add(currentUser.UserId);
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
