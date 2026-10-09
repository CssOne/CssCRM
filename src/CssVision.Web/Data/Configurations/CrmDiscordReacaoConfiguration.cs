using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmDiscordReacaoConfiguration : IEntityTypeConfiguration<CrmDiscordReacao>
{
    public void Configure(EntityTypeBuilder<CrmDiscordReacao> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmDiscordReacoes");
        builder.Property(e => e.MensagemId).IsRequired().HasMaxLength(32);
        builder.Property(e => e.LeituraId).IsRequired().HasMaxLength(32);
        builder.Property(e => e.Emoji).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => new { e.MensagemId, e.UsuarioId, e.Emoji }).IsUnique();
        builder.HasIndex(e => e.LeituraId);
    }
}
