using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Carga única: o Canal de Aquisição "Pessoal" passou a se chamar "Indicação pessoal". Leads e vendas já gravados com o nome antigo (qualquer caixa) são renomeados.
/// Feita aqui e não numa migration: uma consulta travada na migration deixaria o CRM fora do ar (aconteceu na volta da MG134); aqui, qualquer falha só vai para o log
/// e tenta de novo na próxima inicialização, enquanto o marcador não existir.
/// </summary>
public sealed class RenomearCanalPessoalBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<RenomearCanalPessoalBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "crm:canal-pessoal-renomeado-em";
    public const string ChaveStatus = "notion:canal-pessoal-renomeado";

    public const string NomeAntigo = "pessoal";
    public const string NomeNovo = "Indicação pessoal";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));

            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var (leads, vendas) = await ExecutarAsync(db, stoppingToken);
            var resumo = $"{DateTimeOffset.UtcNow:O} | canal \"Pessoal\" renomeado para \"{NomeNovo}\": {leads} lead(s) e {vendas} venda(s)";
            await GravarAsync(db, ChaveStatus, resumo, stoppingToken);
            await GravarAsync(db, ChaveExecutado, resumo, stoppingToken);
            logger.LogInformation("Canal Pessoal renomeado: {Leads} lead(s), {Vendas} venda(s).", leads, vendas);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao renomear o canal Pessoal — o app segue; tenta de novo na próxima inicialização.");
        }
    }

    public static async Task<(int Leads, int Vendas)> ExecutarAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var leads = await db.CrmLeads
            .Where(l => l.TipoIndicacao != null && l.TipoIndicacao.Trim().ToLower() == NomeAntigo)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.TipoIndicacao, NomeNovo), ct);
        var vendas = await db.CrmOpportunities
            .Where(o => o.TipoIndicacao != null && o.TipoIndicacao.Trim().ToLower() == NomeAntigo)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.TipoIndicacao, NomeNovo), ct);
        return (leads, vendas);
    }

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
