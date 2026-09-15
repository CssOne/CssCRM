using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmNotionSyncCheckpointConfiguration : IEntityTypeConfiguration<CrmNotionSyncCheckpoint>
{
    public void Configure(EntityTypeBuilder<CrmNotionSyncCheckpoint> builder)
    {
        builder.ToTable("CrmNotionSyncCheckpoints");
        builder.HasKey(e => e.DataSourceId);
        builder.Property(e => e.DataSourceId).HasMaxLength(64);
        builder.Property(e => e.RegionalNome).IsRequired().HasMaxLength(80);
    }
}
