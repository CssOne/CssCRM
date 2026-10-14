namespace CssVision.Web.Services.Crm;

/// <summary>
/// Origem do lead. Para leads do Notion, a Origem é a tag de campanha do card (as mesmas opções do
/// campo Origem na tela — ver ClientApp/src/lib/opcoesLead.ts); sem tag, fica a origem técnica.
/// Para saber se um lead veio do Notion use os marcadores de <see cref="Domain.Crm.CrmLead.ConsentimentoOrigem"/>,
/// e não o texto da Origem, que pode ser trocado pela tag.
/// </summary>
public static class OrigemLead
{
    /// <summary>Marcador gravado pela migração histórica (tools/NotionMigration) — identifica os leads migrados.</summary>
    public const string MarcadorMigracaoNotion = "Migração da base histórica (Notion)";

    /// <summary>Marcador gravado pela sincronização periódica (NotionSyncService).</summary>
    public const string MarcadorSincronizacaoNotion = "Sincronização automática (Notion)";

    /// <summary>Marcador gravado pela entrada de leads do site (PublicLeadIntakeService).</summary>
    public const string MarcadorFormularioSite = "Formulário do site";

    /// <summary>
    /// Lead que chegou direto do tráfego pago (webhook do Meta Lead Ads ou formulário do site), sem ser
    /// da migração/sincronização do Notion — cards migrados também podem trazer o ID do lead no Meta.
    /// É o que conta no limite mensal de cada consultor e o que é distribuído automaticamente.
    /// </summary>
    public static readonly System.Linq.Expressions.Expression<Func<Domain.Crm.CrmLead, bool>> VeioDoTrafegoPago = l =>
        l.ConsentimentoOrigem != MarcadorMigracaoNotion
        && l.ConsentimentoOrigem != MarcadorSincronizacaoNotion
        && (l.MetaLeadId != null || l.ConsentimentoOrigem == MarcadorFormularioSite);

    /// <summary>
    /// Lead de tráfego pago vindo de qualquer lugar: direto dos anúncios pelo sistema novo
    /// (<see cref="VeioDoTrafegoPago"/>) ou card do Notion com sinal de anúncio — ID do lead no Meta,
    /// GCLID do Google ou campanha preenchida. Usado nos números da Gestão comercial.
    /// </summary>
    public static readonly System.Linq.Expressions.Expression<Func<Domain.Crm.CrmLead, bool>> DeTrafegoPagoInclusiveNotion = l =>
        l.MetaLeadId != null || l.Gclid != null || l.ConsentimentoOrigem == MarcadorFormularioSite
        || ((l.ConsentimentoOrigem == MarcadorMigracaoNotion || l.ConsentimentoOrigem == MarcadorSincronizacaoNotion) && l.Campanha != null);

