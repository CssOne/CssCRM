using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmRegionalConfiguration : IEntityTypeConfiguration<CrmRegional>
{
    public void Configure(EntityTypeBuilder<CrmRegional> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmRegionais");

        builder.Property(e => e.Nome).IsRequired().HasMaxLength(80);

        builder.HasIndex(e => e.Nome).IsUnique();
        builder.HasIndex(e => e.Ativa);
    }
}
