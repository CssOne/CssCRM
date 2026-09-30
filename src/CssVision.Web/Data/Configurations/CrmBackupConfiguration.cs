using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmBackupConfiguration : IEntityTypeConfiguration<CrmBackup>
{
    public void Configure(EntityTypeBuilder<CrmBackup> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmBackups");
        builder.Property(e => e.Chave).HasMaxLength(300);
        builder.Property(e => e.Erro).HasMaxLength(2000);
        builder.HasIndex(e => e.CriadoEm);
    }
}
