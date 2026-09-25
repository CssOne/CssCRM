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

    /// <summary>Explicação livre do consultor sobre a perda, complementar ao motivo pré-cadastrado (MotivoPerda).</summary>
    public string? MotivoPerdaObservacao { get; set; }

    public string? Concorrente { get; set; }
    public string? Observacoes { get; set; }

    /// <summary>Data em que o cliente aderiu ao plano (assinatura do contrato), distinta da data de fechamento da venda.</summary>
    public DateOnly? DataAdesao { get; set; }

    /// <summary>Data/hora em que o plano passou a estar ativo (ex: rastreador instalado e vistoriado).</summary>
    public DateTimeOffset? AtivoEm { get; set; }

    /// <summary>Precisão monetária: numeric(14,2) — ver ApplicationDbContext.</summary>
    public decimal? Mensalidade { get; set; }
    public decimal? MensalidadeComDesconto { get; set; }

    /// <summary>Mensalidade com cupom de desconto aplicado — distinta de MensalidadeComDesconto (desconto padrão, sem cupom).</summary>
    public decimal? MensalidadeComCupom { get; set; }
    public decimal? PagamentoAdesao { get; set; }

    /// <summary>Percentual de comissão ou desconto aplicado, conforme o produto. Precisão: numeric(5,2).</summary>
    public decimal? Porcentagem { get; set; }

    public bool TermoAdesaoAceito { get; set; }

    /// <summary>Indica se esta oportunidade é uma migração de um plano/contrato anterior.</summary>
    public bool Migracao { get; set; }

    /// <summary>CPF do cliente confirmado no fechamento da venda — snapshot histórico, pode divergir do Lead se corrigido depois.</summary>
    public string? Cpf { get; set; }
    public string? Estado { get; set; }

    /// <summary>Indica se esta venda teve origem em indicação de outro cliente.</summary>
    public bool? Indicacao { get; set; }
    public string? TipoIndicacao { get; set; }

    /// <summary>Valor pago/devido pela indicação. Precisão: numeric(14,2).</summary>
    public decimal? ValorIndicacao { get; set; }

    /// <summary>Precisão monetária: numeric(14,2) — ver ApplicationDbContext.</summary>
    public decimal? Total { get; set; }

    /// <summary>Caminho relativo (/uploads/...) do termo de adesão assinado, enviado ao concluir a venda.</summary>
    public string? TermoAdesaoArquivoUrl { get; set; }

    /// <summary>Caminho relativo (/uploads/...) do comprovante de pagamento da adesão, enviado ao concluir a venda.</summary>
    public string? PagamentoAdesaoArquivoUrl { get; set; }

    /// <summary>
    /// Quando a adesão não é paga no dia da venda: data combinada para o pagamento. Com ela, a venda
    /// pode ser concluída sem o comprovante, e nessa data o responsável recebe um lembrete
    /// (ver OpportunityService.ListarLembretesAdesaoAsync) até o comprovante ser anexado.
    /// </summary>
    public DateOnly? DataPagamentoAdesaoPrevista { get; set; }

    /// <summary>
    /// Card de venda do Notion que originou esta oportunidade — um cliente com várias vendas (ex.: dois
    /// veículos) tem um card e uma oportunidade para cada. Nulo para vendas feitas só no CRM.
    /// </summary>
    public string? NotionPageId { get; set; }

    /// <summary>Caminho relativo (/uploads/...) do comprovante de indicação, quando a venda teve origem em indicação.</summary>
    public string? ComprovanteIndicacaoArquivoUrl { get; set; }

    /// <summary>Caminho relativo (/uploads/...) do comprovante de vistoria do veículo.</summary>
    public string? ComprovanteVistoriaArquivoUrl { get; set; }

    public CrmVeiculo? Veiculo { get; set; }

    /// <summary>Quando o evento de conversão offline (CAPI) foi enviado ao Facebook por esta oportunidade ter sido ganha. Nulo = ainda não enviado.</summary>
    public DateTimeOffset? ConversaoOfflineEnviadaEm { get; set; }

    public ICollection<CrmStageHistory> HistoricoEtapas { get; set; } = new List<CrmStageHistory>();
    public ICollection<CrmActivity> Atividades { get; set; } = new List<CrmActivity>();
}
