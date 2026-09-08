using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmPipelineStageConfiguration : IEntityTypeConfiguration<CrmPipelineStage>
{
    public void Configure(EntityTypeBuilder<CrmPipelineStage> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmPipelineStages");

        builder.Property(e => e.Nome).IsRequired().HasMaxLength(80);
        builder.Property(e => e.Cor).HasMaxLength(9);

        builder.HasIndex(e => e.Nome).IsUnique();
        builder.HasIndex(e => e.Ordem);
    }
}

public class CrmLossReasonConfiguration : IEntityTypeConfiguration<CrmLossReason>
{
    public void Configure(EntityTypeBuilder<CrmLossReason> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmLossReasons");

        builder.Property(e => e.Descricao).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => e.Descricao).IsUnique();
    }
}

public class CrmStageHistoryConfiguration : IEntityTypeConfiguration<CrmStageHistory>
{
    public void Configure(EntityTypeBuilder<CrmStageHistory> builder)
    {
        builder.HasKey(e => e.Id);
        builder.ToTable("CrmStageHistories");

        builder.Property(e => e.MotivoPerdaDescricao).HasMaxLength(200);

        builder.HasIndex(e => e.OpportunityId);
        builder.HasIndex(e => e.AlteradoEm);

        builder.HasOne(e => e.Opportunity)
            .WithMany(o => o.HistoricoEtapas)
            .HasForeignKey(e => e.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.EtapaAnterior)
            .WithMany()
            .HasForeignKey(e => e.EtapaAnteriorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EtapaNova)
            .WithMany()
            .HasForeignKey(e => e.EtapaNovaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Usuario)
            .WithMany()
            .HasForeignKey(e => e.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
