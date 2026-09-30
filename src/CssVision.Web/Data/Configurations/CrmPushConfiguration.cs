using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmPushInscricaoConfiguration : IEntityTypeConfiguration<CrmPushInscricao>
{
    public void Configure(EntityTypeBuilder<CrmPushInscricao> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmPushInscricoes");
        builder.Property(e => e.Endpoint).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.P256dh).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Auth).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Navegador).HasMaxLength(300);
        builder.HasIndex(e => e.Endpoint).IsUnique();
        builder.HasIndex(e => e.UsuarioId);
    }
}

public class CrmParametroConfiguration : IEntityTypeConfiguration<CrmParametro>
{
    public void Configure(EntityTypeBuilder<CrmParametro> builder)
    {
        builder.ToTable("CrmParametros");
        builder.HasKey(e => e.Chave);
        builder.Property(e => e.Chave).HasMaxLength(100);
        builder.Property(e => e.Valor).IsRequired();
    }
}
