using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmAttachmentConfiguration : IEntityTypeConfiguration<CrmAttachment>
{
    public void Configure(EntityTypeBuilder<CrmAttachment> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmAttachments");

        builder.Property(e => e.NomeArquivo).IsRequired().HasMaxLength(260);
        builder.Property(e => e.CaminhoArmazenamento).IsRequired().HasMaxLength(500);
        builder.Property(e => e.TipoConteudo).HasMaxLength(150);

        builder.HasIndex(e => e.LeadId);

        builder.HasOne(e => e.Lead)
            .WithMany(l => l.Anexos)
            .HasForeignKey(e => e.LeadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Opportunity)
            .WithMany()
            .HasForeignKey(e => e.OpportunityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.EnviadoPor)
            .WithMany()
            .HasForeignKey(e => e.EnviadoPorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CrmAuditLogConfiguration : IEntityTypeConfiguration<CrmAuditLog>
{
    public void Configure(EntityTypeBuilder<CrmAuditLog> builder)
    {
        builder.HasKey(e => e.Id);
        builder.ToTable("CrmAuditLogs");

        builder.Property(e => e.Acao).IsRequired().HasMaxLength(80);
        builder.Property(e => e.EntidadeTipo).IsRequired().HasMaxLength(80);

        builder.HasIndex(e => e.OcorridoEm);
        builder.HasIndex(e => new { e.EntidadeTipo, e.EntidadeId });

        builder.HasOne(e => e.Usuario)
            .WithMany()
            .HasForeignKey(e => e.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
