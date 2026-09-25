using CssVision.Web.Api.Contracts.Common;

namespace CssVision.Web.Api.Contracts.Crm;

public record UserSummaryDto(
    Guid Id,
    string NomeCompleto,
    string Email,
    string? Telefone,
    IReadOnlyList<string> Papeis,
    Guid? RegionalId,
    string? RegionalNome,
    Guid? GestorComercialId,
    string? GestorComercialNome,
    Guid? GrupoId,
    string? GrupoNome,
    bool Ativo,
    int? LimiteMensalLeads,
    string? FotoUrl,
    DateTimeOffset CriadoEm,
    IReadOnlyList<string>? RecebeSomenteOQue = null);

public record UserFilterRequest : PagedRequest
{
    public string? Busca { get; init; }
    public string? Papel { get; init; }
    public Guid? RegionalId { get; init; }
    public Guid? GrupoId { get; init; }
    public bool? Ativo { get; init; }
}

public record UserCreateRequest(
    string NomeCompleto,
    string Email,
    string Senha,
    string? Telefone,
    string Papel,
    Guid? RegionalId,
    Guid? GestorComercialId,
    Guid? GrupoId,
    int? LimiteMensalLeads,
    IReadOnlyList<string>? RecebeSomenteOQue = null);

public record UserUpdateRequest(
    string NomeCompleto,
    string? Telefone,
    string Papel,
    Guid? RegionalId,
    Guid? GestorComercialId,
    Guid? GrupoId,
    int? LimiteMensalLeads,
    bool Ativo,
    IReadOnlyList<string>? RecebeSomenteOQue = null);

public record ResetPasswordRequest(string NovaSenha);

public record RegionalDto(Guid Id, string Nome, bool Ativa, int QuantidadeUsuarios);

public record CreateRegionalRequest(string Nome);

public record UpdateRegionalRequest(string Nome, bool Ativa);

public record GrupoMembroDto(Guid Id, string NomeCompleto, string? FotoUrl);

public record GrupoDto(Guid Id, Guid RegionalId, string Nome, bool Ativo, IReadOnlyList<GrupoMembroDto> Consultores);

public record CreateGrupoRequest(Guid? RegionalId, string Nome);

public record UpdateGrupoRequest(string Nome, bool Ativo);

public record UpdateGrupoMembrosRequest(IReadOnlyList<Guid> ConsultorIds);
