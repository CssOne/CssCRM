namespace CssVision.Web.Api.Contracts;

public record LoginRequest(string Email, string Senha);

public record MenuItemDto(string Chave, string Rotulo, string Icone, string Rota);

public record SessionDto(
    Guid Id,
    string Email,
    string NomeCompleto,
    IReadOnlyList<string> Papeis,
    string AreaInicial,
    IReadOnlyList<MenuItemDto> Menu);
