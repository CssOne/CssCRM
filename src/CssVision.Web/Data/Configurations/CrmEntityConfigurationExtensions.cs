using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

/// <summary>
/// Configuração comum a todas as entidades do CRM: mapeia RowVersion para a coluna de sistema
/// "xmin" do PostgreSQL (concorrência otimista nativa, sem precisar de coluna extra) e define
/// os campos de auditoria compartilhados.
/// </summary>
public static class CrmEntityConfigurationExtensions
{
    public static void ConfigureCrmBase<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : CrmEntityBase
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(e => e.CriadoEm).IsRequired();
    }

    public static void ConfigureCrmArchivable<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : CrmArchivableEntity
    {
        builder.ConfigureCrmBase();
        builder.HasIndex(e => e.Arquivado);
    }
}
