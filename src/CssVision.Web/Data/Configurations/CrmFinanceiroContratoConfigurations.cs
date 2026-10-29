using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmFinanceiroPerfilConfiguration : IEntityTypeConfiguration<CrmFinanceiroPerfil>
{
    public void Configure(EntityTypeBuilder<CrmFinanceiroPerfil> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmFinanceiroPerfis");
        builder.HasIndex(e => e.UsuarioId).IsUnique();
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmFinanceiroValorConfiguration : IEntityTypeConfiguration<CrmFinanceiroValor>
{
    public void Configure(EntityTypeBuilder<CrmFinanceiroValor> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmFinanceiroValores");
        builder.Property(e => e.Valor).HasColumnType("numeric(14,4)");
        builder.Property(e => e.Observacao).HasMaxLength(300);
        // Um valor por parâmetro e mês de vigência: lançar de novo no mesmo mês corrige o valor (a troca fica na auditoria).
        builder.HasIndex(e => new { e.UsuarioId, e.Parametro, e.VigenteDesde }).IsUnique();
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmFinanceiroPadraoConfiguration : IEntityTypeConfiguration<CrmFinanceiroPadrao>
{
    public void Configure(EntityTypeBuilder<CrmFinanceiroPadrao> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmFinanceiroPadroes");
        builder.Property(e => e.Valor).HasColumnType("numeric(14,4)");
        builder.HasIndex(e => e.Parametro).IsUnique();
    }
}

public class CrmFinanceiroPrazoConfiguration : IEntityTypeConfiguration<CrmFinanceiroPrazo>
{
    public void Configure(EntityTypeBuilder<CrmFinanceiroPrazo> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmFinanceiroPrazos");
        builder.HasIndex(e => new { e.UsuarioId, e.Parametro }).IsUnique();
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmCobrancaPagamentoConfiguration : IEntityTypeConfiguration<CrmCobrancaPagamento>
{
    public void Configure(EntityTypeBuilder<CrmCobrancaPagamento> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmCobrancasPagamento");
        builder.Property(e => e.Observacao).HasMaxLength(300);
        builder.Property(e => e.ObservacaoConsultor).HasMaxLength(300);
        builder.Property(e => e.ComprovanteUrl).HasMaxLength(500);
        builder.Property(e => e.MotivoRecusa).HasMaxLength(300);
        builder.Property(e => e.ValorAjustado).HasColumnType("numeric(14,2)");
        builder.HasIndex(e => e.LembreteEm).HasFilter("\"LembreteEm\" IS NOT NULL AND \"LembreteEnviadoEm\" IS NULL");
        // Uma situação por consultor, item e mês (marcar de novo corrige); os custos adicionais têm uma por parcela de cada custo.
        builder.HasIndex(e => new { e.UsuarioId, e.Item, e.Competencia }).IsUnique().HasFilter("\"CustoId\" IS NULL");
        builder.HasIndex(e => new { e.CustoId, e.Competencia }).IsUnique().HasFilter("\"CustoId\" IS NOT NULL");
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmCustoConsultorConfiguration : IEntityTypeConfiguration<CrmCustoConsultor>
{
    public void Configure(EntityTypeBuilder<CrmCustoConsultor> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmCustosConsultor");
        builder.Property(e => e.Descricao).HasMaxLength(200);
        builder.Property(e => e.ValorTotal).HasColumnType("numeric(14,2)");
        builder.HasIndex(e => e.UsuarioId);
        builder.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CrmCustoAvisoConfiguration : IEntityTypeConfiguration<CrmCustoAviso>
{
    public void Configure(EntityTypeBuilder<CrmCustoAviso> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmCustosAviso");
        builder.HasIndex(e => new { e.CustoId, e.Competencia, e.Momento }).IsUnique();
    }
}
