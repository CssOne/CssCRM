namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Id nulo representa a coluna virtual "Sem etapa" (leads novos, ainda não trabalhados) — não existe como linha em CrmLeadStage.</summary>
public record LeadStageDto(Guid? Id, string Nome, int Ordem, string? Cor, bool Fechada, bool Ativa);

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

/// <summary>NovaEtapaId nulo move o lead de volta pra "Sem etapa" (desmarca).</summary>
public record ChangeLeadStageRequest(Guid? NovaEtapaId, uint RowVersion);

public record CreateLeadStageRequest(string Nome, int Ordem, string? Cor, bool Fechada);
