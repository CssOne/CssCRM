using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmUsuarioAliasConfiguration : IEntityTypeConfiguration<CrmUsuarioAlias>
{
    public void Configure(EntityTypeBuilder<CrmUsuarioAlias> builder)
    {
        builder.ToTable("CrmUsuarioAliases");
        builder.HasKey(e => e.EmailNormalizado);
        builder.Property(e => e.EmailNormalizado).HasMaxLength(256);
        builder.HasIndex(e => e.UsuarioId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
