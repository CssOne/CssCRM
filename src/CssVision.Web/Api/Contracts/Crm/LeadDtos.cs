using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

public record LeadListItemDto(
    Guid Id,
    string NomeOuRazaoSocial,
    TipoPessoa TipoPessoa,
    string? DocumentoMascarado,
    string? Telefone,
    string? Email,
    string? Cidade,
    string? Estado,
    string? Regional,
    string? Origem,
    StatusLead Status,
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
    public StatusLead? Status { get; init; }
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
    string? WhatsApp,
    string? Email,
    DateOnly? DataNascimento,
    string? Cidade,
    string? Estado,
    string? Regional,
    string? Origem,
    string? Campanha,
    string? ProdutoInteresse,
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
    StatusLead Status,
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
    bool Arquivado);

public record LeadOpportunitySummaryDto(
    Guid Id,
    string Titulo,
    string EtapaNome,
    decimal ValorEstimado,
    DateOnly? DataPrevistaFechamento,
    bool Ativa);

public record LeadCreateRequest(
    string NomeOuRazaoSocial,
    TipoPessoa TipoPessoa,
    string? Documento,
    string? Telefone,
    string? WhatsApp,
    string? Email,
    DateOnly? DataNascimento,
    string? Cidade,
    string? Estado,
    string? Regional,
    string? Origem,
    string? Campanha,
    string? ProdutoInteresse,
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
    List<string>? Tags,
    string? Observacoes,
    bool ConsentimentoContato,
    string? ConsentimentoOrigem,
    bool IgnorarDuplicidade = false);

public record LeadUpdateRequest(
    string NomeOuRazaoSocial,
    TipoPessoa TipoPessoa,
    string? Documento,
    string? Telefone,
    string? WhatsApp,
    string? Email,
    DateOnly? DataNascimento,
    string? Cidade,
    string? Estado,
    string? Regional,
    string? Origem,
    string? Campanha,
    string? ProdutoInteresse,
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
    uint RowVersion);

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
