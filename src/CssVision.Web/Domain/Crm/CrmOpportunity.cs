using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

public class CrmOpportunity : CrmArchivableEntity
{
    public Guid LeadId { get; set; }
    public CrmLead Lead { get; set; } = null!;

    public string Titulo { get; set; } = string.Empty;

    public Guid ResponsavelId { get; set; }
    public ApplicationUser Responsavel { get; set; } = null!;

    public Guid EtapaId { get; set; }
    public CrmPipelineStage Etapa { get; set; } = null!;

    /// <summary>Data/hora em que a oportunidade entrou na etapa atual — base para "tempo na etapa".</summary>
    public DateTimeOffset EtapaDesde { get; set; } = DateTimeOffset.UtcNow;

    public string? ProdutoOuServico { get; set; }

    /// <summary>Precisão monetária: numeric(14,2) — ver ApplicationDbContext.</summary>
    public decimal ValorEstimado { get; set; }

    public int? ProbabilidadeFechamento { get; set; }

    public DateOnly? DataPrevistaFechamento { get; set; }

    public decimal? ValorFinal { get; set; }
    public DateTimeOffset? DataEfetivaFechamento { get; set; }

    public Guid? MotivoPerdaId { get; set; }
    public CrmLossReason? MotivoPerda { get; set; }

    public string? Concorrente { get; set; }
    public string? Observacoes { get; set; }

    /// <summary>Data em que o cliente aderiu ao plano (assinatura do contrato), distinta da data de fechamento da venda.</summary>
    public DateOnly? DataAdesao { get; set; }

    /// <summary>Data/hora em que o plano passou a estar ativo (ex: rastreador instalado e vistoriado).</summary>
    public DateTimeOffset? AtivoEm { get; set; }

    /// <summary>Precisão monetária: numeric(14,2) — ver ApplicationDbContext.</summary>
    public decimal? Mensalidade { get; set; }
    public decimal? MensalidadeComDesconto { get; set; }
    public decimal? PagamentoAdesao { get; set; }

    /// <summary>Percentual de comissão ou desconto aplicado, conforme o produto. Precisão: numeric(5,2).</summary>
    public decimal? Porcentagem { get; set; }

    public bool TermoAdesaoAceito { get; set; }

    /// <summary>Indica se esta oportunidade é uma migração de um plano/contrato anterior.</summary>
    public bool Migracao { get; set; }

    public CrmVeiculo? Veiculo { get; set; }

    /// <summary>Quando o evento de conversão offline (CAPI) foi enviado ao Facebook por esta oportunidade ter sido ganha. Nulo = ainda não enviado.</summary>
    public DateTimeOffset? ConversaoOfflineEnviadaEm { get; set; }

    public ICollection<CrmStageHistory> HistoricoEtapas { get; set; } = new List<CrmStageHistory>();
    public ICollection<CrmActivity> Atividades { get; set; } = new List<CrmActivity>();
}
