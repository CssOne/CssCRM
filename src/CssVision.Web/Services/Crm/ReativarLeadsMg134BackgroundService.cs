using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Etapa 1 da volta da MG134 ao CRM: desarquiva os leads da regional que o sistema arquivou nas migrations de 04 e 05/10/2026
/// (ArquivaLeadsMg134 e ArquivaLeadsMg134Externos). Roda <b>uma única vez</b>, depois que o app já está no ar — e não numa migration,
/// que roda antes de o app abrir a porta: uma consulta travada ali deixaria o CRM inteiro fora do ar (foi o que aconteceu no primeiro
/// deploy desta volta). Aqui, qualquer falha só vai para o log e o app segue normalmente.
/// Só volta o que o sistema arquivou: exclusão feita por uma pessoa grava ArquivadoPorId (LeadService.ExcluirAsync) e fica como está.
/// A sincronização com o Notion da MG134 NÃO volta nesta etapa.
/// </summary>
public sealed class ReativarLeadsMg134BackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReativarLeadsMg134BackgroundService> logger) : BackgroundService
{
    /// <summary>Parâmetro gravado quando a reativação termina — impede de rodar de novo (e de mexer em exclusões futuras).</summary>
    public const string ChaveExecutado = "mg134:leads-reativados-em";

    /// <summary>As migrations que arquivaram os leads rodaram em 04 e 05/10/2026; nada arquivado pelo sistema antes disso é tocado.</summary>
    private static readonly DateTimeOffset ArquivadosAPartirDe = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LimiteDaConsulta = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Primeiro await de verdade: devolve o controle na hora, então não atrasa a subida do app, e dá tempo de o app
            // terminar de subir antes de mexer no banco.
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(LimiteDaConsulta);

            var reativados = await ExecutarAsync(db, stoppingToken);
            if (reativados is { } n)
            {
                logger.LogInformation("MG134: {Quantidade} lead(s) desarquivado(s) (etapa 1 da volta ao CRM).", n);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao desarquivar os leads da MG134 — o app segue normalmente; tenta de novo na próxima inicialização.");
        }
    }

    /// <returns>Quantos leads voltaram, ou <c>null</c> se já tinha rodado antes.</returns>
    public static async Task<int?> ExecutarAsync(ApplicationDbContext db, CancellationToken ct)
    {
        if (await db.CrmParametros.AnyAsync(p => p.Chave == ChaveExecutado, ct)) return null;

        var reativados = await db.CrmLeads
            .Where(l => l.Regional == "MG134" && l.Arquivado && l.ArquivadoPorId == null && l.ArquivadoEm >= ArquivadosAPartirDe)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Arquivado, false)
                .SetProperty(l => l.ArquivadoEm, (DateTimeOffset?)null), ct);

        db.CrmParametros.Add(new CrmParametro { Chave = ChaveExecutado, Valor = $"{DateTimeOffset.UtcNow:O} | {reativados} lead(s)" });
        await db.SaveChangesAsync(ct);
        return reativados;
    }
}
