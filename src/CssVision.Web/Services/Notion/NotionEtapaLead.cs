namespace CssVision.Web.Services.Notion;

/// <summary>
/// Regras do quadro de leads para decidir a coluna (CrmLeadStage) de um lead a partir do "Status"
/// do card no Notion. Espelha as regras do próprio quadro:
/// <list type="bullet">
/// <item>card sem Status fica em "Sem etapa" (EtapaId nulo);</item>
/// <item>"Em atendimento" e "Venda concluída" têm duas colunas cada — "(Leads)" só recebe cartões
/// Lead e "(Indicação)" só recebe cartões Indicação (mesma regra de LeadsKanban.tsx);</item>
/// <item>PRÉ CADASTRO vai para "Cotação" e RECUSA/INATIVA para "Perdido", como fizeram as migrações
/// que desativaram essas colunas (RemovePreCadastroLeadStage / RemoveRecusaInativaLeadStage);</item>
/// <item>a base MG132 usa Status próprios: EM COTAÇÃO (= Cotação) e JÁ TEM SEGURO (lead perdido,
/// com esse mesmo texto como motivo de perda).</item>
/// </list>
/// </summary>
public static class NotionEtapaLead
{
    public const string EmAtendimento = "Em atendimento";
    public const string Cotacao = "Cotação";
    public const string VendaConcluida = "Venda concluída";
    public const string Perdido = "Perdido";
    public const string NaoFazemos = "Não fazemos";

    private static readonly Dictionary<string, string> StatusParaColuna = new()
    {
        ["EM ATENDIMENTO"] = EmAtendimento,
        ["COTAÇÃO"] = Cotacao,
        ["EM COTAÇÃO"] = Cotacao,
        ["PRÉ CADASTRO"] = Cotacao,
        ["VENDA CONCLUIDA"] = VendaConcluida,
        ["PERDIDO"] = Perdido,
        ["RECUSA/INATIVA"] = Perdido,
        ["JÁ TEM SEGURO"] = Perdido,
        ["NÃO FAZEMOS"] = NaoFazemos,
    };

    /// <summary>Status comparável: sem espaços nas pontas ("NÃO FAZEMOS " no Notion) e em maiúsculas.</summary>
    public static string? Normalizar(string? status) =>
        string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();

    /// <summary>Mesma regra da etiqueta Lead/Indicação do cartão do quadro (classificarCartao em LeadsKanban.tsx).</summary>
    public static bool EhIndicacao(bool criadoManualmente, string? tipoIndicacao) =>
        !string.Equals(tipoIndicacao, "Lead", StringComparison.OrdinalIgnoreCase)
        // Indicação, Pessoal, Contemplando Sonhos... — qualquer tipo que não seja "Lead".
        && (criadoManualmente || !string.IsNullOrWhiteSpace(tipoIndicacao));

    /// <param name="etapasAtivasPorNome">Colunas ativas do quadro, por nome.</param>
    /// <returns>
    /// <c>Reconhecido = false</c> quando o Status não tem coluna correspondente (o lead fica onde está);
    /// senão, a coluna de destino — <c>EtapaId = null</c> significa "Sem etapa".
    /// </returns>
    public static (bool Reconhecido, Guid? EtapaId, string? Nome) Resolver(
        string? statusNotion, bool ehIndicacao, IReadOnlyDictionary<string, Guid> etapasAtivasPorNome)
    {
        var status = Normalizar(statusNotion);
        if (status is null) return (true, null, null);
        if (!StatusParaColuna.TryGetValue(status, out var coluna)) return (false, null, null);

        if (coluna is EmAtendimento or VendaConcluida)
        {
            var nomeDividido = $"{coluna} ({(ehIndicacao ? "Indicação" : "Leads")})";
            if (etapasAtivasPorNome.TryGetValue(nomeDividido, out var idDividido)) return (true, idDividido, nomeDividido);
        }

        return etapasAtivasPorNome.TryGetValue(coluna, out var id) ? (true, id, coluna) : (false, null, null);
    }

    /// <summary>Motivo de perda quando o card do Notion não tem "Motivo da perda" preenchido.</summary>
    public static string MotivoPerdaPadrao(string? statusNotion) => Normalizar(statusNotion) switch
    {
        "RECUSA/INATIVA" => "Recusa/Inativa",
        "JÁ TEM SEGURO" => "Já tem seguro",
        _ => "Não informado no Notion",
    };
}
