using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmAvisoConsultorConfiguration : IEntityTypeConfiguration<CrmAvisoConsultor>
{
    public void Configure(EntityTypeBuilder<CrmAvisoConsultor> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmAvisosConsultor");
        builder.Property(e => e.Titulo).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Mensagem).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Valor).HasPrecision(18, 2);
        builder.Property(e => e.Referencia).HasMaxLength(200);
        builder.HasIndex(e => new { e.ConsultorId, e.Status });
        builder.HasIndex(e => e.CriadoEm);
    }
}
