using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmActivityConfiguration : IEntityTypeConfiguration<CrmActivity>
{
    public void Configure(EntityTypeBuilder<CrmActivity> builder)
    {
        builder.ConfigureCrmArchivable();
        builder.ToTable("CrmActivities");

        builder.Property(e => e.Assunto).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Resultado).HasMaxLength(500);

        builder.HasIndex(e => e.ResponsavelId);
        builder.HasIndex(e => e.LeadId);
        builder.HasIndex(e => e.OpportunityId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.DataHoraPrevista);
        builder.HasIndex(e => e.Tipo);

        builder.HasOne(e => e.Lead)
            .WithMany(l => l.Atividades)
            .HasForeignKey(e => e.LeadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Opportunity)
            .WithMany(o => o.Atividades)
            .HasForeignKey(e => e.OpportunityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Responsavel)
            .WithMany()
            .HasForeignKey(e => e.ResponsavelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
