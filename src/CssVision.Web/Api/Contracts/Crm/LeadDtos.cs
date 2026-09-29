using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

public record LeadListItemDto(
    Guid Id,
    string NomeOuRazaoSocial,
    TipoPessoa TipoPessoa,
    string? DocumentoMascarado,
    string? Telefone,
    string? Telefone2,
    string? Email,
    string? Cidade,
    string? Estado,
    string? Regional,
    string? Origem,
    string? Placa,
    bool? TemSeguro,
    string? UtilidadeVeiculo,
    Guid? EtapaId,
    string? EtapaNome,
    string? EtapaCor,
    string? EtapaAtual,
    Guid? ResponsavelId,
    string? ResponsavelNome,
    IReadOnlyList<string> Tags,
    DateTimeOffset CriadoEm,
    DateTimeOffset? UltimoContatoEm,
    DateTimeOffset? ProximoContatoEm,
    bool SemContato,
    bool Arquivado);

public record LeadFilterRequest : PagedRequest
{
    public string? Busca { get; init; }
    public Guid? ResponsavelId { get; init; }
    public string? Regional { get; init; }
    public string? Origem { get; init; }
    /// <summary>Etapa do lead no quadro de leads (CrmLeadStage) — distinta de EtapaId, que filtra pela etapa da oportunidade aberta.</summary>
    public Guid? LeadEtapaId { get; init; }
    public Guid? EtapaId { get; init; }
    public DateOnly? DataInicio { get; init; }
    public DateOnly? DataFim { get; init; }
    public List<string>? Tags { get; init; }
    public bool IncluirArquivados { get; init; }
    public string? OrdenarPor { get; init; } = "criadoEm";
    public bool OrdemDescendente { get; init; } = true;
}

public record LeadDetailDto(
    Guid Id,
    string NomeOuRazaoSocial,
    TipoPessoa TipoPessoa,
    string? Documento,
    string? Telefone,
    string? Telefone2,
    string? WhatsApp,
    string? Email,
    DateOnly? DataNascimento,
    string? Cidade,
    string? Estado,
    string? Regional,
    string? Origem,
    string? Campanha,
    string? ProdutoInteresse,
    string? Placa,
    bool? TemSeguro,
    string? UtilidadeVeiculo,
    string? Gclid,
    string? UtmMedium,
    string? UtmSource,
    string? UtmTerm,
    string? MetaClickId,
    string? MetaFormId,
    string? MetaLeadId,
    Guid? IndicadoPorLeadId,
    string? IndicadoPorLeadNome,
    string? TipoIndicacao,
    bool CriadoManualmente,
    Guid? EtapaId,
    string? EtapaNome,
    string? EtapaCor,
    Guid? MotivoPerdaId,
    string? MotivoPerdaDescricao,
    string? MotivoPerdaObservacao,
    string? VeiculoNaoAtendido,
    Guid? ResponsavelId,
    string? ResponsavelNome,
    string? Observacoes,
    bool ConsentimentoContato,
    DateTimeOffset? ConsentimentoDataEm,
    string? ConsentimentoOrigem,
    IReadOnlyList<string> Tags,
    IReadOnlyList<LeadOpportunitySummaryDto> Oportunidades,
    DateTimeOffset CriadoEm,
    DateTimeOffset? AtualizadoEm,
    uint RowVersion,
    bool Arquivado,
    /// <summary>Valor da adesão informado na "Cotação" — pré-preenche Oportunidade e Venda concluída.</summary>
    decimal? ValorAdesao = null,
    string? UtmCampaign = null,
    /// <summary>Card de outro veículo do mesmo cliente: o card original (ver CrmLead.VeiculoAdicionalDeLeadId).</summary>
    Guid? VeiculoAdicionalDeLeadId = null,
    string? VeiculoAdicionalDeLeadNome = null,
    /// <summary>CPF/CNPJ do card original — a venda do veículo adicional usa o mesmo documento.</summary>
    string? VeiculoAdicionalDeDocumento = null);

/// <summary>
/// Cria cards para outros veículos do mesmo cliente. EtapaId = coluna onde os cards novos entram
/// (nulo = a do card original, ou "Em atendimento" se ele já fechou — ver LeadService);
/// Quantidade = quantos veículos a mais (1 a 10). VendaConcluida = o card nasce direto em
/// "Venda concluída" (Leads/Indicação, pela etiqueta do cliente) — é o caso do formulário "Outro
/// veículo", que já registra a venda do veículo novo.
/// </summary>
public record LeadVeiculosAdicionaisRequest(Guid? EtapaId, int Quantidade = 1, bool VendaConcluida = false);

