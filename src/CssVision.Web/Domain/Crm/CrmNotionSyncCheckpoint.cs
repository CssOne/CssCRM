namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Marca até quando cada base do Notion (data source) já foi sincronizada — a sincronização
/// periódica busca só o que foi editado depois de <see cref="UltimaSincronizacaoEm"/>. Não herda
/// CrmEntityBase de propósito: roda fora de uma requisição HTTP (serviço em segundo plano), sem
/// usuário autenticado para preencher os campos de auditoria.
/// </summary>
public class CrmNotionSyncCheckpoint
{
    /// <summary>Id do data source no Notion — chave primária.</summary>
    public string DataSourceId { get; set; } = string.Empty;

    public string RegionalNome { get; set; } = string.Empty;

    public DateTimeOffset UltimaSincronizacaoEm { get; set; }

    /// <summary>
    /// Quando a base foi relida inteira (cards a partir da data mínima de importação) para alinhar a
    /// coluna de cada lead ao Status atual do Notion. Nulo = realinhamento ainda pendente.
    /// </summary>
    public DateTimeOffset? RealinhamentoConcluidoEm { get; set; }

    /// <summary>
    /// Quando a base foi relida inteira (todas as datas) para trazer todos os cards dos consultores
    /// ativos no CRM, com todos os campos — inclusive cards sem nome ou anteriores à data mínima.
    /// Nulo = reimportação ainda pendente (roda uma vez, depois do realinhamento).
    /// </summary>
    public DateTimeOffset? ReimportacaoAtivosConcluidaEm { get; set; }

    /// <summary>
    /// Quando todas as vendas da base (cards "VENDA CONCLUIDA", de qualquer vendedor e data) foram
    /// trazidas para o CRM, uma oportunidade por card. Nulo = importação de vendas pendente.
    /// </summary>
    public DateTimeOffset? ImportacaoVendasConcluidaEm { get; set; }

    /// <summary>
    /// Quando a "Data de chegada" de todos os cards da base foi gravada nos leads ligados a eles
    /// (antes a importação gravava a data da migração). Nulo = correção pendente.
    /// </summary>
    public DateTimeOffset? DataChegadaCorrigidaEm { get; set; }

    /// <summary>
    /// Quando os cards criados em 2025 (01/01/2025 até 31/12/2025) foram trazidos para o CRM.
    /// Nulo = importação de 2025 pendente (roda uma vez por base).
    /// </summary>
    public DateTimeOffset? Importacao2025ConcluidaEm { get; set; }

    /// <summary>
    /// Quando todos os cards criados desde a data mínima de importação (01/01/2025) que ainda não tinham
    /// lead no CRM foram trazidos — cada card do Notion passa a aparecer no quadro de leads. Nulo =
    /// importação completa pendente (roda uma vez por base).
    /// </summary>
    public DateTimeOffset? ImportacaoCompletaConcluidaEm { get; set; }

    /// <summary>
    /// Quando os cards de leads parados em "Vendedor não identificado" foram relidos para ligar cada um ao vendedor
    /// do card. Nulo = revisão pendente (roda uma vez por base).
    /// </summary>
    public DateTimeOffset? RevisaoVendedoresConcluidaEm { get; set; }
}
