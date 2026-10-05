namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Id nulo representa a coluna virtual "Sem etapa" (leads novos, ainda não trabalhados) — não existe como linha em CrmLeadStage.</summary>
public record LeadStageDto(Guid? Id, string Nome, int Ordem, string? Cor, bool Fechada, bool Ativa);

public record LeadKanbanCardDto(
    Guid LeadId,
    string NomeOuRazaoSocial,
    string? Telefone,
    string? Telefone2,
    string? Email,
    string? Estado,
    string? Origem,
    string? Campanha,
    string? Placa,
    bool? TemSeguro,
    string? UtilidadeVeiculo,
    string? TipoIndicacao,
    bool Migracao,
    bool? Indicacao,
    bool CriadoManualmente,
    Guid? ResponsavelId,
    string? ResponsavelNome,
    IReadOnlyList<string> Tags,
    DateTimeOffset CriadoEm,
    DateTimeOffset? UltimoContatoEm,
    bool SemContato,
    bool Arquivado,
    uint RowVersion,
    /// <summary>"O que?" — produto de interesse (AGV, AGV ELÉTRICO, AGV TRUCK...), visível para todos.</summary>
    string? OQue = null,
    decimal? ValorAdesao = null,
    /// <summary>Foto do consultor responsável (mostrada em círculo no rodapé do cartão).</summary>
    string? ResponsavelFotoUrl = null,
    /// <summary>Card de outro veículo de um cliente que já tem card (ver CrmLead.VeiculoAdicionalDeLeadId).</summary>
    bool VeiculoAdicional = false,
    /// <summary>Motivo da perda (cartões em "Perdido") e a explicação do consultor.</summary>
    string? MotivoPerda = null,
    string? MotivoPerdaObservacao = null);

/// <summary>Opção do filtro por motivo da coluna "Perdido" (Id vazio = sem motivo informado), com a quantidade.</summary>
public record LeadKanbanMotivoPerdaDto(Guid Id, string Descricao, int Quantidade);

/// <summary>
/// Coluna do quadro. <see cref="Cartoes"/> traz só a primeira página (os mais recentes);
/// <see cref="Total"/> é a quantidade de leads da coluna inteira — o restante vem por "Ver mais".
/// </summary>
public record LeadKanbanColumnDto(
    LeadStageDto Etapa, IReadOnlyList<LeadKanbanCardDto> Cartoes, int Total,
    /// <summary>Só na coluna "Perdido": motivos dos cartões dela com os filtros de cima (para o filtro da coluna).</summary>
    IReadOnlyList<LeadKanbanMotivoPerdaDto>? MotivosPerda = null);

public record LeadKanbanBoardDto(IReadOnlyList<LeadKanbanColumnDto> Colunas);

public record LeadKanbanFilterRequest
{
    public string? Busca { get; init; }

    // Filtros de múltipla escolha: ?origem=A&origem=B traz leads de A OU de B.
    public Guid[]? ResponsavelId { get; init; }
    public string[]? Origem { get; init; }
    public string[]? Regional { get; init; }

    /// <summary>Grupos (CrmGrupo) do consultor responsável: leads de quem está em algum destes grupos.</summary>
    public Guid[]? GrupoId { get; init; }
    public bool IncluirArquivados { get; init; }

    /// <summary>
    /// Opcional — quando omitido, o quadro mostra leads cadastrados manualmente e automaticamente
    /// juntos (ver etiquetas "Lead"/"Indicação" no rodapé do cartão). Filtro mantido para uso futuro.
    /// </summary>
    public bool? CriadoManualmente { get; init; }

    /// <summary>"Migração" (leads da migração histórica do Notion), "Indicação" ou "Lead" (TipoIndicacao) —
    /// ver LeadKanbanService.ObterBoardAsync.</summary>
    public string[]? Categoria { get; init; }

    /// <summary>Tipo de indicação do lead (Lead, Indicação, Pessoal, Contemplando Sonhos...), sem diferenciar maiúsculas.</summary>
    public string[]? TipoIndicacao { get; init; }

    /// <summary>
    /// Por onde o lead entrou no CRM: "TrafegoPago" (direto dos anúncios — Meta Lead Ads e formulário
    /// do site) ou "Notion" (migração histórica e sincronização). Ver LeadKanbanService.
    /// </summary>
    public string[]? Fonte { get; init; }

    public DateOnly? DataChegadaInicio { get; init; }
    public DateOnly? DataChegadaFim { get; init; }

    /// <summary>Filtra pela data efetiva de fechamento (venda) de alguma oportunidade do lead.</summary>
    public DateOnly? DataVendaInicio { get; init; }
    public DateOnly? DataVendaFim { get; init; }

    /// <summary>Quantos cartões cada coluna traz na carga do quadro (os mais recentes). Máximo 200.</summary>
    public int CartoesPorColuna { get; init; } = 30;

    /// <summary>
    /// Filtro da coluna "Perdido" por motivo da perda (Guid.Empty = sem motivo informado). Vale só para
    /// essa coluna — as outras não têm motivo — e soma-se aos filtros de cima.
    /// </summary>
    public Guid[]? MotivoPerdaId { get; init; }
}

/// <summary>"Ver mais" de uma coluna: os mesmos filtros do quadro + a coluna (EtapaId nulo = "Sem etapa") e a página.</summary>
public record LeadKanbanColunaRequest : LeadKanbanFilterRequest
{
    public Guid? EtapaId { get; init; }
    public int Pular { get; init; }
    public int Quantidade { get; init; } = 30;
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
    string? VeiculoNaoAtendido = null,
    /// <summary>Valor da adesão — obrigatório ao mover para "Cotação" (ver LeadService.MudarEtapaAsync).</summary>
    decimal? ValorAdesao = null);

public record CreateLeadStageRequest(string Nome, int Ordem, string? Cor, bool Fechada);
