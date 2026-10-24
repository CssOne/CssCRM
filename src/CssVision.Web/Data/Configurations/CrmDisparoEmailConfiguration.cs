using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmDisparoEmailConfiguration : IEntityTypeConfiguration<CrmDisparoEmail>
{
    public void Configure(EntityTypeBuilder<CrmDisparoEmail> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmDisparosEmail");
        builder.Property(e => e.EtapaNome).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Assunto).IsRequired().HasMaxLength(150);
        builder.Property(e => e.Mensagem).IsRequired().HasMaxLength(5000);
        builder.Property(e => e.Erro).HasMaxLength(500);
        builder.HasIndex(e => new { e.Status, e.CriadoEm });
        builder.HasIndex(e => new { e.UsuarioId, e.CriadoEm });
        builder.HasMany(e => e.Destinatarios).WithOne(d => d.Disparo).HasForeignKey(d => d.DisparoId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmDisparoEmailDestinatarioConfiguration : IEntityTypeConfiguration<CrmDisparoEmailDestinatario>
{
    public void Configure(EntityTypeBuilder<CrmDisparoEmailDestinatario> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmDisparosEmailDestinatarios");
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Nome).IsRequired().HasMaxLength(300);
        builder.HasIndex(e => new { e.DisparoId, e.Status });
    }
}
