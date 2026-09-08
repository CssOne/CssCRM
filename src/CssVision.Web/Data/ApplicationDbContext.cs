using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CssVision.Web.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentUserService currentUser)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<CrmLead> CrmLeads => Set<CrmLead>();
    public DbSet<CrmOpportunity> CrmOpportunities => Set<CrmOpportunity>();
    public DbSet<CrmPipelineStage> CrmPipelineStages => Set<CrmPipelineStage>();
    public DbSet<CrmStageHistory> CrmStageHistories => Set<CrmStageHistory>();
    public DbSet<CrmActivity> CrmActivities => Set<CrmActivity>();
    public DbSet<CrmNote> CrmNotes => Set<CrmNote>();
    public DbSet<CrmTag> CrmTags => Set<CrmTag>();
    public DbSet<CrmLeadTag> CrmLeadTags => Set<CrmLeadTag>();
    public DbSet<CrmLeadAssignmentHistory> CrmLeadAssignmentHistories => Set<CrmLeadAssignmentHistory>();
    public DbSet<CrmLossReason> CrmLossReasons => Set<CrmLossReason>();
    public DbSet<CrmSalesGoal> CrmSalesGoals => Set<CrmSalesGoal>();
    public DbSet<CrmAttachment> CrmAttachments => Set<CrmAttachment>();
    public DbSet<CrmAuditLog> CrmAuditLogs => Set<CrmAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        if (!Database.IsNpgsql())
        {
            // "xmin" é uma coluna de sistema exclusiva do PostgreSQL usada como token de
            // concorrência otimista (ver CrmEntityConfigurationExtensions). Em outros provedores
            // (ex: SQLite nos testes automatizados) ela não existe automaticamente, então
            // tratamos RowVersion como uma coluna comum com valor padrão — a concorrência
            // otimista real só é exercitada em produção, contra o PostgreSQL.
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                if (entityType.FindProperty(nameof(Domain.Crm.CrmEntityBase.RowVersion)) is null) continue;

                builder.Entity(entityType.ClrType)
                    .Property<uint>(nameof(Domain.Crm.CrmEntityBase.RowVersion))
                    .ValueGeneratedOnAdd()
                    .HasDefaultValue(0u);
            }

            // O provedor SQLite (usado nos testes automatizados) recusa-se a traduzir ORDER BY
            // sobre DateTimeOffset, pois em geral o deslocamento (offset) pode variar por linha e
            // a ordenação textual ficaria incorreta. Como todo DateTimeOffset desta aplicação é
            // sempre gravado em UTC, convertê-lo para DateTime aqui (somente fora do Postgres) é
            // seguro e destrava a tradução das consultas nos testes, sem afetar a produção.
            var conversorOffset = new ValueConverter<DateTimeOffset, DateTime>(
                v => v.UtcDateTime,
                v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            var conversorOffsetNulo = new ValueConverter<DateTimeOffset?, DateTime?>(
                v => v.HasValue ? v.Value.UtcDateTime : null,
                v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);

            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset)) property.SetValueConverter(conversorOffset);
                    else if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(conversorOffsetNulo);
                }
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AplicarAuditoria();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AplicarAuditoria();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void AplicarAuditoria()
    {
        var agora = DateTimeOffset.UtcNow;
        var userId = currentUser.IsAuthenticated ? currentUser.UserId : (Guid?)null;

        foreach (var entry in ChangeTracker.Entries<CrmEntityBase>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CriadoEm = agora;
                    entry.Entity.CriadoPorId = userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.AtualizadoEm = agora;
                    entry.Entity.AtualizadoPorId = userId;
                    break;
            }
        }
    }
}
