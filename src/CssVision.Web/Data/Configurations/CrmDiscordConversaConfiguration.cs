using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmDiscordConversaConfiguration : IEntityTypeConfiguration<CrmDiscordConversa>
{
    public void Configure(EntityTypeBuilder<CrmDiscordConversa> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmDiscordConversas");
        builder.Property(e => e.DiscordThreadId).IsRequired().HasMaxLength(32);
        builder.Property(e => e.DiscordVozId).HasMaxLength(32);
        builder.HasIndex(e => new { e.UsuarioAId, e.UsuarioBId }).IsUnique();
    }
}
