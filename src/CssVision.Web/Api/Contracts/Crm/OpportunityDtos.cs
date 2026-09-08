using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

public record OpportunityDto(
    Guid Id,
    Guid LeadId,
    string LeadNome,
    string Titulo,
    Guid ResponsavelId,
    string ResponsavelNome,
    Guid EtapaId,
    string EtapaNome,
    TipoEtapaPipeline EtapaTipo,
    DateTimeOffset EtapaDesde,
    string? ProdutoOuServico,
    decimal ValorEstimado,
    int? ProbabilidadeFechamento,
    DateOnly? DataPrevistaFechamento,
    decimal? ValorFinal,
    DateTimeOffset? DataEfetivaFechamento,
    string? MotivoPerdaDescricao,
    string? Concorrente,
    string? Observacoes,
    DateOnly? DataAdesao,
    DateTimeOffset? AtivoEm,
    decimal? Mensalidade,
    decimal? MensalidadeComDesconto,
    decimal? PagamentoAdesao,
    decimal? Porcentagem,
    bool TermoAdesaoAceito,
    bool Migracao,
    VeiculoDto? Veiculo,
    DateTimeOffset CriadoEm,
    DateTimeOffset? AtualizadoEm,
    uint RowVersion,
    bool Atrasada);

public record VeiculoDto(
    Guid Id,
    string? Descricao,
    string? Placa,
    decimal? Fipe,
    string? Rastreador,
    Guid? VistoriadorId,
    string? VistoriadorNome,
    DateTimeOffset? DataChegada);

public record VeiculoUpsertRequest(
    string? Descricao,
    string? Placa,
    decimal? Fipe,
    string? Rastreador,
    Guid? VistoriadorId,
    DateTimeOffset? DataChegada);

public record OpportunityFilterRequest : PagedRequest
{
    public Guid? ResponsavelId { get; init; }
    public Guid? EtapaId { get; init; }
    public string? Origem { get; init; }
    public string? ProdutoOuServico { get; init; }
    public string? Regional { get; init; }
    public DateOnly? DataInicio { get; init; }
    public DateOnly? DataFim { get; init; }
    public TipoEtapaPipeline? StatusEtapa { get; init; }
}

public record OpportunityCreateRequest(
    Guid LeadId,
    string Titulo,
    Guid ResponsavelId,
    Guid? EtapaId,
    string? ProdutoOuServico,
    decimal ValorEstimado,
    int? ProbabilidadeFechamento,
    DateOnly? DataPrevistaFechamento,
    string? Concorrente,
    string? Observacoes,
    DateOnly? DataAdesao,
    decimal? Mensalidade,
    decimal? MensalidadeComDesconto,
    decimal? PagamentoAdesao,
    decimal? Porcentagem,
    bool TermoAdesaoAceito,
    bool Migracao,
    VeiculoUpsertRequest? Veiculo);

public record OpportunityUpdateRequest(
    string Titulo,
    Guid ResponsavelId,
    string? ProdutoOuServico,
    decimal ValorEstimado,
    int? ProbabilidadeFechamento,
    DateOnly? DataPrevistaFechamento,
    string? Concorrente,
    string? Observacoes,
    DateOnly? DataAdesao,
    DateTimeOffset? AtivoEm,
    decimal? Mensalidade,
    decimal? MensalidadeComDesconto,
    decimal? PagamentoAdesao,
    decimal? Porcentagem,
    bool TermoAdesaoAceito,
    bool Migracao,
    VeiculoUpsertRequest? Veiculo,
    uint RowVersion);

public record ChangeStageRequest(
    Guid NovaEtapaId,
    uint RowVersion,
    Guid? MotivoPerdaId,
    decimal? ValorFinal,
    DateOnly? DataEfetivaFechamento);
