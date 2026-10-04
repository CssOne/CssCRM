using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmControleMarketingMesConfiguration : IEntityTypeConfiguration<CrmControleMarketingMes>
{
    public void Configure(EntityTypeBuilder<CrmControleMarketingMes> builder)
    {
        builder.ToTable("CrmControleMarketingMeses");
        builder.HasKey(e => e.Mes);
        builder.Property(e => e.FacebookAds).HasPrecision(14, 2);
        builder.Property(e => e.GoogleAds).HasPrecision(14, 2);
        builder.Property(e => e.FerramentasMarketing).HasPrecision(14, 2);
        builder.Property(e => e.Backlinks).HasPrecision(14, 2);
        builder.Property(e => e.FaturamentoTotal).HasPrecision(14, 2);
        builder.Property(e => e.MetaFaturamento).HasPrecision(14, 2);
    }
}
