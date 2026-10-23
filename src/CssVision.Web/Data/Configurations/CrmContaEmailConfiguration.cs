using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmContaEmailConfiguration : IEntityTypeConfiguration<CrmContaEmail>
{
    public void Configure(EntityTypeBuilder<CrmContaEmail> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmContasEmail");
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Host).IsRequired().HasMaxLength(200);
        builder.Property(e => e.UserName).IsRequired().HasMaxLength(256);
        builder.Property(e => e.SenhaProtegida).IsRequired().HasMaxLength(2000);
        builder.HasIndex(e => e.UsuarioId).IsUnique();
    }
}
