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

        builder.HasOne(e => e.GestorComercial)
            .WithMany()
            .HasForeignKey(e => e.GestorComercialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
