using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmSuporteChamadoConfiguration : IEntityTypeConfiguration<CrmSuporteChamado>
{
    public void Configure(EntityTypeBuilder<CrmSuporteChamado> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmSuporteChamados");
        builder.Property(e => e.Assunto).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Categoria).IsRequired().HasMaxLength(40);
        builder.HasIndex(e => e.SolicitanteId);
        builder.HasIndex(e => new { e.Status, e.UltimaMensagemEm });
        builder.HasMany(e => e.Mensagens).WithOne(m => m.Chamado).HasForeignKey(m => m.ChamadoId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmSuporteMensagemConfiguration : IEntityTypeConfiguration<CrmSuporteMensagem>
{
    public void Configure(EntityTypeBuilder<CrmSuporteMensagem> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmSuporteMensagens");
        builder.Property(e => e.Texto).IsRequired().HasMaxLength(4000);
        builder.HasIndex(e => e.ChamadoId);
    }
}
