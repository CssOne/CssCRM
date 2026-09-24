using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

public class CrmLead : CrmArchivableEntity
{
    public string NomeOuRazaoSocial { get; set; } = string.Empty;
    public TipoPessoa TipoPessoa { get; set; }

    /// <summary>CPF ou CNPJ normalizado (somente dígitos), usado para deduplicação e índice único.</summary>
    public string? DocumentoNormalizado { get; set; }

    /// <summary>Telefone normalizado (E.164 simplificado, somente dígitos com DDI/DDD).</summary>
    public string? TelefoneNormalizado { get; set; }
    public string? Telefone { get; set; }

    /// <summary>Segundo telefone — alguns leads migrados do Notion vieram com dois números colados
    /// no mesmo campo (sem separador); ver NomeTelefoneHeuristica.SepararTelefones.</summary>
    public string? Telefone2Normalizado { get; set; }
    public string? Telefone2 { get; set; }
    public string? WhatsApp { get; set; }

    /// <summary>E-mail normalizado (minúsculas, sem espaços), usado para deduplicação.</summary>
    public string? EmailNormalizado { get; set; }
    public string? Email { get; set; }

    public DateOnly? DataNascimento { get; set; }

    public string? Cidade { get; set; }
    public string? Estado { get; set; }
    public string? Regional { get; set; }

    public string? Origem { get; set; }
    public string? Campanha { get; set; }
    public string? ProdutoInteresse { get; set; }

    /// <summary>Placa do veículo informada logo na chegada do lead (antes de existir uma oportunidade/veículo formal).</summary>
    public string? Placa { get; set; }

    /// <summary>Se o lead já possui seguro para o veículo — captado na qualificação inicial (formulário/anúncio).</summary>
    public bool? TemSeguro { get; set; }

    /// <summary>Para que o veículo é usado (ex: particular, trabalho, aplicativo) — captado na qualificação inicial.</summary>
    public string? UtilidadeVeiculo { get; set; }

    /// <summary>
    /// True para leads cadastrados por uma pessoa (tela "+ Novo lead", importação de planilha,
    /// migração de dados antigos) — false para leads que chegaram sozinhos via integração
    /// automática (webhook do Meta Lead Ads, formulário público do site). Usado para manter os
    /// dois grupos sempre visíveis separadamente no quadro de leads, ainda que percorram as
    /// mesmas etapas do funil.
    /// </summary>
    public bool CriadoManualmente { get; set; } = true;

    /// <summary>Google Click ID — associa o lead ao clique que originou a conversão (Google Ads).</summary>
    public string? Gclid { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmTerm { get; set; }

    /// <summary>Identificadores do lead ad (Meta/Facebook Lead Ads) que originou este cadastro.</summary>
    public string? MetaClickId { get; set; }
    public string? MetaFormId { get; set; }
    public string? MetaLeadId { get; set; }

    /// <summary>Lead que indicou este cadastro (programa de indicação), quando aplicável.</summary>
    public Guid? IndicadoPorLeadId { get; set; }
    public CrmLead? IndicadoPorLead { get; set; }
    public string? TipoIndicacao { get; set; }

    /// <summary>
    /// Etapa do lead no quadro (kanban). Fica nula de propósito em leads novos — sem etapa marcada
    /// é como a vendedora enxerga "ninguém pegou ainda"; ela mesma arrasta pra uma etapa real
    /// (ex: "Em atendimento") quando começa a trabalhar o lead.
    /// </summary>
    public Guid? EtapaId { get; set; }
    public CrmLeadStage? Etapa { get; set; }

    /// <summary>Motivo da perda, obrigatório ao mover o lead para a etapa "Perdido".</summary>
    public Guid? MotivoPerdaId { get; set; }
    public CrmLossReason? MotivoPerda { get; set; }

    /// <summary>Explicação livre do consultor sobre a perda, complementar ao motivo pré-cadastrado (MotivoPerda).</summary>
    public string? MotivoPerdaObservacao { get; set; }

    /// <summary>Modelo do veículo que motivou mover o lead para a etapa "Não fazemos" (veículo fora do que a CSS Brasil atende).</summary>
    public string? VeiculoNaoAtendido { get; set; }

    /// <summary>
    /// Valor da adesão informado ao mover o lead para "Cotação" (obrigatório nessa etapa). Pré-preenche
    /// o "Pagamento de adesão" dos formulários de Oportunidade e de Venda concluída.
    /// </summary>
    public decimal? ValorAdesao { get; set; }

    /// <summary>
    /// Último "Status" do Notion aplicado a este lead pela sincronização. Serve para detectar quando
    /// o Status mudou lá — só então o lead troca de coluna, sem desfazer movimentos feitos no CRM
    /// enquanto o Status no Notion continua o mesmo.
    /// </summary>
    public string? NotionStatus { get; set; }

    /// <summary>
    /// Id da página (card) do Notion ligada a este lead. É a primeira chave usada pela sincronização
    /// para achar o lead — CPF/e-mail/telefone no Notion são digitados à mão e às vezes vêm inválidos
    /// ou trocados, o que fazia um card atualizar o lead de outro cliente.
    /// </summary>
    public string? NotionPageId { get; set; }

    public Guid? ResponsavelId { get; set; }

    /// <summary>
    /// Quando o lead passou para o responsável atual — preenchido sozinho ao salvar (ver
    /// ApplicationDbContext.AplicarAuditoria). É o que dispara a notificação de "novo lead" do consultor.
    /// </summary>
    public DateTimeOffset? ResponsavelAtribuidoEm { get; set; }
    public ApplicationUser? Responsavel { get; set; }

    public string? Observacoes { get; set; }

    public bool ConsentimentoContato { get; set; }
    public DateTimeOffset? ConsentimentoDataEm { get; set; }
    public string? ConsentimentoOrigem { get; set; }

    /// <summary>Data/hora do último contato efetivo (atividade concluída) — usado para "leads parados".</summary>
    public DateTimeOffset? UltimoContatoEm { get; set; }

    /// <summary>Data/hora do próximo contato agendado (atividade pendente mais próxima).</summary>
    public DateTimeOffset? ProximoContatoEm { get; set; }

    public ICollection<CrmOpportunity> Oportunidades { get; set; } = new List<CrmOpportunity>();
    public ICollection<CrmActivity> Atividades { get; set; } = new List<CrmActivity>();
    public ICollection<CrmNote> Notas { get; set; } = new List<CrmNote>();
    public ICollection<CrmLeadTag> LeadTags { get; set; } = new List<CrmLeadTag>();
    public ICollection<CrmLeadAssignmentHistory> HistoricoAtribuicoes { get; set; } = new List<CrmLeadAssignmentHistory>();
    public ICollection<CrmAttachment> Anexos { get; set; } = new List<CrmAttachment>();
}
