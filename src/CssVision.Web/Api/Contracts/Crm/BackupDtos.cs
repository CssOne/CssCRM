namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Um backup do banco na tela de Backups (Status/Origem por extenso: "Concluido", "Automatico"...).</summary>
public record CrmBackupDto(
    Guid Id,
    string Status,
    string Origem,
    DateTimeOffset CriadoEm,
    DateTimeOffset? ConcluidoEm,
    long TamanhoBytes,
    string? NomeArquivo,
    int Leads,
    int Oportunidades,
    int Usuarios,
    string? Erro);
