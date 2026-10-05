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
    bool Arquivado,
    /// <summary>Dados da venda do lead (colunas dos Relatórios do Notion); nulo se o lead não tem oportunidade.</summary>
    LeadVendaResumoDto? Venda = null);

/// <summary>Venda do lead: as mesmas colunas da tela "RELATÓRIOS CSS BRASIL" do Notion.</summary>
public record LeadVendaResumoDto(
    decimal? Adesao,
    decimal? Fipe,
    decimal? Mensalidade,
    decimal? MensalidadeComDesconto,
    decimal? Porcentagem,
    decimal? Rastreador,
    decimal? Indicacao,
    decimal? Vistoria,
    decimal? Total,
    DateTimeOffset? DataVenda);

/// <summary>
/// Rodapé da lista de leads (como a barra de somas da tela do Notion): quantos leads batem com os filtros e a soma
/// das colunas de dinheiro das vendas deles — todas as páginas, não só a que aparece.
/// </summary>
public record LeadTotaisDto(
    int Contagem,
    decimal Adesao,
    decimal Fipe,
    decimal Mensalidade,
    decimal MensalidadeComDesconto,
    decimal Rastreador,
    decimal Indicacao,
    decimal Vistoria,
    decimal Total,
    /// <summary>Média da porcentagem (coluna "%") das vendas dos leads filtrados.</summary>
    decimal MediaPorcentagem = 0m);

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
    /// <summary>Período da venda (data efetiva de fechamento de alguma oportunidade do lead).</summary>
    public DateOnly? DataVendaInicio { get; init; }
    public DateOnly? DataVendaFim { get; init; }

    // Filtros de múltipla escolha (?responsavelIds=a&responsavelIds=b traz de a OU b).
    public Guid[]? ResponsavelIds { get; init; }
    /// <summary>Etapas do quadro de leads; Guid.Empty = "Sem etapa".</summary>
    public Guid[]? LeadEtapaIds { get; init; }
    public string[]? Origens { get; init; }
    public string[]? Regionais { get; init; }
    /// <summary>Grupos (CrmGrupo) do consultor responsável.</summary>
    public Guid[]? GrupoIds { get; init; }
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
    DateOnly? DataPagamentoAdesaoPrevista = null,
    /// <summary>Consultor dono da oportunidade — consultores só excluem as próprias.</summary>
    Guid? ResponsavelId = null);

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
    string? UtmCampaign = null,
    /// <summary>Modelo do veículo que não atendemos (coluna "Não fazemos"); vazio limpa, nulo não mexe.</summary>
    string? VeiculoNaoAtendido = null);

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
    string? UtmCampaign = null,
    string? VeiculoNaoAtendido = null);

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
