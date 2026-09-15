using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmRegionalGoalConfiguration : IEntityTypeConfiguration<CrmRegionalGoal>
{
    public void Configure(EntityTypeBuilder<CrmRegionalGoal> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmRegionalGoals");

        builder.Property(e => e.MetaValor).HasColumnType("numeric(14,2)");

        // Uma única meta por regional/mês.
        builder.HasIndex(e => new { e.RegionalId, e.MesReferencia }).IsUnique();

        builder.HasOne(e => e.Regional)
            .WithMany()
            .HasForeignKey(e => e.RegionalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