public record LeadOpportunitySummaryDto(
    Guid Id,
    string Titulo,
    string EtapaNome,
    TipoEtapaPipeline EtapaTipo,
    decimal ValorEstimado,
    DateOnly? DataPrevistaFechamento,
    bool Ativa,
    bool Migracao,
    bool? Indicacao,
    string? Cpf,
    string? Estado,
    DateTimeOffset? AtivoEm,
    decimal? Porcentagem,
    decimal? Mensalidade,
    decimal? MensalidadeComDesconto,
    decimal? MensalidadeComCupom,
    decimal? PagamentoAdesao,
    decimal? Total,
    string? TipoIndicacao,
    decimal? ValorIndicacao,
    LeadOpportunityVeiculoSummaryDto? Veiculo,
    // Anexos da venda concluída (URLs públicas do armazenamento) e a data combinada para a adesão.
    string? TermoAdesaoArquivoUrl = null,
    string? PagamentoAdesaoArquivoUrl = null,
    string? ComprovanteIndicacaoArquivoUrl = null,
    string? ComprovanteVistoriaArquivoUrl = null,
    DateOnly? DataPagamentoAdesaoPrevista = null);

public record LeadOpportunityVeiculoSummaryDto(
    string? Descricao,
    string? Placa,
    decimal? Fipe,
    decimal? Rastreador,
    decimal? ValorVistoria,
    DateTimeOffset? DataChegada,
    string? Chassi = null);

public record LeadCreateRequest(
    string NomeOuRazaoSocial,
    TipoPessoa TipoPessoa,
    string? Documento,
    string? Telefone,
    string? Telefone2,
    string? WhatsApp,
    string? Email,
    DateOnly? DataNascimento,
    string? Cidade,
    string? Estado,
    string? Regional,
    string? Origem,
    string? Campanha,
    string? ProdutoInteresse,
    string? Placa,
    bool? TemSeguro,
    string? UtilidadeVeiculo,
    string? Gclid,
    string? UtmMedium,
    string? UtmSource,
    string? UtmTerm,
    string? MetaClickId,
    string? MetaFormId,
    string? MetaLeadId,
    Guid? IndicadoPorLeadId,
    string? TipoIndicacao,
    Guid? ResponsavelId,
    Guid? EtapaId,
    List<string>? Tags,
    string? Observacoes,
    bool ConsentimentoContato,
    string? ConsentimentoOrigem,
    bool IgnorarDuplicidade = false,
    string? UtmCampaign = null);

public record LeadUpdateRequest(
    string NomeOuRazaoSocial,
    TipoPessoa TipoPessoa,
    string? Documento,
    string? Telefone,
    string? Telefone2,
    string? WhatsApp,
    string? Email,
    DateOnly? DataNascimento,
    string? Cidade,
    string? Estado,
    string? Regional,
    string? Origem,
    string? Campanha,
    string? ProdutoInteresse,
    string? Placa,
    bool? TemSeguro,
    string? UtilidadeVeiculo,
    string? Gclid,
    string? UtmMedium,
    string? UtmSource,
    string? UtmTerm,
    string? MetaClickId,
    string? MetaFormId,
    string? MetaLeadId,
    Guid? IndicadoPorLeadId,
    string? TipoIndicacao,
    List<string>? Tags,
    string? Observacoes,
    bool ConsentimentoContato,
    string? ConsentimentoOrigem,
    uint RowVersion,
    string? UtmCampaign = null);

public record LeadAssignRequest(Guid ResponsavelId, string? Motivo);

public record LeadBulkAssignRequest(List<Guid> LeadIds, Guid ResponsavelId, string? Motivo);

public record LeadDuplicateWarningDto(Guid LeadExistenteId, string NomeExistente, string CampoDuplicado);

public record LeadImportResultDto(
    int TotalLinhas,
    int Importados,
    int Duplicados,
    int ComErro,
    IReadOnlyList<string> Erros);

public record AddNoteRequest(string Texto);

public record LeadTimelineItemDto(
    Guid Id,
    TipoEventoTimeline Tipo,
    string Titulo,
    string? Descricao,
    string? UsuarioNome,
    DateTimeOffset OcorridoEm);

/// <summary>Leads que passaram a ser do usuário desde a última verificação. <see cref="Agora"/> é o cursor da próxima.</summary>
public record NovosLeadsDto(DateTimeOffset Agora, IReadOnlyList<NovoLeadDto> Leads);

public record NovoLeadDto(Guid LeadId, string Nome, DateTimeOffset AtribuidoEm);
