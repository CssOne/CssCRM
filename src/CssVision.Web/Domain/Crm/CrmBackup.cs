namespace CssVision.Web.Domain.Crm;

public enum StatusBackup
{
    EmAndamento = 0,
    Concluido = 1,
    Falhou = 2,
    /// <summary>Arquivo apagado pela retenção (só os mais recentes são guardados).</summary>
    Removido = 3,
}

public enum OrigemBackup
{
    /// <summary>Backup diário feito sozinho pelo sistema (BackupDiarioBackgroundService).</summary>
    Automatico = 0,
    /// <summary>"Gerar backup agora" na tela de Backups.</summary>
    Manual = 1,
}

/// <summary>
/// Cópia completa do banco (pg_dump) guardada fora dele — no S3 em produção. Esta tabela é o
/// índice das cópias (o armazenamento não precisa ser listado) e registra quantos leads/vendas
/// havia em cada uma, para conferir rapidamente se a cópia está completa.
/// </summary>
public class CrmBackup : CrmEntityBase
{
    public StatusBackup Status { get; set; }
    public OrigemBackup Origem { get; set; }

    /// <summary>Onde o arquivo está no armazenamento (ex.: "backups/crm-20260930-0300.dump").</summary>
    public string? Chave { get; set; }
    public long TamanhoBytes { get; set; }
    public DateTimeOffset? ConcluidoEm { get; set; }
    public string? Erro { get; set; }

    public int Leads { get; set; }
    public int Oportunidades { get; set; }
    public int Usuarios { get; set; }
}
