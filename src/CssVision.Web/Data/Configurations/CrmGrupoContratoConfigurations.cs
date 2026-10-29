using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmGrupoMembroConfiguration : IEntityTypeConfiguration<CrmGrupoMembro>
{
    public void Configure(EntityTypeBuilder<CrmGrupoMembro> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmGrupoMembros");
        builder.HasIndex(e => new { e.UsuarioId, e.DesdeMes }).IsUnique();
        builder.HasIndex(e => e.GrupoId);
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        // Grupo nunca é excluído (só desativado): o histórico não pode perder a referência.
        builder.HasOne(e => e.Grupo).WithMany().HasForeignKey(e => e.GrupoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CrmGrupoContratoPerfilConfiguration : IEntityTypeConfiguration<CrmGrupoContratoPerfil>
{
    public void Configure(EntityTypeBuilder<CrmGrupoContratoPerfil> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmGrupoContratoPerfis");
        builder.HasIndex(e => new { e.GrupoId, e.VigenteDesde }).IsUnique();
        builder.HasOne(e => e.Grupo).WithMany().HasForeignKey(e => e.GrupoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CrmGrupoContratoValorConfiguration : IEntityTypeConfiguration<CrmGrupoContratoValor>
{
    public void Configure(EntityTypeBuilder<CrmGrupoContratoValor> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmGrupoContratoValores");
        builder.Property(e => e.Valor).HasColumnType("numeric(14,4)");
        builder.Property(e => e.Observacao).HasMaxLength(300);
        builder.HasIndex(e => new { e.GrupoId, e.Parametro, e.VigenteDesde }).IsUnique();
        builder.HasOne(e => e.Grupo).WithMany().HasForeignKey(e => e.GrupoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CrmGrupoContratoPrazoConfiguration : IEntityTypeConfiguration<CrmGrupoContratoPrazo>
{
    public void Configure(EntityTypeBuilder<CrmGrupoContratoPrazo> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmGrupoContratoPrazos");
        builder.HasIndex(e => new { e.GrupoId, e.Parametro }).IsUnique();
        builder.HasOne(e => e.Grupo).WithMany().HasForeignKey(e => e.GrupoId).OnDelete(DeleteBehavior.Restrict);
    }
}
