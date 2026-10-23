using System.Text;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Regras de entrada do Controle financeiro, num lugar só: tudo o que chega da tela é conferido aqui antes de tocar o banco
/// (listas fechadas, limites de valor e de data, tamanho e limpeza de texto). Nada do que o navegador manda é confiável por si.
/// </summary>
public static class FinanceiroValidador
{
    public const decimal ValorMaximo = 10_000_000m;
    public const int AnoMinimo = 2020;
    public const int AnoMaximo = 2100;

    public static readonly string[] Produtos = ["Adesão", "Recorrência", "Bônus", "Premiações", "Outros"];

    public static readonly string[] Categorias =
    [
        "Rastreador", "Gestão de Tráfego", "Custo de Leads", "Coworking", "Impostos / MEI", "Telefone / Internet",
        "Transporte / Combustível", "Material de Trabalho", "Retiradas", "Alimentação", "Indicação", "Assinaturas", "Outros",
    ];

    public static int ValidarAno(int ano) =>
        ano is < AnoMinimo or > AnoMaximo ? throw Invalido($"O ano deve estar entre {AnoMinimo} e {AnoMaximo}.", "ano") : ano;

    /// <summary>
    /// Venda lançada à mão (<paramref name="doCard"/> falso) valida tudo. Venda que veio do quadro de leads só tem o
    /// complemento validado aqui — cliente, data, produto e adesão vêm do card e o que a tela mandar neles é ignorado.
    /// </summary>
    public static void Validar(FinanceiroVendaDto v, bool doCard = false)
    {
        if (!doCard)
        {
            Texto(v.Cliente, "Cliente", 200, obrigatorio: true);
            Valor(v.ValorAdesao, "Valor da adesão");
            Data(v.DataVenda, "Data da venda");
            Lista(v.Produto, Produtos, "Produto");
        }
        Texto(v.Observacoes, "Observações", 1000);
        Valor(v.ValorBonus, "Bônus / recorrência");
        Valor(v.Premiacoes, "Premiações");
        Data(v.DataPrevistaAdesao, "Previsão da adesão");
        Data(v.DataPrevistaBonus, "Previsão do bônus");
        Lista(v.StatusAdesao, FinanceiroStatus.Recebimento, "Status da adesão");
        Lista(v.StatusBonus, FinanceiroStatus.Recebimento, "Status do bônus");
    }

    public static void Validar(FinanceiroCustoDto c)
    {
        Texto(c.Descricao, "Descrição", 300);
        Valor(c.Valor, "Valor");
        if (c.Valor <= 0) throw Invalido("Informe o valor do custo.", "valor");
        Data(c.Data, "Data do custo");
        Data(c.Vencimento, "Vencimento");
        Data(c.DataPagamento, "Data do pagamento");
        Lista(c.Categoria, Categorias, "Categoria");
        Lista(c.Status, FinanceiroStatus.Pagamento, "Status");
    }

    public static void Validar(FinanceiroMetaDto m)
    {
        if (m.Mes is < 1 or > 12) throw Invalido("Mês inválido.", "mes");
        Valor(m.MetaAdesao, "Meta de adesão");
        Valor(m.MetaBonus, "Meta de bônus");
        Valor(m.MetaRecorrencia, "Meta de recorrência");
        if (m.MetaVendas is < 0 or > 100_000) throw Invalido("A meta de vendas deve estar entre 0 e 100.000.", "metaVendas");
        Texto(m.Observacoes, "Observações", 1000);
    }

    public static void Validar(FinanceiroConfigDto c)
    {
        if (c.PercentualReserva is < 0 or > 1) throw Invalido("A reserva deve estar entre 0% e 100%.", "percentualReserva");
        if (Math.Abs(c.CaixaInicial) > ValorMaximo) throw Invalido("O caixa inicial está fora do limite permitido.", "caixaInicial");
    }

    /// <summary>
    /// Texto livre pronto para gravar: sem espaços nas pontas e sem caracteres de controle (que não têm uso em um campo
    /// de texto e servem para esconder conteúdo ou quebrar relatórios). Vazio vira nulo.
    /// </summary>
    public static string? Limpar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Trim())
        {
            if (!char.IsControl(ch) || ch is '\n' or '\r' or '\t') sb.Append(ch);
        }
        var limpo = sb.ToString().Trim();
        return limpo.Length == 0 ? null : limpo;
    }

    /// <summary>Valores em reais: duas casas, sem negativo e até o teto — evita estourar a coluna numérica e valores absurdos.</summary>
    public static decimal Arredondar(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    private static void Valor(decimal valor, string campo)
    {
        if (valor < 0) throw Invalido($"{campo} não pode ser negativo.", campo);
        if (valor > ValorMaximo) throw Invalido($"{campo} está acima do limite permitido.", campo);
    }

    private static void Data(DateOnly? data, string campo)
    {
        if (data is { } d && (d.Year < AnoMinimo || d.Year > AnoMaximo))
            throw Invalido($"{campo} deve estar entre {AnoMinimo} e {AnoMaximo}.", campo);
    }

    private static void Texto(string? texto, string campo, int max, bool obrigatorio = false)
    {
        var limpo = Limpar(texto);
        if (limpo is null)
        {
            if (obrigatorio) throw Invalido($"{campo} é obrigatório.", campo);
            return;
        }
        if (limpo.Length > max) throw Invalido($"{campo} aceita no máximo {max} caracteres.", campo);
    }

    private static void Lista(string? valor, string[] permitidos, string campo)
    {
        if (valor is null || !permitidos.Contains(valor)) throw Invalido($"Valor inválido em \"{campo}\".", campo);
    }

    private static CrmBusinessException Invalido(string mensagem, string campo) =>
        new(mensagem, "financeiro_invalido", new { campo });
}
