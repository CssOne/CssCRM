using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CssVision.Web.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentUserService currentUser)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>Chaves do ASP.NET Data Protection (assinam o cookie de login) — ver AddDataProtection.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<CrmLead> CrmLeads => Set<CrmLead>();
    public DbSet<CrmOpportunity> CrmOpportunities => Set<CrmOpportunity>();
    public DbSet<CrmPipelineStage> CrmPipelineStages => Set<CrmPipelineStage>();
    public DbSet<CrmLeadStage> CrmLeadStages => Set<CrmLeadStage>();
    public DbSet<CrmStageHistory> CrmStageHistories => Set<CrmStageHistory>();
    public DbSet<CrmActivity> CrmActivities => Set<CrmActivity>();
    public DbSet<CrmNote> CrmNotes => Set<CrmNote>();
    public DbSet<CrmTag> CrmTags => Set<CrmTag>();
    public DbSet<CrmLeadTag> CrmLeadTags => Set<CrmLeadTag>();
    public DbSet<CrmLeadAssignmentHistory> CrmLeadAssignmentHistories => Set<CrmLeadAssignmentHistory>();
    public DbSet<CrmLossReason> CrmLossReasons => Set<CrmLossReason>();
    public DbSet<CrmSalesGoal> CrmSalesGoals => Set<CrmSalesGoal>();
    public DbSet<CrmFinanceiroVenda> CrmFinanceiroVendas => Set<CrmFinanceiroVenda>();
    public DbSet<CrmFinanceiroCusto> CrmFinanceiroCustos => Set<CrmFinanceiroCusto>();
    public DbSet<CrmFinanceiroMeta> CrmFinanceiroMetas => Set<CrmFinanceiroMeta>();
    public DbSet<CrmFinanceiroConfig> CrmFinanceiroConfigs => Set<CrmFinanceiroConfig>();
    public DbSet<CrmFinanceiroPerfil> CrmFinanceiroPerfis => Set<CrmFinanceiroPerfil>();
    public DbSet<CrmFinanceiroValor> CrmFinanceiroValores => Set<CrmFinanceiroValor>();
    public DbSet<CrmFinanceiroPadrao> CrmFinanceiroPadroes => Set<CrmFinanceiroPadrao>();
    public DbSet<CrmFinanceiroPrazo> CrmFinanceiroPrazos => Set<CrmFinanceiroPrazo>();
    public DbSet<CrmGrupoMembro> CrmGrupoMembros => Set<CrmGrupoMembro>();
    public DbSet<CrmGrupoContratoPerfil> CrmGrupoContratoPerfis => Set<CrmGrupoContratoPerfil>();
    public DbSet<CrmGrupoContratoValor> CrmGrupoContratoValores => Set<CrmGrupoContratoValor>();
    public DbSet<CrmGrupoContratoPrazo> CrmGrupoContratoPrazos => Set<CrmGrupoContratoPrazo>();
    public DbSet<CrmCobrancaPagamento> CrmCobrancasPagamento => Set<CrmCobrancaPagamento>();
    public DbSet<CrmCustoConsultor> CrmCustosConsultor => Set<CrmCustoConsultor>();
    public DbSet<CrmCustoAviso> CrmCustosAviso => Set<CrmCustoAviso>();
    public DbSet<CrmRegionalGoal> CrmRegionalGoals => Set<CrmRegionalGoal>();
    public DbSet<CrmAttachment> CrmAttachments => Set<CrmAttachment>();
    public DbSet<CrmVeiculo> CrmVeiculos => Set<CrmVeiculo>();
    public DbSet<CrmRegional> CrmRegionais => Set<CrmRegional>();
    public DbSet<CrmGrupo> CrmGrupos => Set<CrmGrupo>();
    public DbSet<CrmAnnouncement> CrmAnnouncements => Set<CrmAnnouncement>();
    public DbSet<CrmAuditLog> CrmAuditLogs => Set<CrmAuditLog>();
    public DbSet<CrmNotionSyncCheckpoint> CrmNotionSyncCheckpoints => Set<CrmNotionSyncCheckpoint>();
    public DbSet<CrmControleMarketingMes> CrmControleMarketingMeses => Set<CrmControleMarketingMes>();
    public DbSet<CrmUsuarioAlias> CrmUsuarioAliases => Set<CrmUsuarioAlias>();
    public DbSet<CrmBackup> CrmBackups => Set<CrmBackup>();
    public DbSet<CrmSuporteChamado> CrmSuporteChamados => Set<CrmSuporteChamado>();
    public DbSet<CrmAvisoConsultor> CrmAvisosConsultor => Set<CrmAvisoConsultor>();
    public DbSet<CrmContaEmail> CrmContasEmail => Set<CrmContaEmail>();
    public DbSet<CrmDisparoEmail> CrmDisparosEmail => Set<CrmDisparoEmail>();
    public DbSet<CrmDisparoEmailDestinatario> CrmDisparosEmailDestinatarios => Set<CrmDisparoEmailDestinatario>();
    public DbSet<CrmSuporteMensagem> CrmSuporteMensagens => Set<CrmSuporteMensagem>();
    public DbSet<CrmPushInscricao> CrmPushInscricoes => Set<CrmPushInscricao>();
    public DbSet<CrmParametro> CrmParametros => Set<CrmParametro>();
    public DbSet<CrmEsperaLead> CrmEsperasLead => Set<CrmEsperaLead>();
    public DbSet<CrmDiscordVinculo> CrmDiscordVinculos => Set<CrmDiscordVinculo>();
    public DbSet<CrmDiscordCanal> CrmDiscordCanais => Set<CrmDiscordCanal>();
    public DbSet<CrmDiscordReacao> CrmDiscordReacoes => Set<CrmDiscordReacao>();
    public DbSet<CrmDiscordConversa> CrmDiscordConversas => Set<CrmDiscordConversa>();
    public DbSet<CrmDiscordLeitura> CrmDiscordLeituras => Set<CrmDiscordLeitura>();

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
        TravaDasColunasDeVendaConcluidaAsync(CancellationToken.None).GetAwaiter().GetResult();
        RegistrarTrocaDeGrupoAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AplicarAuditoria();
        await TravaDasColunasDeVendaConcluidaAsync(cancellationToken);
        await RegistrarTrocaDeGrupoAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Histórico de grupo do consultor: qualquer caminho que crie um usuário já em um grupo, ou mude ou tire o grupo dele (tela de usuários, tela de grupos,
    /// sincronização do Notion), grava uma linha no histórico a partir do mês atual. O Controle financeiro cobra cada mês pelo grupo em que o consultor estava.
    /// Trocar duas vezes no mesmo mês ajusta a linha do mês em vez de criar outra.
    /// </summary>
    private async Task RegistrarTrocaDeGrupoAsync(CancellationToken ct)
    {
        var trocas = ChangeTracker.Entries<ApplicationUser>()
            .Where(e => e.State == EntityState.Added
                ? e.Entity.GrupoId is not null
                : e.State == EntityState.Modified && e.Property(u => u.GrupoId).IsModified && !Equals(e.Property(u => u.GrupoId).OriginalValue, e.Entity.GrupoId))
            .Select(e => (Usuario: e.Entity, Novo: e.State == EntityState.Added))
            .ToList();
        if (trocas.Count == 0) return;

        var mes = new DateOnly(Services.Crm.HorarioBrasilia.Hoje.Year, Services.Crm.HorarioBrasilia.Hoje.Month, 1);
        var ids = trocas.Where(t => !t.Novo).Select(t => t.Usuario.Id).ToList();
        var historico = ids.Count == 0
            ? new Dictionary<Guid, CrmGrupoMembro>()
            : (await CrmGrupoMembros.Where(m => ids.Contains(m.UsuarioId)).ToListAsync(ct))
                .GroupBy(m => m.UsuarioId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.DesdeMes).Last());

        foreach (var (usuario, novo) in trocas)
        {
            if (!novo && historico.TryGetValue(usuario.Id, out var ultimo))
            {
                if (ultimo.GrupoId == usuario.GrupoId) continue;
                if (ultimo.DesdeMes >= mes) { ultimo.GrupoId = usuario.GrupoId; continue; }
            }
            else if (!novo && usuario.GrupoId is null) continue;
            CrmGrupoMembros.Add(new CrmGrupoMembro { Usuario = usuario, GrupoId = usuario.GrupoId, DesdeMes = mes });
        }
    }

    private const string ColunaVendaConcluidaLeads = "Venda concluída (Leads)";
    private const string ColunaVendaConcluidaIndicacao = "Venda concluída (Indicação)";
    private (Guid Leads, Guid Indicacao)? _colunasDeVenda;

    /// <summary>
    /// Trava das colunas "Venda concluída (Leads)" e "Venda concluída (Indicação)": nenhum caminho (tela, API, sincronização do Notion, carga, edição do
    /// canal) pode deixar um card de Indicação na coluna dos Leads nem um card Lead na das Indicações. Se um card for gravado na coluna que não é a da etiqueta
    /// dele, vai para a coluna da etiqueta — a etiqueta nunca é trocada aqui (a exceção "Lead que vendeu por indicação" ganha a etiqueta "Indicação Lead" antes,
    /// em MudarEtapa/concluir venda).
    /// </summary>
    private async Task TravaDasColunasDeVendaConcluidaAsync(CancellationToken ct)
    {
        var candidatos = ChangeTracker.Entries<CrmLead>()
            .Where(e => e.State == EntityState.Added
                || (e.State == EntityState.Modified && (e.Property(l => l.EtapaId).IsModified || e.Property(l => l.TipoIndicacao).IsModified || e.Property(l => l.CriadoManualmente).IsModified)))
            .Where(e => e.Entity.EtapaId is not null)
            .ToList();
        if (candidatos.Count == 0) return;

        if (_colunasDeVenda is null)
        {
            var colunas = await CrmLeadStages.AsNoTracking().Where(s => s.Nome == ColunaVendaConcluidaLeads || s.Nome == ColunaVendaConcluidaIndicacao)
                .Select(s => new { s.Id, s.Nome }).ToListAsync(ct);
            var leads = colunas.FirstOrDefault(c => c.Nome == ColunaVendaConcluidaLeads);
            var indicacao = colunas.FirstOrDefault(c => c.Nome == ColunaVendaConcluidaIndicacao);
            if (leads is null || indicacao is null) return;
            _colunasDeVenda = (leads.Id, indicacao.Id);
        }

        var (idLeads, idIndicacao) = _colunasDeVenda.Value;
        foreach (var entry in candidatos)
        {
            var lead = entry.Entity;
            if (lead.EtapaId != idLeads && lead.EtapaId != idIndicacao) continue;
            lead.EtapaId = Services.Notion.NotionEtapaLead.EhIndicacao(lead.CriadoManualmente, lead.TipoIndicacao) ? idIndicacao : idLeads;
        }
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
                    // Mantém a data informada (importações); antes, toda data de chegada vinda do
                    // Notion era trocada pelo momento da gravação.
                    if (entry.Entity.CriadoEm == default) entry.Entity.CriadoEm = agora;
                    entry.Entity.CriadoPorId = userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.AtualizadoEm = agora;
                    entry.Entity.AtualizadoPorId = userId;
                    break;
            }
        }

        // Lead que ganhou responsável (criado já atribuído, distribuído ou transferido): marca o momento.
        foreach (var entry in ChangeTracker.Entries<CrmLead>())
        {
            var responsavel = entry.Property(l => l.ResponsavelId);
            var ganhouResponsavel = entry.State == EntityState.Added
                ? entry.Entity.ResponsavelId is not null
                : entry.State == EntityState.Modified && responsavel.IsModified && entry.Entity.ResponsavelId is not null
                    && !Equals(responsavel.OriginalValue, responsavel.CurrentValue);
            if (ganhouResponsavel) entry.Entity.ResponsavelAtribuidoEm = agora;
        }
    }
}
