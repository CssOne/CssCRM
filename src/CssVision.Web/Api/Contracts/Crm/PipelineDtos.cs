using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

public record PipelineStageDto(Guid Id, string Nome, int Ordem, TipoEtapaPipeline Tipo, string? Cor, bool Ativa);

public record PipelineCardDto(
    Guid OpportunityId,
    Guid LeadId,
    string LeadNome,
    string Titulo,
    string? ProdutoOuServico,
    decimal ValorEstimado,
    Guid ResponsavelId,
    string ResponsavelNome,
    string? Origem,
    DateTimeOffset? ProximaAtividadeEm,
    string? ProximaAtividadeAssunto,
    DateTimeOffset EtapaDesde,
    bool Atrasada,
    uint RowVersion);

public record PipelineColumnDto(PipelineStageDto Etapa, IReadOnlyList<PipelineCardDto> Cartoes, decimal ValorTotal);

public record PipelineBoardDto(IReadOnlyList<PipelineColumnDto> Colunas);

public record PipelineFilterRequest
{
    public Guid? ResponsavelId { get; init; }
    public string? Origem { get; init; }
    public string? ProdutoOuServico { get; init; }
    public string? Regional { get; init; }
    public DateOnly? DataInicio { get; init; }
    public DateOnly? DataFim { get; init; }
    public bool IncluirFechadas { get; init; }
}

public record CreateStageRequest(string Nome, int Ordem, string? Cor);
public record LossReasonDto(Guid Id, string Descricao, bool Ativo);
public record CreateLossReasonRequest(string Descricao);
