using CssVision.Web.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(e => e.NomeCompleto).IsRequired().HasMaxLength(200);

        builder.HasIndex(e => e.GestorComercialId);
        builder.Property(e => e.NotionUserId).HasMaxLength(64);
        builder.HasIndex(e => e.NotionUserId);
        builder.HasIndex(e => e.RegionalId);
        builder.HasIndex(e => e.GrupoId);

        builder.HasOne(e => e.GestorComercial)
            .WithMany()
            .HasForeignKey(e => e.GestorComercialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Regional)
            .WithMany(r => r.Usuarios)
            .HasForeignKey(e => e.RegionalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.RegionalRestritaId);
        builder.HasOne<Domain.Crm.CrmRegional>()
            .WithMany()
            .HasForeignKey(e => e.RegionalRestritaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Grupo)
            .WithMany(g => g.Usuarios)
            .HasForeignKey(e => e.GrupoId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
