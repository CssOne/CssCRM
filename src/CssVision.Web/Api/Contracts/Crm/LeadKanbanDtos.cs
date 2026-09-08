namespace CssVision.Web.Api.Contracts.Crm;

public record LeadStageDto(Guid Id, string Nome, int Ordem, string? Cor, bool Fechada, bool Ativa);

public record LeadKanbanCardDto(
    Guid LeadId,
    string NomeOuRazaoSocial,
    string? Telefone,
    string? Email,
    string? Origem,
    string? Campanha,
    Guid? ResponsavelId,
    string? ResponsavelNome,
    IReadOnlyList<string> Tags,
    DateTimeOffset CriadoEm,
    DateTimeOffset? UltimoContatoEm,
    bool SemContato,
    uint RowVersion);

public record LeadKanbanColumnDto(LeadStageDto Etapa, IReadOnlyList<LeadKanbanCardDto> Cartoes);

public record LeadKanbanBoardDto(IReadOnlyList<LeadKanbanColumnDto> Colunas);

public record LeadKanbanFilterRequest
{
    public Guid? ResponsavelId { get; init; }
    public string? Origem { get; init; }
    public string? Regional { get; init; }
}

public record ChangeLeadStageRequest(Guid NovaEtapaId, uint RowVersion);

public record CreateLeadStageRequest(string Nome, int Ordem, string? Cor, bool Fechada);
