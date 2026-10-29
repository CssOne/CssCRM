using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>Como o consultor é contratado: PJ (recebe a adesão inteira e ganha bônus por placa OU recorrência, e tem custos fixos) ou CLT (salário mais uma porcentagem da adesão, sem custos fixos).</summary>
public enum TipoContrato
{
    Pj = 1,
    Clt = 2,
}

/// <summary>
/// Como o consultor PJ é remunerado: ou por bônus por placa (um valor por venda) ou por recorrência (um valor por mês) — nunca os dois.
/// </summary>
public enum ModeloRemuneracaoPj
{
    BonusPorPlaca = 1,
    Recorrencia = 2,
}

/// <summary>Transporte fixo do consultor PJ: combustível é cobrado por semana; Uber e ônibus, por dia útil.</summary>
public enum ModalidadeTransporte
{
    Nenhum = 0,
    Combustivel = 1,
    Uber = 2,
    Onibus = 3,
}

/// <summary>
/// Valores definidos pela gestão para cada consultor. Todos têm histórico mês a mês (<see cref="CrmFinanceiroValor"/>); a
/// <see cref="Recorrencia"/> vale só no mês em que foi lançada, os demais valem do mês de vigência em diante, até o próximo lançamento.
/// </summary>
public enum ParametroFinanceiro
{
    /// <summary>PJ remunerado por bônus: valor pago por placa (venda) — varia de consultor para consultor e de mês para mês (ex.: R$ 100 num mês, R$ 200 em outro).</summary>
    BonusPorPlaca = 1,

    /// <summary>PJ remunerado por recorrência: valor do mês (varia todo mês; vale só no mês lançado).</summary>
    Recorrencia = 2,

    /// <summary>CLT: salário mensal.</summary>
    Salario = 3,

    /// <summary>CLT: fração da adesão que o consultor recebe (0,30 = 30%); ele não ganha a adesão inteira.</summary>
    PercentualAdesao = 4,

    /// <summary>PJ: coworking mensal (padrão R$ 100).</summary>
    Coworking = 5,

    /// <summary>PJ: imposto MEI mensal (padrão R$ 89).</summary>
    ImpostoMei = 6,

    /// <summary>PJ: valor do transporte por semana (combustível) ou por dia útil (Uber/ônibus), conforme a modalidade.</summary>
    Transporte = 7,

    /// <summary>PJ: gestão de tráfego pago, mensal. Por padrão só passa a ser cobrada alguns meses depois da entrada (a carência).</summary>
    TrafegoPago = 9,

    /// <summary>PJ: custo de leads, mensal. Também começa só depois da carência.</summary>
    CustoLead = 10,

    /// <summary>CLT: valor da alimentação pago por mês (benefício somado à renda do mês).</summary>
    Alimentacao = 11,

    /// <summary>CLT: valor da passagem (transporte) pago por mês (benefício somado à renda do mês).</summary>
    Passagem = 12,
}

/// <summary>Perfil financeiro do consultor: tipo de contrato e modalidade de transporte. Definido pela gestão (nunca pelo próprio consultor).</summary>
public class CrmFinanceiroPerfil : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public TipoContrato? Tipo { get; set; }

    /// <summary>PJ: bônus por placa ou recorrência (só um dos dois vale). Nulo para CLT.</summary>
    public ModeloRemuneracaoPj? ModeloPj { get; set; }

    public ModalidadeTransporte Transporte { get; set; }

    /// <summary>Primeiro dia do mês a partir do qual valem o salário e os custos fixos (nada é cobrado nem pago antes disso).</summary>
    public DateOnly? InicioContrato { get; set; }
}

/// <summary>Um valor definido para um consultor, com vigência a partir de um mês. O conjunto de lançamentos é o histórico de quanto ele recebia e vai receber.</summary>
public class CrmFinanceiroValor : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public ParametroFinanceiro Parametro { get; set; }

    /// <summary>Primeiro dia do mês de vigência (ex.: 2026-10-01).</summary>
    public DateOnly VigenteDesde { get; set; }

    public decimal Valor { get; set; }
    public string? Observacao { get; set; }

    /// <summary>Quem definiu o valor (administrador, gestor ou financeiro).</summary>
    public Guid DefinidoPorId { get; set; }
}

/// <summary>
/// Valor e carência padrão de um custo fixo para TODOS os consultores PJ (ex.: coworking, imposto MEI, tráfego pago, custo de lead). Cada consultor
/// pode ter o próprio valor (<see cref="CrmFinanceiroValor"/>) e o próprio prazo (<see cref="CrmFinanceiroPrazo"/>) por cima deste padrão.
/// </summary>
public class CrmFinanceiroPadrao : CrmEntityBase
{
    public ParametroFinanceiro Parametro { get; set; }

    /// <summary>Valor mensal padrão. Nulo = usa o padrão do sistema.</summary>
    public decimal? Valor { get; set; }

    /// <summary>Meses depois da entrada do consultor até começar a cobrar. Nulo = usa o padrão do sistema.</summary>
    public int? CarenciaMeses { get; set; }

    public Guid DefinidoPorId { get; set; }
}

