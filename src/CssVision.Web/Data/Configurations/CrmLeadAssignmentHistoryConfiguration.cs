using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmLeadAssignmentHistoryConfiguration : IEntityTypeConfiguration<CrmLeadAssignmentHistory>
{
    public void Configure(EntityTypeBuilder<CrmLeadAssignmentHistory> builder)
    {
        builder.HasKey(e => e.Id);
        builder.ToTable("CrmLeadAssignmentHistories");

        builder.Property(e => e.Motivo).HasMaxLength(300);

        builder.HasIndex(e => e.LeadId);
        builder.HasIndex(e => e.AlteradoEm);

        builder.HasOne(e => e.Lead)
            .WithMany(l => l.HistoricoAtribuicoes)
            .HasForeignKey(e => e.LeadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ResponsavelAnterior)
            .WithMany()
            .HasForeignKey(e => e.ResponsavelAnteriorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ResponsavelNovo)
            .WithMany()
            .HasForeignKey(e => e.ResponsavelNovoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.AlteradoPor)
            .WithMany()
            .HasForeignKey(e => e.AlteradoPorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
