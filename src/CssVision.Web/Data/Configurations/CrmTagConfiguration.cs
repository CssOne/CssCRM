using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmTagConfiguration : IEntityTypeConfiguration<CrmTag>
{
    public void Configure(EntityTypeBuilder<CrmTag> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmTags");

        builder.Property(e => e.Nome).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Cor).HasMaxLength(9);
        builder.HasIndex(e => e.Nome).IsUnique();
    }
}

public class CrmLeadTagConfiguration : IEntityTypeConfiguration<CrmLeadTag>
{
    public void Configure(EntityTypeBuilder<CrmLeadTag> builder)
    {
        builder.ToTable("CrmLeadTags");
        builder.HasKey(e => new { e.LeadId, e.TagId });

        builder.HasOne(e => e.Lead)
            .WithMany(l => l.LeadTags)
            .HasForeignKey(e => e.LeadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tag)
            .WithMany(t => t.LeadTags)
            .HasForeignKey(e => e.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
