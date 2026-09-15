namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Id nulo representa a coluna virtual "Sem etapa" (leads novos, ainda não trabalhados) — não existe como linha em CrmLeadStage.</summary>
public record LeadStageDto(Guid? Id, string Nome, int Ordem, string? Cor, bool Fechada, bool Ativa);

public record LeadKanbanCardDto(
    Guid LeadId,
    string NomeOuRazaoSocial,
    string? Telefone,
    string? Email,
    string? Estado,
    string? Origem,
    string? Campanha,
    string? Placa,
    bool? TemSeguro,
    string? UtilidadeVeiculo,
    Guid? ResponsavelId,
    string? ResponsavelNome,
    IReadOnlyList<string> Tags,
    DateTimeOffset CriadoEm,
    DateTimeOffset? UltimoContatoEm,
    bool SemContato,
    bool Arquivado,
    uint RowVersion);

public record LeadKanbanColumnDto(LeadStageDto Etapa, IReadOnlyList<LeadKanbanCardDto> Cartoes);

public record LeadKanbanBoardDto(IReadOnlyList<LeadKanbanColumnDto> Colunas);

public record LeadKanbanFilterRequest
{
    public string? Busca { get; init; }
    public Guid? ResponsavelId { get; init; }
    public string? Origem { get; init; }
    public string? Regional { get; init; }
    public bool IncluirArquivados { get; init; }

    /// <summary>
    /// Separa o quadro em dois grupos que nunca aparecem juntos: true (padrão) mostra só leads
    /// cadastrados por uma pessoa, false mostra só os que chegaram automaticamente (Meta Ads,
    /// formulário do site).
    /// </summary>
    public bool CriadoManualmente { get; init; } = true;
}

/// <summary>NovaEtapaId nulo move o lead de volta pra "Sem etapa" (desmarca). MotivoPerdaId é obrigatório
/// quando a nova etapa é "Perdido" (ver LeadService.MudarEtapaAsync). MotivoPerdaObservacao é a
/// explicação livre do consultor, opcional, complementar ao motivo pré-cadastrado. VeiculoNaoAtendido
/// é obrigatório quando a nova etapa é "Não fazemos".</summary>
public record ChangeLeadStageRequest(
    Guid? NovaEtapaId,
    uint RowVersion,
    Guid? MotivoPerdaId = null,
    string? MotivoPerdaObservacao = null,
    string? VeiculoNaoAtendido = null);

public record CreateLeadStageRequest(string Nome, int Ordem, string? Cor, bool Fechada);
