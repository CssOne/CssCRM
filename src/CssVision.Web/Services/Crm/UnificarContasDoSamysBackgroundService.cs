using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Carga única: o Samys Alexandre tinha duas contas, a de consultor (MG132) e a de Administrador. Tudo o que estava na de consultor (leads, vendas, atividades,
/// anotações, histórico, metas, vistorias...) passa para a de Administrador, que fica como a única conta dele e deixa de aparecer nos rankings, na TV e na Gestão
/// comercial (não atua nas vendas). A conta de consultor é excluída. Os vínculos são movidos olhando as chaves estrangeiras do próprio modelo, numa transação só: ou
/// move tudo ou não move nada. Roda uma vez (marcador em CrmParametros); falha só vai para o log.
/// </summary>
public sealed class UnificarContasDoSamysBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<UnificarContasDoSamysBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "crm:contas-samys-unificadas-em";
    public const string ChaveStatus = "notion:contas-samys-unificadas";
    public const string EmailConsultor = "samys.alexandre@gmail.com";
    public const string EmailAdministrador = "samys.financeiro@gmail.com";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(4);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));

            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var resumo = await ExecutarAsync(db, userManager, stoppingToken);
            var linha = $"{DateTimeOffset.UtcNow:O} | {resumo}";
            await GravarAsync(db, ChaveStatus, linha, stoppingToken);
            await GravarAsync(db, ChaveExecutado, linha, stoppingToken);
            logger.LogInformation("Contas do Samys unificadas: {Resumo}", resumo);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao unificar as contas do Samys — nada foi movido; tenta de novo na próxima inicialização.");
        }
    }

    public static async Task<string> ExecutarAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, CancellationToken ct)
    {
        var consultor = await db.Users.FirstOrDefaultAsync(u => u.Email!.ToLower() == EmailConsultor, ct);
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email!.ToLower() == EmailAdministrador, ct);
        if (consultor is null || admin is null || consultor.Id == admin.Id) return "contas não encontradas; nada feito";

        var movidos = 0;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var (tabela, coluna) in ReferenciasAUsuarios(db))
            {
                movidos += await db.Database.ExecuteSqlRawAsync(
                    $"UPDATE \"{tabela}\" SET \"{coluna}\" = {{0}} WHERE \"{coluna}\" = {{1}}", [admin.Id, consultor.Id], ct);
            }

            // A conta que sobra é a do Administrador: não atua nas vendas (fora de rankings, TV e Gestão comercial) e herda o vínculo com o Notion.
            admin.AtuaNasVendas = false;
            admin.RecebeLeads = false;
            admin.NotionUserId ??= consultor.NotionUserId;
            consultor.NotionUserId = null;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        db.ChangeTracker.Clear();
        var antiga = await db.Users.FirstAsync(u => u.Id == consultor.Id, ct);
        var excluida = false;
        try
        {
            excluida = (await userManager.DeleteAsync(antiga)).Succeeded;
        }
        catch (DbUpdateException)
        {
        }

        if (!excluida)
        {
            db.ChangeTracker.Clear();
            antiga = await db.Users.FirstAsync(u => u.Id == consultor.Id, ct);
            var anonimo = $"{antiga.Id:N}{UserManagementService.SufixoEmailExcluido}";
            antiga.Ativo = false;
            antiga.RecebeLeads = false;
            antiga.LockoutEnabled = true;
            antiga.LockoutEnd = DateTimeOffset.MaxValue;
            await userManager.SetUserNameAsync(antiga, anonimo);
            await userManager.SetEmailAsync(antiga, anonimo);
        }

        return $"vínculos movidos para a conta do Administrador: {movidos}; conta de consultor {(excluida ? "excluída" : "desativada (ainda tem registros ligados)")}";
    }

    /// <summary>Todas as colunas do modelo que apontam para um usuário (menos as tabelas do login: papéis, claims...).</summary>
    private static IEnumerable<(string Tabela, string Coluna)> ReferenciasAUsuarios(ApplicationDbContext db)
    {
        foreach (var entidade in db.Model.GetEntityTypes())
        {
            if (entidade.ClrType.Namespace?.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) == true) continue;
            var tabela = entidade.GetTableName();
            if (tabela is null) continue;
            var objeto = StoreObjectIdentifier.Table(tabela, entidade.GetSchema());
            foreach (var fk in entidade.GetForeignKeys())
            {
                if (fk.PrincipalEntityType.ClrType != typeof(ApplicationUser) || fk.Properties.Count != 1) continue;
                var coluna = fk.Properties[0].GetColumnName(objeto);
                if (coluna is not null) yield return (tabela, coluna);
            }
        }
    }

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
