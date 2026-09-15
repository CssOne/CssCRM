using CssVision.Web.Services.Crm;

namespace NotionMigration;

/// <summary>Identidade fixa usada só para satisfazer o ApplicationDbContext fora de uma requisição HTTP.</summary>
public sealed class MigrationCurrentUser : ICurrentUserService
{
    public Guid UserId => Guid.Empty;
    public string? NomeCompleto => "Migração Notion";
    public bool IsAuthenticated => true;
    public bool IsInRole(string role) => false;
    public bool TemVisaoTotal => true;
    public bool PodeGerirComercial => true;
    public bool IsGestorComercial => false;
}
