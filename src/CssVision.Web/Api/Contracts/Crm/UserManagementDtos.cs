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
    /// <summary>Regionais ocultas para este administrador (ele não vê os dados delas).</summary>
    IReadOnlyList<Guid>? RegionaisOcultasIds = null,
    IReadOnlyList<string>? RegionaisOcultasNomes = null,
    /// <summary>Administrador que pega leads/atua nas vendas; falso = só administra a plataforma.</summary>
    bool AtuaNasVendas = true);

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
    IReadOnlyList<Guid>? RegionaisOcultasIds = null,
    bool? AtuaNasVendas = null);

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
    /// <summary>Regionais a ocultar deste administrador; só vale com <see cref="AlterarRegionaisOcultas"/> e vindo de um administrador sem regionais ocultas.</summary>
    IReadOnlyList<Guid>? RegionaisOcultasIds = null,
    bool AlterarRegionaisOcultas = false,
    bool? AtuaNasVendas = null);

/// <summary>Ativar/inativar a conta de um consultor (tela de Usuários, também para o Financeiro).</summary>
public record UserAtivoRequest(bool Ativo);

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
