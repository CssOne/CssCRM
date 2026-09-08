using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmSalesGoalConfiguration : IEntityTypeConfiguration<CrmSalesGoal>
{
    public void Configure(EntityTypeBuilder<CrmSalesGoal> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmSalesGoals");

        builder.Property(e => e.MetaValor).HasColumnType("numeric(14,2)");

        // Uma única meta por vendedor/mês.
        builder.HasIndex(e => new { e.VendedorId, e.MesReferencia }).IsUnique();

        builder.HasOne(e => e.Vendedor)
            .WithMany()
            .HasForeignKey(e => e.VendedorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
