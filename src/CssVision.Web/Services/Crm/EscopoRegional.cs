using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// "Administrador restrito a uma regional": um Admin/Gestor master/Supervisor pode ficar limitado a UMA regional (ex.: MG132) e deixa de
/// ver os dados das outras — quadro de leads, lista de leads, usuários, gestão, relatórios, painel, regionais e Tráfego pago. Só quem tem
/// visão total pode ter essa restrição; todos os outros já enxergam só a própria regional.
/// </summary>
public static class EscopoRegional
{
    /// <summary>Regional à qual o usuário atual está restrito; nulo se ele não tem visão total ou não tem restrição.</summary>
    public static async Task<Guid?> RestritaAsync(ApplicationDbContext db, ICurrentUserService currentUser, CancellationToken ct)
    {
        if (!currentUser.TemVisaoTotal) return null;
        return await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.RegionalRestritaId).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Regional que vale para o usuário atual: a restrição, se ele for administrador restrito; senão a própria regional do cadastro.
    /// </summary>
    public static async Task<Guid?> EfetivaAsync(ApplicationDbContext db, ICurrentUserService currentUser, CancellationToken ct) =>
        await RestritaAsync(db, currentUser, ct)
        ?? await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.RegionalId).FirstOrDefaultAsync(ct);
}