    /// <summary>"Facebook ADS" / "Google ads": as tags de anúncio do campo "Tags" do card do Notion.</summary>
    public static bool EhTagDeAnuncio(string? tag) =>
        tag is not null && (string.Equals(tag.Trim(), "Facebook ADS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag.Trim(), "Google ads", StringComparison.OrdinalIgnoreCase));

    private static readonly Func<Domain.Crm.CrmLead, bool> _deTrafegoPago = l =>
        l.MetaLeadId != null || l.Gclid != null || l.UtmSource != null || l.TagDeAnuncioNotion
        || l.ConsentimentoOrigem == MarcadorFormularioSite
        || ((l.ConsentimentoOrigem == MarcadorMigracaoNotion || l.ConsentimentoOrigem == MarcadorSincronizacaoNotion) && l.Campanha != null);

    /// <summary>
    /// O lead tem algum sinal de tráfego pago: veio direto do anúncio ou do formulário do site, ou é um card do Notion com ID do lead no Meta, GCLID, UTM source,
    /// campanha ou tag Facebook/Google (<see cref="Domain.Crm.CrmLead.TagDeAnuncioNotion"/>).
    /// </summary>
    public static bool DeTrafegoPago(Domain.Crm.CrmLead lead) => _deTrafegoPago(lead);

    /// <summary>
    /// O lead está com o responsável atual dentro do período [<paramref name="inicio"/>, <paramref name="fim"/>) — é a conta de "leads que o consultor pegou".
    /// Vale o momento em que o lead passou para o responsável atual (<see cref="Domain.Crm.CrmLead.ResponsavelAtribuidoEm"/>: rodízio ou troca de responsável),
    /// e não o da criação: um lead atribuído a outro consultor sai da conta de quem o tinha e entra na de quem recebeu, no mês em que isso aconteceu.
    /// Cards vindos do Notion valem pela data de chegada (<c>CriadoEm</c>): neles a atribuição é só o horário em que o sistema os importou/sincronizou.
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<Domain.Crm.CrmLead, bool>> ComPosseNoPeriodo(DateTimeOffset inicio, DateTimeOffset fim) =>
        l => (l.ConsentimentoOrigem == MarcadorMigracaoNotion || l.ConsentimentoOrigem == MarcadorSincronizacaoNotion
                ? l.CriadoEm
                : (l.ResponsavelAtribuidoEm ?? l.CriadoEm)) >= inicio
            && (l.ConsentimentoOrigem == MarcadorMigracaoNotion || l.ConsentimentoOrigem == MarcadorSincronizacaoNotion
                ? l.CriadoEm
                : (l.ResponsavelAtribuidoEm ?? l.CriadoEm)) < fim;

    /// <summary>Mesma regra de <see cref="ComPosseNoPeriodo"/>, sem limite final (de <paramref name="inicio"/> em diante).</summary>
    public static System.Linq.Expressions.Expression<Func<Domain.Crm.CrmLead, bool>> ComPosseDesde(DateTimeOffset inicio) =>
        l => (l.ConsentimentoOrigem == MarcadorMigracaoNotion || l.ConsentimentoOrigem == MarcadorSincronizacaoNotion
                ? l.CriadoEm
                : (l.ResponsavelAtribuidoEm ?? l.CriadoEm)) >= inicio;

    /// <summary>
    /// "Data de chegada" do lead no Relatório comercial, dentro de [<paramref name="inicio"/>, <paramref name="fim"/>): para o lead do sistema vindo do tráfego pago
    /// (<see cref="VeioDoTrafegoPago"/>) vale o momento em que ele passou para o responsável atual (<c>ResponsavelAtribuidoEm</c>), como na Gestão comercial e no
    /// rodízio; para os demais (indicação, cadastro manual, cards do Notion) continua a data de criação/chegada. O mesmo critério está em <c>RelatorioComercialService</c>
    /// (projeção da data usada nos gráficos por mês e semana).
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<Domain.Crm.CrmLead, bool>> ChegouNoPeriodo(DateTimeOffset inicio, DateTimeOffset fim) =>
        l => (l.ConsentimentoOrigem != MarcadorMigracaoNotion && l.ConsentimentoOrigem != MarcadorSincronizacaoNotion
                && (l.MetaLeadId != null || l.ConsentimentoOrigem == MarcadorFormularioSite)
                ? (l.ResponsavelAtribuidoEm ?? l.CriadoEm)
                : l.CriadoEm) >= inicio
            && (l.ConsentimentoOrigem != MarcadorMigracaoNotion && l.ConsentimentoOrigem != MarcadorSincronizacaoNotion
                && (l.MetaLeadId != null || l.ConsentimentoOrigem == MarcadorFormularioSite)
                ? (l.ResponsavelAtribuidoEm ?? l.CriadoEm)
                : l.CriadoEm) < fim;

    public const string OrigemMigracaoNotion = "Migração Notion";
    public const string OrigemSincronizacaoNotion = "Sincronização Notion";

    public static readonly IReadOnlyList<string> Tags =
    [
        "Lookalike", "UGC VENDA", "UGC CAMINHÃO", "Pesquisa", "Pmax",
        "Demand Gen", "UGC", "INFLUENCERS", "Lead convertido", "Caixa de pergunta",
    ];

    /// <summary>A tag correspondente à campanha do card (sem diferenciar maiúsculas: "Demand gen" → "Demand Gen"), ou null.</summary>
    public static string? TagDaCampanha(string? campanha)
    {
        if (string.IsNullOrWhiteSpace(campanha)) return null;
        var valor = campanha.Trim();
        return Tags.FirstOrDefault(t => string.Equals(t, valor, StringComparison.OrdinalIgnoreCase));
    }
}
