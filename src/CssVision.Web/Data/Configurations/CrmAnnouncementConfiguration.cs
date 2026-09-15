using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmAnnouncementConfiguration : IEntityTypeConfiguration<CrmAnnouncement>
{
    public void Configure(EntityTypeBuilder<CrmAnnouncement> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmAnnouncements");

        builder.Property(e => e.Titulo).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Descricao).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Cor).HasMaxLength(20);

        builder.HasIndex(e => new { e.Tipo, e.Ativo, e.Ordem });
    }
}
