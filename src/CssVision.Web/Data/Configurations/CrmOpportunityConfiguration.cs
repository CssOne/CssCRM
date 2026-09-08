using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmOpportunityConfiguration : IEntityTypeConfiguration<CrmOpportunity>
{
    public void Configure(EntityTypeBuilder<CrmOpportunity> builder)
    {
        builder.ConfigureCrmArchivable();
        builder.ToTable("CrmOpportunities");

        builder.Property(e => e.Titulo).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ProdutoOuServico).HasMaxLength(120);
        builder.Property(e => e.Concorrente).HasMaxLength(120);
        builder.Property(e => e.ValorEstimado).HasColumnType("numeric(14,2)");
        builder.Property(e => e.ValorFinal).HasColumnType("numeric(14,2)");

        builder.HasIndex(e => e.ResponsavelId);
        builder.HasIndex(e => e.EtapaId);
        builder.HasIndex(e => e.LeadId);
        builder.HasIndex(e => e.DataPrevistaFechamento);
        builder.HasIndex(e => e.DataEfetivaFechamento);
        builder.HasIndex(e => e.CriadoEm);

        builder.HasOne(e => e.Lead)
            .WithMany(l => l.Oportunidades)
            .HasForeignKey(e => e.LeadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Responsavel)
            .WithMany()
            .HasForeignKey(e => e.ResponsavelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Etapa)
            .WithMany(s => s.Oportunidades)
            .HasForeignKey(e => e.EtapaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.MotivoPerda)
            .WithMany(m => m.Oportunidades)
            .HasForeignKey(e => e.MotivoPerdaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
