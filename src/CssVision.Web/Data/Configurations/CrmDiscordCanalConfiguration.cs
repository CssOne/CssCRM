using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmDiscordCanalConfiguration : IEntityTypeConfiguration<CrmDiscordCanal>
{
    public void Configure(EntityTypeBuilder<CrmDiscordCanal> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmDiscordCanais");
        builder.Property(e => e.Chave).IsRequired().HasMaxLength(80);
        builder.Property(e => e.Nome).IsRequired().HasMaxLength(200);
        builder.Property(e => e.DiscordCanalId).IsRequired().HasMaxLength(32);
        builder.Property(e => e.DiscordCargoId).IsRequired().HasMaxLength(32);
        builder.HasIndex(e => e.Chave).IsUnique();
    }
}
