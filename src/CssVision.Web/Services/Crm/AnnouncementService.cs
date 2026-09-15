using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class AnnouncementService(ApplicationDbContext db) : IAnnouncementService
{
    public async Task<IReadOnlyList<AnnouncementDto>> ObterAsync(TipoAnuncio? tipo, CancellationToken ct)
    {
        var query = db.CrmAnnouncements.AsNoTracking().Where(a => a.Ativo);
        if (tipo.HasValue) query = query.Where(a => a.Tipo == tipo);

        return await query
            .OrderBy(a => a.Ordem)
            .Select(a => new AnnouncementDto(a.Id, a.Tipo, a.Titulo, a.Descricao, a.Cor))
            .ToListAsync(ct);
    }
}
