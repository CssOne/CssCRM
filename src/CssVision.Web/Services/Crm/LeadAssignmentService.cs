using CssVision.Web.Authorization;
using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LeadAssignmentService(ApplicationDbContext db) : ILeadAssignmentService
{
    public async Task<Guid?> ProximoResponsavelAsync(CancellationToken ct)
    {
        var vendedores = await db.UserRoles
            .Join(db.Roles.Where(r => r.Name == Roles.Comercial), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.Ativo), id => id, u => u.Id, (_, u) => new { u.Id, u.NomeCompleto, u.LimiteMensalLeads })
            .ToListAsync(ct);
        if (vendedores.Count == 0) return null;

        var inicioMes = new DateTimeOffset(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), TimeSpan.Zero);
        var ids = vendedores.Select(v => v.Id).ToList();

        var recebidosNoMes = await db.CrmLeads.AsNoTracking()
            .Where(l => l.ResponsavelId != null && ids.Contains(l.ResponsavelId.Value) && l.CriadoEm >= inicioMes)
            .GroupBy(l => l.ResponsavelId!.Value)
            .Select(g => new { ResponsavelId = g.Key, Quantidade = g.Count() })
            .ToDictionaryAsync(x => x.ResponsavelId, x => x.Quantidade, ct);

        return vendedores
            .Select(v => new { v.Id, v.NomeCompleto, v.LimiteMensalLeads, Recebidos = recebidosNoMes.GetValueOrDefault(v.Id) })
            .Where(v => v.LimiteMensalLeads is null || v.Recebidos < v.LimiteMensalLeads)
            .OrderBy(v => v.Recebidos)
            .ThenBy(v => v.NomeCompleto, StringComparer.OrdinalIgnoreCase)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefault();
    }
}
