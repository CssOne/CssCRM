using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

public record ActivityDto(
    Guid Id,
    Guid LeadId,
    string LeadNome,
    Guid? OpportunityId,
    string? OpportunityTitulo,
    Guid ResponsavelId,
    string ResponsavelNome,
    TipoAtividade Tipo,
    string Assunto,
    string? Descricao,
    DateTimeOffset DataHoraPrevista,
    DateTimeOffset? DataHoraConclusao,
    string? Resultado,
    StatusAtividade Status,
    int? LembreteMinutosAntes,
    bool Atrasada,
    uint RowVersion);

public enum VisaoAtividade
{
    Minhas = 1,
    Hoje = 2,
    Proximas = 3,
    Atrasadas = 4,
    Concluidas = 5,
    Semana = 6
}

public record ActivityFilterRequest : PagedRequest
{
    public VisaoAtividade Visao { get; init; } = VisaoAtividade.Minhas;
    public Guid? ResponsavelId { get; init; }
    public TipoAtividade? Tipo { get; init; }
    public Guid? LeadId { get; init; }
    public DateOnly? DataReferencia { get; init; }
}

public record ActivityCreateRequest(
    Guid LeadId,
    Guid? OpportunityId,
    Guid? ResponsavelId,
    TipoAtividade Tipo,
    string Assunto,
    string? Descricao,
    DateTimeOffset DataHoraPrevista,
    int? LembreteMinutosAntes);

public record ActivityUpdateRequest(
    TipoAtividade Tipo,
    string Assunto,
    string? Descricao,
    DateTimeOffset DataHoraPrevista,
    int? LembreteMinutosAntes,
    uint RowVersion);

public record ActivityCompleteRequest(string? Resultado, DateTimeOffset? DataHoraConclusao, uint RowVersion);
