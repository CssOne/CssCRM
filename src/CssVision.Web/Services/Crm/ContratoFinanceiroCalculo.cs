using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Regras do contrato do consultor, em funções puras (sem banco): qual valor vale em cada mês, a renda fixa (salário/recorrência) e os
/// custos fixos do PJ. PJ recebe a adesão inteira, bônus por placa e recorrência, e tem custos fixos; CLT recebe salário mais uma
/// porcentagem da adesão e não tem custos fixos.
/// </summary>
public static class ContratoFinanceiroCalculo
{
    public const decimal CoworkingPadrao = 100m;
    public const decimal ImpostoMeiPadrao = 89m;

    /// <summary>Meses depois da entrada em que tráfego pago e custo de lead passam a ser cobrados, quando a gestão não define outro prazo.</summary>
    public const int CarenciaPadraoMeses = 4;

    /// <summary>Bônus por placa e recorrência são pagos alguns meses depois do mês em que valem (venda de janeiro = bônus pago em março).</summary>
    public const int AtrasoDePagamentoMeses = 2;

    /// <summary>Primeiro dia do mês em que o bônus ou a recorrência do mês informado é efetivamente pago.</summary>
    public static DateOnly MesDoPagamento(DateOnly mes) => PrimeiroDia(mes).AddMonths(AtrasoDePagamentoMeses);

    /// <summary>Quantos dias antes do início da cobrança o aviso começa a aparecer.</summary>
    public const int DiasDeAntecedenciaDoAviso = 31;

    /// <summary>
    /// Custos fixos do PJ cobrados pela CSS, com valor mensal e carência definíveis por consultor (e padrão para todos). O imposto MEI não entra: é
    /// responsabilidade do consultor e só aparece pré-preenchido (R$ 89,00) no Controle financeiro dele.
    /// </summary>
    public static readonly ParametroFinanceiro[] CustosFixosPj =
        [ParametroFinanceiro.Coworking, ParametroFinanceiro.TrafegoPago, ParametroFinanceiro.CustoLead];

    public static string Rotulo(ParametroFinanceiro p) => p switch
    {
        ParametroFinanceiro.Coworking => "Coworking",
        ParametroFinanceiro.ImpostoMei => "Imposto MEI",
        ParametroFinanceiro.TrafegoPago => "Tráfego pago",
        ParametroFinanceiro.CustoLead => "Custo por lead",
        ParametroFinanceiro.Transporte => "Transporte",
        _ => p.ToString(),
    };

    /// <summary>Valor do sistema (sem padrão da gestão nem valor do consultor) de cada custo fixo.</summary>
    public static decimal ValorDoSistema(ParametroFinanceiro p) => p switch
    {
        ParametroFinanceiro.Coworking => CoworkingPadrao,
        ParametroFinanceiro.ImpostoMei => ImpostoMeiPadrao,
        _ => 0m,
    };

    /// <summary>Carência do sistema: só tráfego pago e custo de lead esperam alguns meses; os demais custos começam junto com o contrato.</summary>
    public static int CarenciaDoSistema(ParametroFinanceiro p) => p is ParametroFinanceiro.TrafegoPago or ParametroFinanceiro.CustoLead ? CarenciaPadraoMeses : 0;

    /// <summary>
    /// O que vale para um consultor além do histórico de valores dele: o padrão da gestão para todos os PJ (valor e carência) e o prazo próprio.
    /// Ordem de precedência: consultor, depois padrão da gestão, depois padrão do sistema.
    /// </summary>
    public sealed class Regras(
        IReadOnlyDictionary<ParametroFinanceiro, CrmFinanceiroPadrao>? padroes = null,
        IReadOnlyDictionary<ParametroFinanceiro, CrmFinanceiroPrazo>? prazos = null,
        IReadOnlyDictionary<DateOnly, int>? leadsPorMes = null,
        IReadOnlyDictionary<ParametroFinanceiro, DateOnly>? liberadoEm = null)
    {
        public static readonly Regras Nenhuma = new();

        /// <summary>
        /// Consultor sem contrato definido: o custo só passa a valer a partir do mês em que a gestão definiu o padrão dele (para não cobrar meses antigos).
        /// Sem esse limite (consultor com contrato), vale desde o fim da carência.
        /// </summary>
        public bool Liberado(ParametroFinanceiro p, DateOnly mes) => liberadoEm is null || !liberadoEm.TryGetValue(p, out var desde) || mes >= desde;

        /// <summary>Leads de tráfego pago (e do Notion) que o consultor pegou no mês: base do custo real do lead.</summary>
        public int LeadsNoMes(DateOnly mes) => leadsPorMes is not null && leadsPorMes.TryGetValue(PrimeiroDia(mes), out var n) ? n : 0;

        public decimal ValorPadrao(ParametroFinanceiro p) =>
            padroes is not null && padroes.TryGetValue(p, out var d) && d.Valor is { } v ? v : ValorDoSistema(p);

        public int CarenciaPadrao(ParametroFinanceiro p) =>
            padroes is not null && padroes.TryGetValue(p, out var d) && d.CarenciaMeses is { } c ? c : CarenciaDoSistema(p);

        public bool CarenciaPropria(ParametroFinanceiro p) => prazos is not null && prazos.TryGetValue(p, out var z) && z.CarenciaMeses is not null;

        public int Carencia(ParametroFinanceiro p) =>
            prazos is not null && prazos.TryGetValue(p, out var z) && z.CarenciaMeses is { } c ? c : CarenciaPadrao(p);

        public DateOnly? AlertaConfirmadoPara(ParametroFinanceiro p) => prazos is not null && prazos.TryGetValue(p, out var z) ? z.AlertaConfirmadoPara : null;
    }

