using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmVeiculoConfiguration : IEntityTypeConfiguration<CrmVeiculo>
{
    public void Configure(EntityTypeBuilder<CrmVeiculo> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmVeiculos");

        builder.Property(e => e.Descricao).HasMaxLength(200);
        builder.Property(e => e.Placa).HasMaxLength(10);
        builder.Property(e => e.Fipe).HasColumnType("numeric(14,2)");
        builder.Property(e => e.Rastreador).HasMaxLength(120);

        builder.HasIndex(e => e.OpportunityId).IsUnique();
        builder.HasIndex(e => e.Placa);

        builder.HasOne(e => e.Opportunity)
            .WithOne(o => o.Veiculo)
            .HasForeignKey<CrmVeiculo>(e => e.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Vistoriador)
            .WithMany()
            .HasForeignKey(e => e.VistoriadorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
