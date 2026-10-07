using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmDiscordLeituraConfiguration : IEntityTypeConfiguration<CrmDiscordLeitura>
{
    public void Configure(EntityTypeBuilder<CrmDiscordLeitura> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmDiscordLeituras");
        builder.Property(e => e.Chave).IsRequired().HasMaxLength(80);
        builder.Property(e => e.UltimaLidaId).IsRequired().HasMaxLength(32);
        builder.HasIndex(e => new { e.UsuarioId, e.Chave }).IsUnique();
    }
}