    /// <summary>Valor no padrão brasileiro (vírgula), sem depender da cultura do servidor.</summary>
    public static string ValorEmTexto(decimal valor) => valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Primeiro mês em que o custo é cobrado: o mês de início do contrato mais a carência.</summary>
    public static DateOnly InicioCobranca(DateOnly inicioContrato, int carenciaMeses) => PrimeiroDia(inicioContrato).AddMonths(carenciaMeses);

    /// <summary>
    /// Custos fixos do consultor PJ que começam a ser cobrados nos próximos dias (<see cref="DiasDeAntecedenciaDoAviso"/>) e cujo aviso a gestão
    /// ainda não confirmou. Só entram custos com valor maior que zero no mês em que começam.
    /// </summary>
    public static IReadOnlyList<(ParametroFinanceiro Parametro, DateOnly Inicio, decimal Valor, int Dias)> AlertasDeInicio(
        CrmFinanceiroPerfil? perfil, IReadOnlyCollection<CrmFinanceiroValor> valores, Regras regras, DateOnly hoje)
    {
        if (perfil is not { Tipo: TipoContrato.Pj, InicioContrato: { } inicioContrato }) return [];
        var alertas = new List<(ParametroFinanceiro, DateOnly, decimal, int)>();
        foreach (var p in CustosFixosPj)
        {
            var carencia = regras.Carencia(p);
            if (carencia <= 0) continue;
            var inicio = InicioCobranca(inicioContrato, carencia);
            var dias = inicio.DayNumber - hoje.DayNumber;
            if (dias <= 0 || dias > DiasDeAntecedenciaDoAviso) continue;
            if (regras.AlertaConfirmadoPara(p) == inicio) continue;
            var valor = Vigente(valores, p, inicio)?.Valor ?? regras.ValorPadrao(p);
            if (valor > 0) alertas.Add((p, inicio, valor, dias));
        }
        return alertas;
    }

