using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

public record AnnouncementDto(Guid Id, TipoAnuncio Tipo, string Titulo, string Descricao, string? Cor);
