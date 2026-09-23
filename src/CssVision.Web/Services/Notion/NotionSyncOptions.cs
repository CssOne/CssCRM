namespace CssVision.Web.Services.Notion;

/// <summary>Configuração da sincronização periódica com o Notion (seção "NotionSync" do appsettings).</summary>
public class NotionSyncOptions
{
    /// <summary>Liga/desliga o serviço em segundo plano. Fica falso se não houver Token configurado.</summary>
    public bool Enabled { get; set; }

    /// <summary>Token de integração do Notion — normalmente vem do Secrets Manager (NOTION_TOKEN).</summary>
    public string? Token { get; set; }

    /// <summary>
    /// Intervalo entre consultas ao Notion. Curto de propósito: o Notion não consegue avisar o
    /// servidor quando um card muda, então "tempo real" = consultar com frequência só o que foi
    /// editado desde a última execução (poucas requisições por ciclo).
    /// </summary>
    public int IntervalSeconds { get; set; } = 20;
}