    public static DateOnly PrimeiroDia(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>
    /// Valor do parâmetro no mês: o último lançamento com vigência até o mês (vale até o próximo lançamento). Com <paramref name="soNoMes"/> só conta
    /// o lançamento do próprio mês — é o caso da recorrência, que varia todo mês. Sem lançamento, retorna nulo.
    /// </summary>
    public static CrmFinanceiroValor? Vigente(IEnumerable<CrmFinanceiroValor> valores, ParametroFinanceiro parametro, DateOnly mes, bool soNoMes = false)
    {
        var inicio = PrimeiroDia(mes);
        var doParametro = valores.Where(v => v.Parametro == parametro);
        return soNoMes
            ? doParametro.FirstOrDefault(v => v.VigenteDesde == inicio)
            : doParametro.Where(v => v.VigenteDesde <= inicio).OrderByDescending(v => v.VigenteDesde).FirstOrDefault();
    }

    public static decimal ValorNoMes(IEnumerable<CrmFinanceiroValor> valores, ParametroFinanceiro parametro, DateOnly mes, bool soNoMes = false) =>
        Vigente(valores, parametro, mes, soNoMes)?.Valor ?? 0m;

    /// <summary>Dias úteis (segunda a sexta) do mês — base do transporte por Uber ou ônibus.</summary>
    public static int DiasUteis(int ano, int mes)
    {
        var dias = 0;
        for (var d = 1; d <= DateTime.DaysInMonth(ano, mes); d++)
        {
            var semana = new DateOnly(ano, mes, d).DayOfWeek;
            if (semana is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dias++;
        }
        return dias;
    }

    /// <summary>Semanas do mês (dias ÷ 7, duas casas) — base do combustível, cobrado por semana.</summary>
    public static decimal Semanas(int ano, int mes) => Math.Round(DateTime.DaysInMonth(ano, mes) / 7m, 2);

    /// <summary>Custo do transporte no mês conforme a modalidade: combustível por semana, Uber/ônibus por dia útil.</summary>
    public static decimal CustoDeTransporte(ModalidadeTransporte modalidade, decimal valorUnitario, int ano, int mes) => modalidade switch
    {
        ModalidadeTransporte.Combustivel => Math.Round(valorUnitario * Semanas(ano, mes), 2),
        ModalidadeTransporte.Uber or ModalidadeTransporte.Onibus => Math.Round(valorUnitario * DiasUteis(ano, mes), 2),
        _ => 0m,
    };

    /// <summary>
    /// O contrato mês a mês do ano. Só entram meses que já começaram (do início do contrato até o mês atual): salário, recorrência e custos fixos
    /// são do que já aconteceu. Os valores por venda (bônus por placa e % da adesão) são do mês de cada venda e saem para todos os meses.
    /// </summary>
    public static IReadOnlyList<FinanceiroMesContratoDto> Meses(CrmFinanceiroPerfil? perfil, IReadOnlyCollection<CrmFinanceiroValor> valores, int ano, DateOnly hoje, Regras? regras = null)
    {
        if (perfil?.Tipo is not { } tipo) return [];
        regras ??= Regras.Nenhuma;
        var inicio = perfil.InicioContrato is { } i ? PrimeiroDia(i) : new DateOnly(ano, 1, 1);
        var limite = PrimeiroDia(hoje);

        var meses = new List<FinanceiroMesContratoDto>();
        for (var m = 1; m <= 12; m++)
        {
            var mes = new DateOnly(ano, m, 1);
            var correu = mes >= inicio && mes <= limite;

            // PJ é remunerado por bônus por placa OU por recorrência, nunca os dois: o outro valor, mesmo que exista no histórico, não conta.
            var pjPorBonus = tipo == TipoContrato.Pj && perfil.ModeloPj != ModeloRemuneracaoPj.Recorrencia;
            var pjPorRecorrencia = tipo == TipoContrato.Pj && perfil.ModeloPj == ModeloRemuneracaoPj.Recorrencia;
            var bonus = pjPorBonus ? ValorNoMes(valores, ParametroFinanceiro.BonusPorPlaca, mes) : 0m;
            var pct = tipo == TipoContrato.Clt ? ValorNoMes(valores, ParametroFinanceiro.PercentualAdesao, mes) : 0m;
            var salario = tipo == TipoContrato.Clt && correu ? ValorNoMes(valores, ParametroFinanceiro.Salario, mes) : 0m;
            var recorrencia = pjPorRecorrencia && correu ? ValorNoMes(valores, ParametroFinanceiro.Recorrencia, mes, soNoMes: true) : 0m;

            var custos = new List<FinanceiroCustoFixoDto>();
            if (tipo == TipoContrato.Pj && correu)
            {
                // Cada custo só é cobrado a partir do fim da carência (meses depois do início do contrato), que pode ser própria do consultor.
                decimal Mensal(ParametroFinanceiro p) =>
                    mes >= InicioCobranca(inicio, regras.Carencia(p)) && regras.Liberado(p, mes) ? Vigente(valores, p, mes)?.Valor ?? regras.ValorPadrao(p) : 0m;

                var cowork = Mensal(ParametroFinanceiro.Coworking);
                if (cowork > 0) custos.Add(new FinanceiroCustoFixoDto("Coworking", "Coworking (fixo)", cowork, "Coworking"));

                var mei = Mensal(ParametroFinanceiro.ImpostoMei);
                if (mei > 0) custos.Add(new FinanceiroCustoFixoDto("Impostos / MEI", "Imposto MEI (fixo)", mei, "ImpostoMei"));

                var trafego = Mensal(ParametroFinanceiro.TrafegoPago);
                if (trafego > 0) custos.Add(new FinanceiroCustoFixoDto("Gestão de Tráfego", "Tráfego pago (fixo)", trafego, "TrafegoPago"));

                // Custo real do lead: o valor por lead do mês (varia de mês para mês) vezes os leads de tráfego pago e do Notion que o consultor pegou no mês.
                var valorPorLead = Mensal(ParametroFinanceiro.CustoLead);
                var leadsDoMes = regras.LeadsNoMes(mes);
                var custoLead = Math.Round(valorPorLead * leadsDoMes, 2);
                if (custoLead > 0)
                    custos.Add(new FinanceiroCustoFixoDto("Custo de Leads", $"Custo de leads — {leadsDoMes} lead(s) × R$ {ValorEmTexto(valorPorLead)} (fixo)", custoLead, "CustoLead"));

                var transporte = CustoDeTransporte(perfil.Transporte, ValorNoMes(valores, ParametroFinanceiro.Transporte, mes), ano, m);
                if (transporte > 0)
                {
                    var rotulo = perfil.Transporte switch
                    {
                        ModalidadeTransporte.Combustivel => "Combustível (por semana)",
                        ModalidadeTransporte.Uber => "Uber (por dia útil)",
                        _ => "Ônibus (por dia útil)",
                    };
                    custos.Add(new FinanceiroCustoFixoDto("Transporte / Combustível", $"{rotulo} (fixo)", transporte, "Transporte"));
                }

            }

            meses.Add(new FinanceiroMesContratoDto(m, salario, recorrencia, bonus, pct, custos));
        }
        return meses;
    }
}
