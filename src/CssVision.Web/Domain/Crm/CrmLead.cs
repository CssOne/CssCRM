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

    public StatusLead Status { get; set; } = StatusLead.Novo;

    public Guid? ResponsavelId { get; set; }
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