/// <summary>
/// Prazo do consultor para começar a pagar um custo fixo (a carência, em meses a contar do início do contrato) quando difere do padrão, e a
/// confirmação do aviso de início de cobrança ("deixar começar").
/// </summary>
public class CrmFinanceiroPrazo : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public ParametroFinanceiro Parametro { get; set; }

    /// <summary>Carência própria deste consultor. Nulo = segue o padrão.</summary>
    public int? CarenciaMeses { get; set; }

    /// <summary>Mês de início de cobrança cujo aviso a gestão já confirmou (sumiu do alerta). Muda sozinho se o prazo for estendido.</summary>
    public DateOnly? AlertaConfirmadoPara { get; set; }

    public Guid DefinidoPorId { get; set; }
}

/// <summary>O que a CSS cobra do consultor PJ e que o Setor Financeiro acompanha mês a mês (pago ou em aberto).</summary>
public enum ItemCobranca
{
    /// <summary>Coworking: responsabilidade da CSS, cobrado do consultor PJ todo mês.</summary>
    Coworking = 5,

    CustoLead = 2,
    TrafegoPago = 3,

    /// <summary>Custo adicional lançado pelo financeiro para o consultor (notebook, celular...), pago em parcelas: veja <see cref="CrmCustoConsultor"/>.</summary>
    Adicional = 4,
}

/// <summary>
/// Situação de pagamento de uma cobrança do consultor em um mês. O valor devido vem do contrato; aqui fica só se foi pago, quando, e quem registrou.
/// Sem registro = em aberto.
/// </summary>
public class CrmCobrancaPagamento : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public ItemCobranca Item { get; set; }

    /// <summary>Custo adicional a que a parcela pertence (só quando <see cref="Item"/> é <see cref="ItemCobranca.Adicional"/>).</summary>
    public Guid? CustoId { get; set; }

    /// <summary>Primeiro dia do mês da cobrança (competência).</summary>
    public DateOnly Competencia { get; set; }

    /// <summary>Pagamento CONFIRMADO pelo financeiro.</summary>
    public bool Pago { get; set; }
    public DateOnly? PagoEm { get; set; }
    public string? Observacao { get; set; }
    public Guid RegistradoPorId { get; set; }

    /// <summary>Quando o consultor avisou que pagou (aguarda o financeiro conferir e confirmar).</summary>
    public DateTimeOffset? InformadoEm { get; set; }

    /// <summary>Data em que o consultor diz ter pago.</summary>
    public DateOnly? DataInformada { get; set; }

    /// <summary>Observação do consultor ao informar o pagamento.</summary>
    public string? ObservacaoConsultor { get; set; }

    /// <summary>Comprovante de pagamento anexado pelo consultor.</summary>
    public string? ComprovanteUrl { get; set; }

    /// <summary>"Pagar depois": dia em que o consultor quer ser lembrado de pagar.</summary>
    public DateOnly? LembreteEm { get; set; }

    /// <summary>Quando o lembrete foi enviado (um por pedido).</summary>
    public DateTimeOffset? LembreteEnviadoEm { get; set; }

    /// <summary>O financeiro não confirmou o pagamento informado: quando e por quê.</summary>
    public DateTimeOffset? RecusadoEm { get; set; }
    public string? MotivoRecusa { get; set; }

    /// <summary>Valor combinado para esta cobrança no lugar do calculado pelo contrato (nulo = vale o do contrato).</summary>
    public decimal? ValorAjustado { get; set; }

    /// <summary>Cobrança removida deste mês: deixa de ser devida (não conta em aberto nem atrasada). Dá para restaurar.</summary>
    public bool Dispensada { get; set; }
}

public enum TipoCustoConsultor
{
    Notebook = 1,
    Celular = 2,
    Equipamento = 3,
    Outro = 4,
}

/// <summary>
/// Custo que o financeiro lança para um consultor e que ele paga em parcelas mensais (ex.: notebook comprado com a empresa). Cada parcela vira uma
/// cobrança acompanhada no Setor Financeiro (paga ou em aberto) e um custo no Controle financeiro dele.
/// </summary>
public class CrmCustoConsultor : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public TipoCustoConsultor Tipo { get; set; }

    /// <summary>Detalhe do custo (obrigatório no tipo "Outro").</summary>
    public string? Descricao { get; set; }

    public decimal ValorTotal { get; set; }
    public int Parcelas { get; set; }

    /// <summary>Data de vencimento da 1ª parcela; as demais vencem no mesmo dia dos meses seguintes (no último dia, se o mês for mais curto).</summary>
    public DateOnly PrimeiraParcela { get; set; }

    public Guid CriadoPorId { get; set; }
}

public enum MomentoAvisoCusto
{
    /// <summary>Poucos dias antes do vencimento da parcela.</summary>
    Antecedencia = 1,

    /// <summary>No dia do vencimento da parcela.</summary>
    Vencimento = 2,
}

/// <summary>Registro de que o aviso de uma parcela (antecedência ou vencimento) já foi enviado ao consultor — evita mandar o mesmo aviso a cada ciclo.</summary>
public class CrmCustoAviso : CrmEntityBase
{
    public Guid CustoId { get; set; }

    /// <summary>Primeiro dia do mês da parcela.</summary>
    public DateOnly Competencia { get; set; }

    public MomentoAvisoCusto Momento { get; set; }
}
