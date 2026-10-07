using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmDiscordVinculoConfiguration : IEntityTypeConfiguration<CrmDiscordVinculo>
{
    public void Configure(EntityTypeBuilder<CrmDiscordVinculo> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmDiscordVinculos");
        builder.Property(e => e.DiscordUserId).IsRequired().HasMaxLength(32);
        builder.Property(e => e.DiscordNome).IsRequired().HasMaxLength(100);
        // Um usuário, uma conta; uma conta, um usuário.
        builder.HasIndex(e => e.UsuarioId).IsUnique();
        builder.HasIndex(e => e.DiscordUserId).IsUnique();
    }
}
