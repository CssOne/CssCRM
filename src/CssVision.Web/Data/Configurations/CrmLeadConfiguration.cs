using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmLeadConfiguration : IEntityTypeConfiguration<CrmLead>
{
    public void Configure(EntityTypeBuilder<CrmLead> builder)
    {
        builder.ConfigureCrmArchivable();
        builder.ToTable("CrmLeads");

        builder.Property(e => e.NomeOuRazaoSocial).IsRequired().HasMaxLength(200);
        builder.Property(e => e.DocumentoNormalizado).HasMaxLength(14);
        builder.Property(e => e.TelefoneNormalizado).HasMaxLength(20);
        builder.Property(e => e.EmailNormalizado).HasMaxLength(256);
        builder.Property(e => e.Email).HasMaxLength(256);
        builder.Property(e => e.Cidade).HasMaxLength(120);
        builder.Property(e => e.Estado).HasMaxLength(2);
        builder.Property(e => e.Regional).HasMaxLength(80);
        builder.Property(e => e.Origem).HasMaxLength(80);
        builder.Property(e => e.Campanha).HasMaxLength(120);
        builder.Property(e => e.ProdutoInteresse).HasMaxLength(120);
        builder.Property(e => e.Gclid).HasMaxLength(200);
        builder.Property(e => e.UtmMedium).HasMaxLength(120);
        builder.Property(e => e.UtmSource).HasMaxLength(120);
        builder.Property(e => e.UtmTerm).HasMaxLength(120);
        builder.Property(e => e.MetaClickId).HasMaxLength(200);
        builder.Property(e => e.MetaFormId).HasMaxLength(120);
        builder.Property(e => e.MetaLeadId).HasMaxLength(120);
        builder.Property(e => e.TipoIndicacao).HasMaxLength(80);

        // Deduplicação: únicos apenas entre leads não arquivados, ignorando nulos.
        builder.HasIndex(e => e.DocumentoNormalizado)
            .HasFilter("\"DocumentoNormalizado\" IS NOT NULL AND \"Arquivado\" = false")
            .IsUnique();

        builder.HasIndex(e => e.EmailNormalizado)
            .HasFilter("\"EmailNormalizado\" IS NOT NULL AND \"Arquivado\" = false")
            .IsUnique();

        builder.HasIndex(e => e.TelefoneNormalizado);

        // Idempotência do webhook de Lead Ads: um mesmo leadgen_id nunca deve virar 2 leads.
        builder.HasIndex(e => e.MetaLeadId)
            .HasFilter("\"MetaLeadId\" IS NOT NULL")
            .IsUnique();

        builder.HasIndex(e => e.ResponsavelId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Regional);
        builder.HasIndex(e => e.Origem);
        builder.HasIndex(e => e.CriadoEm);
        builder.HasIndex(e => e.ProximoContatoEm);
        builder.HasIndex(e => e.UltimoContatoEm);

        builder.HasOne(e => e.Responsavel)
            .WithMany()
            .HasForeignKey(e => e.ResponsavelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.IndicadoPorLead)
            .WithMany()
            .HasForeignKey(e => e.IndicadoPorLeadId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
