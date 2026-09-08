namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Campos de auditoria comuns a todas as entidades de negócio do CRM.
/// RowVersion é mapeado para a coluna de sistema "xmin" do PostgreSQL (ver ApplicationDbContext),
/// fornecendo controle de concorrência otimista sem precisar de uma coluna extra.
/// </summary>
public abstract class CrmEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;

    public Guid? CriadoPorId { get; set; }

    public DateTimeOffset? AtualizadoEm { get; set; }

    public Guid? AtualizadoPorId { get; set; }

    /// <summary>Token de concorrência otimista (mapeado para xmin no PostgreSQL).</summary>
    public uint RowVersion { get; set; }
}

/// <summary>
/// Entidade que nunca é fisicamente excluída (possui histórico associado).
/// Em vez de DELETE, usa-se arquivamento lógico.
/// </summary>
public abstract class CrmArchivableEntity : CrmEntityBase
{
    public bool Arquivado { get; set; }
    public DateTimeOffset? ArquivadoEm { get; set; }
    public Guid? ArquivadoPorId { get; set; }
}
