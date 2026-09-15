using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmLeadStageConfiguration : IEntityTypeConfiguration<CrmLeadStage>
{
    public void Configure(EntityTypeBuilder<CrmLeadStage> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmLeadStages");

        builder.Property(e => e.Nome).IsRequired().HasMaxLength(80);
        builder.Property(e => e.Cor).HasMaxLength(20);

        builder.HasIndex(e => e.Ordem);
        builder.HasIndex(e => e.Ativa);
    }
}
