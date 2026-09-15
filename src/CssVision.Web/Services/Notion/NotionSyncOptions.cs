namespace CssVision.Web.Services.Notion;

/// <summary>Configuração da sincronização periódica com o Notion (seção "NotionSync" do appsettings).</summary>
public class NotionSyncOptions
{
    /// <summary>Liga/desliga o serviço em segundo plano. Fica falso se não houver Token configurado.</summary>
    public bool Enabled { get; set; }

    /// <summary>Token de integração do Notion — normalmente vem do Secrets Manager (NOTION_TOKEN).</summary>
    public string? Token { get; set; }

    public int IntervalMinutes { get; set; } = 15;
}
