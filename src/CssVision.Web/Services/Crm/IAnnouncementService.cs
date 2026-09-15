using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Crm;

public interface IAnnouncementService
{
    Task<IReadOnlyList<AnnouncementDto>> ObterAsync(TipoAnuncio? tipo, CancellationToken ct);
}
