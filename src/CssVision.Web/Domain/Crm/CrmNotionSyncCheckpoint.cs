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
}
