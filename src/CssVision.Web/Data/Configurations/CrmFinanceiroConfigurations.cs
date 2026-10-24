using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmFinanceiroVendaConfiguration : IEntityTypeConfiguration<CrmFinanceiroVenda>
{
    public void Configure(EntityTypeBuilder<CrmFinanceiroVenda> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmFinanceiroVendas");
        builder.Property(e => e.Cliente).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Produto).HasMaxLength(40).IsRequired();
        builder.Property(e => e.StatusAdesao).HasMaxLength(20).IsRequired();
        builder.Property(e => e.StatusBonus).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Observacoes).HasMaxLength(1000);
        builder.Property(e => e.ValorAdesao).HasColumnType("numeric(14,2)");
        builder.Property(e => e.ValorBonus).HasColumnType("numeric(14,2)");
        builder.Property(e => e.BonusRecebido).HasColumnType("numeric(14,2)");
        builder.Property(e => e.Premiacoes).HasColumnType("numeric(14,2)");
        builder.HasIndex(e => new { e.UsuarioId, e.DataVenda });
        builder.HasIndex(e => new { e.UsuarioId, e.OportunidadeId }).IsUnique().HasFilter("\"OportunidadeId\" IS NOT NULL");
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmFinanceiroCustoConfiguration : IEntityTypeConfiguration<CrmFinanceiroCusto>
{
    public void Configure(EntityTypeBuilder<CrmFinanceiroCusto> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmFinanceiroCustos");
        builder.Property(e => e.Categoria).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Descricao).HasMaxLength(300);
        builder.Property(e => e.Status).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Valor).HasColumnType("numeric(14,2)");
        builder.HasIndex(e => new { e.UsuarioId, e.Data });
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmFinanceiroMetaConfiguration : IEntityTypeConfiguration<CrmFinanceiroMeta>
{
    public void Configure(EntityTypeBuilder<CrmFinanceiroMeta> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmFinanceiroMetas");
        builder.Property(e => e.MetaAdesao).HasColumnType("numeric(14,2)");
        builder.Property(e => e.MetaBonus).HasColumnType("numeric(14,2)");
        builder.Property(e => e.MetaRecorrencia).HasColumnType("numeric(14,2)");
        builder.Property(e => e.Observacoes).HasMaxLength(1000);
        builder.HasIndex(e => new { e.UsuarioId, e.Ano, e.Mes }).IsUnique();
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmFinanceiroConfigConfiguration : IEntityTypeConfiguration<CrmFinanceiroConfig>
{
    public void Configure(EntityTypeBuilder<CrmFinanceiroConfig> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmFinanceiroConfigs");
        builder.Property(e => e.PercentualReserva).HasColumnType("numeric(5,4)");
        builder.Property(e => e.CaixaInicial).HasColumnType("numeric(14,2)");
        builder.HasIndex(e => new { e.UsuarioId, e.Ano }).IsUnique();
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
