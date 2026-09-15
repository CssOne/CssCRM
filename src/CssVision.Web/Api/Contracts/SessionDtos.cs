namespace CssVision.Web.Api.Contracts;

public record LoginRequest(string Email, string Senha, bool ManterConectado = true);

public record MenuItemDto(string Chave, string Rotulo, string Icone, string Rota);

public record SessionDto(
    Guid Id,
    string Email,
    string NomeCompleto,
    string? FotoUrl,
    IReadOnlyList<string> Papeis,
    string AreaInicial,
    IReadOnlyList<MenuItemDto> Menu);

/// <summary>Resultado de uma tentativa de login: ou já veio com a sessão pronta, ou precisa do segundo fator.</summary>
public record LoginResultDto(bool RequerDoisFatores, SessionDto? Sessao);

public record TwoFactorLoginRequest(string Codigo, bool ManterConectado = true, bool CodigoRecuperacao = false);

public record ProfileDto(
    Guid Id,
    string NomeCompleto,
    string Email,
    string? Telefone,
    string? FotoUrl,
    bool DoisFatoresAtivo,
    IReadOnlyList<string> Papeis,
    bool PodeExcluirPropriaConta);

public record UpdateProfileRequest(string NomeCompleto, string Email, string? Telefone);

public record ChangePasswordRequest(string SenhaAtual, string NovaSenha);

public record TwoFactorSetupDto(string ChaveManual, string UriQrCode);

public record TwoFactorEnableRequest(string Codigo);

public record TwoFactorEnableResultDto(IReadOnlyList<string> CodigosRecuperacao);

public record TwoFactorDisableRequest(string Senha);

public record DeleteAccountRequest(string Senha);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordConfirmRequest(string Email, string Token, string NovaSenha);
