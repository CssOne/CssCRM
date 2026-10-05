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
    IReadOnlyList<string>? RecebeSomenteOQue = null,
    int? LimiteDiarioLeads = null,
    /// <summary>Administrador restrito a esta regional (vê só os dados dela); nulo = todas.</summary>
    Guid? RegionalRestritaId = null,
    string? RegionalRestritaNome = null);

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
    IReadOnlyList<string>? RecebeSomenteOQue = null,
    int? LimiteDiarioLeads = null,
    Guid? RegionalRestritaId = null);

public record UserUpdateRequest(
    string NomeCompleto,
    string? Telefone,
    string Papel,
    Guid? RegionalId,
    Guid? GestorComercialId,
    Guid? GrupoId,
    int? LimiteMensalLeads,
    bool Ativo,
    IReadOnlyList<string>? RecebeSomenteOQue = null,
    int? LimiteDiarioLeads = null,
    /// <summary>Só considerado quando vem de um administrador sem restrição e o papel é Admin/Gestor master; nulo = todas as regionais.</summary>
    Guid? RegionalRestritaId = null,
    /// <summary>Verdadeiro quando o chamador manda o campo (nulo + falso = não mexe; nulo + verdadeiro = remove a restrição).</summary>
    bool AlterarRestricaoRegional = false);

public record ResetPasswordRequest(string NovaSenha);

public record RegionalDto(Guid Id, string Nome, bool Ativa, int QuantidadeUsuarios);

public record CreateRegionalRequest(string Nome);

public record UpdateRegionalRequest(string Nome, bool Ativa);

public record GrupoMembroDto(Guid Id, string NomeCompleto, string? FotoUrl, bool Ativo = true);

public record GrupoDto(Guid Id, Guid RegionalId, string Nome, bool Ativo, IReadOnlyList<GrupoMembroDto> Consultores);

/// <summary>Grupo com o nome da regional, para os filtros do quadro e da lista de leads (inclui os grupos criados depois).</summary>
public record GrupoFiltroDto(Guid Id, Guid RegionalId, string RegionalNome, string Nome);

public record CreateGrupoRequest(Guid? RegionalId, string Nome);

public record UpdateGrupoRequest(string Nome, bool Ativo);

public record UpdateGrupoMembrosRequest(IReadOnlyList<Guid> ConsultorIds);
