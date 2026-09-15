using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmGrupoConfiguration : IEntityTypeConfiguration<CrmGrupo>
{
    public void Configure(EntityTypeBuilder<CrmGrupo> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmGrupos");

        builder.Property(e => e.Nome).IsRequired().HasMaxLength(80);

        builder.HasIndex(e => new { e.RegionalId, e.Nome }).IsUnique();
        builder.HasIndex(e => e.Ativo);

        builder.HasOne(e => e.Regional)
            .WithMany()
            .HasForeignKey(e => e.RegionalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
