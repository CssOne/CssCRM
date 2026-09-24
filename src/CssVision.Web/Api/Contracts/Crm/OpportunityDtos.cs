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
    string? MotivoPerdaObservacao,
    string? Concorrente,
    string? Observacoes,
    DateOnly? DataAdesao,
    DateTimeOffset? AtivoEm,
    decimal? Mensalidade,
    decimal? MensalidadeComDesconto,
    decimal? MensalidadeComCupom,
    decimal? PagamentoAdesao,
    decimal? Porcentagem,
    bool TermoAdesaoAceito,
    bool Migracao,
    VeiculoDto? Veiculo,
    DateTimeOffset CriadoEm,
    DateTimeOffset? AtualizadoEm,
    uint RowVersion,
    bool Atrasada,
    string? Cpf,
    string? Estado,
    bool? Indicacao,
    string? TipoIndicacao,
    decimal? ValorIndicacao,
    decimal? Total,
    string? TermoAdesaoArquivoUrl,
    string? PagamentoAdesaoArquivoUrl,
    string? ComprovanteIndicacaoArquivoUrl,
    string? ComprovanteVistoriaArquivoUrl,
    /// <summary>Preenchido só quando o Meta confirma (HTTP 2xx) o recebimento da conversão offline — null se nunca tentou ou se falhou.</summary>
    DateTimeOffset? ConversaoOfflineEnviadaEm);

public record VeiculoDto(
    Guid Id,
    string? Descricao,
    string? Placa,
    decimal? Fipe,
    decimal? Rastreador,
    decimal? ValorVistoria,
    Guid? VistoriadorId,
    string? VistoriadorNome,
    DateTimeOffset? DataChegada);

public record VeiculoUpsertRequest(
    string? Descricao,
    string? Placa,
    decimal? Fipe,
    decimal? Rastreador,
    decimal? ValorVistoria,
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
    decimal? MensalidadeComCupom,
    decimal? PagamentoAdesao,
    decimal? Porcentagem,
    bool TermoAdesaoAceito,
    bool Migracao,
    VeiculoUpsertRequest? Veiculo,
    // Campos do formulário de Venda concluída, que a criação de oportunidade passou a usar.
    string? Cpf = null,
    string? Estado = null,
    bool? Indicacao = null,
    string? TipoIndicacao = null,
    decimal? ValorIndicacao = null,
    decimal? Total = null);

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
    decimal? MensalidadeComCupom,
    decimal? PagamentoAdesao,
    decimal? Porcentagem,
    bool TermoAdesaoAceito,
    bool Migracao,
    VeiculoUpsertRequest? Veiculo,
    uint RowVersion,
    string? Cpf = null,
    string? Estado = null,
    bool? Indicacao = null,
    string? TipoIndicacao = null,
    decimal? ValorIndicacao = null,
    decimal? Total = null,
    DateOnly? DataEfetivaFechamento = null);

public record ChangeStageRequest(
    Guid NovaEtapaId,
    uint RowVersion,
    Guid? MotivoPerdaId,
    string? MotivoPerdaObservacao,
    decimal? ValorFinal,
    DateOnly? DataEfetivaFechamento,
    string? Cpf = null,
    string? Estado = null,
    bool? Indicacao = null,
    string? TipoIndicacao = null,
    decimal? ValorIndicacao = null,
    decimal? Total = null,
    DateTimeOffset? AtivoEm = null,
    decimal? Mensalidade = null,
    decimal? MensalidadeComDesconto = null,
    decimal? MensalidadeComCupom = null,
    decimal? PagamentoAdesao = null,
    decimal? Porcentagem = null,
    bool Migracao = false,
    VeiculoUpsertRequest? Veiculo = null);
