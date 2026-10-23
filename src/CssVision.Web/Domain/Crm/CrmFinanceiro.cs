using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>Venda lançada pelo consultor no Controle financeiro (adesão + bônus/recorrência + premiações).</summary>
public class CrmFinanceiroVenda : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    /// <summary>
    /// Venda do quadro de leads (oportunidade ganha) a que este lançamento se refere. Cliente, data e adesão vêm sempre do card;
    /// aqui ficam só os complementos do consultor (bônus/recorrência, premiações, status e previsões de recebimento).
    /// </summary>
    public Guid? OportunidadeId { get; set; }

    public DateOnly DataVenda { get; set; }
    public string Cliente { get; set; } = "";
    public string Produto { get; set; } = "Adesão";

    public decimal ValorAdesao { get; set; }
    public decimal ValorBonus { get; set; }
    public decimal Premiacoes { get; set; }

    public DateOnly? DataPrevistaAdesao { get; set; }
    /// <summary>Pendente, Recebido ou Cancelado (atraso é calculado pela data prevista).</summary>
    public string StatusAdesao { get; set; } = FinanceiroStatus.Pendente;

    public DateOnly? DataPrevistaBonus { get; set; }

    /// <summary>
    /// Bônus do contrato: o valor que o consultor realmente recebeu, informado por ele quando o mês do pagamento chega (o bônus é só uma previsão: sobre o
    /// primeiro boleto pago do associado, que pode não ser pago). Nulo = ainda não informado.
    /// </summary>
    public decimal? BonusRecebido { get; set; }

    public string StatusBonus { get; set; } = FinanceiroStatus.Pendente;

    public string? Observacoes { get; set; }
}

/// <summary>Custo operacional do consultor (rastreador, leads, impostos, retiradas...).</summary>
public class CrmFinanceiroCusto : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public DateOnly Data { get; set; }
    public string Categoria { get; set; } = "Outros";
    public string? Descricao { get; set; }
    public decimal Valor { get; set; }

    public DateOnly? Vencimento { get; set; }
    /// <summary>APagar, Pago ou Cancelado.</summary>
    public string Status { get; set; } = FinanceiroStatus.APagar;
    public DateOnly? DataPagamento { get; set; }
    public bool ComprovanteSalvo { get; set; }
}

/// <summary>Meta mensal do consultor para o Controle financeiro.</summary>
public class CrmFinanceiroMeta : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public int Ano { get; set; }
    public int Mes { get; set; }
    public decimal MetaAdesao { get; set; }
    public decimal MetaBonus { get; set; }
    public decimal MetaRecorrencia { get; set; }
    public int MetaVendas { get; set; }
    public string? Observacoes { get; set; }
}

/// <summary>Configurações anuais do consultor: % de reserva para investimentos e caixa inicial.</summary>
public class CrmFinanceiroConfig : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public int Ano { get; set; }
    /// <summary>Fração (0,10 = 10%).</summary>
    public decimal PercentualReserva { get; set; } = 0.10m;
    public decimal CaixaInicial { get; set; }
}

public static class FinanceiroStatus
{
    public const string Pendente = "Pendente";
    public const string Recebido = "Recebido";
    public const string Cancelado = "Cancelado";
    public const string APagar = "APagar";
    public const string Pago = "Pago";

    public static readonly string[] Recebimento = [Pendente, Recebido, Cancelado];
    public static readonly string[] Pagamento = [APagar, Pago, Cancelado];
}
