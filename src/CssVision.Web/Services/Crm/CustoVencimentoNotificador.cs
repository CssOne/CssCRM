using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>Avisa o consultor das parcelas dos custos que o financeiro lançou para ele: alguns dias antes do vencimento e no dia.</summary>
public interface ICustoVencimentoNotificador
{
    /// <returns>Quantos avisos foram enviados.</returns>
    Task<int> NotificarAsync(DateOnly hoje, CancellationToken ct);
}

public sealed class CustoVencimentoNotificador(ApplicationDbContext db, IPushService push, ILogger<CustoVencimentoNotificador> logger, ICrmEventHub? eventos = null) : ICustoVencimentoNotificador
{
    /// <summary>Quantos dias antes do vencimento sai o primeiro aviso.</summary>
    public const int DiasDeAntecedencia = 3;

    public async Task<int> NotificarAsync(DateOnly hoje, CancellationToken ct)
    {
        var custos = await db.CrmCustosConsultor.AsNoTracking()
            .Where(c => db.Users.Any(u => u.Id == c.UsuarioId && u.Ativo))
            .ToListAsync(ct);
        if (custos.Count == 0) return 0;

        var ids = custos.Select(c => c.Id).ToList();
        var pagos = (await db.CrmCobrancasPagamento.AsNoTracking().Where(p => p.CustoId != null && ids.Contains(p.CustoId.Value) && p.Pago).ToListAsync(ct))
            .Select(p => (p.CustoId!.Value, p.Competencia)).ToHashSet();
        var avisados = (await db.CrmCustosAviso.AsNoTracking().Where(a => ids.Contains(a.CustoId)).ToListAsync(ct))
            .Select(a => (a.CustoId, a.Competencia, a.Momento)).ToHashSet();

        var enviados = 0;
        foreach (var custo in custos)
        {
            for (var n = 1; n <= custo.Parcelas; n++)
            {
                var vencimento = CobrancasCalculo.VencimentoDaParcela(custo, n);
                var dias = vencimento.DayNumber - hoje.DayNumber;
                if (dias < 0) continue;
                if (dias > DiasDeAntecedencia) break;   // as parcelas seguintes vencem ainda mais tarde

                var competencia = ContratoFinanceiroCalculo.PrimeiroDia(vencimento);
                var momento = dias == 0 ? MomentoAvisoCusto.Vencimento : MomentoAvisoCusto.Antecedencia;
                if (pagos.Contains((custo.Id, competencia)) || avisados.Contains((custo.Id, competencia, momento))) continue;

                var nome = string.IsNullOrWhiteSpace(custo.Descricao) ? CobrancasCalculo.RotuloDoTipo(custo.Tipo) : custo.Descricao;
                var valor = CobrancasCalculo.ValorDaParcela(custo, n);
                var quando = dias == 0 ? "vence hoje" : dias == 1 ? "vence amanhã" : $"vence em {dias} dias ({vencimento:dd/MM/yyyy})";
                var aviso = new CrmAvisoConsultor
                {
                    ConsultorId = custo.UsuarioId,
                    Titulo = dias == 0 ? "Parcela vence hoje" : "Parcela a vencer",
                    Mensagem = $"A parcela {n}/{custo.Parcelas} de {nome} {quando}.",
                    Valor = valor,
                    Referencia = CobrancasCalculo.RotuloDoTipo(custo.Tipo),
                    EnviadoPorId = custo.CriadoPorId,
                };
                db.CrmAvisosConsultor.Add(aviso);
                db.CrmCustosAviso.Add(new CrmCustoAviso { CustoId = custo.Id, Competencia = competencia, Momento = momento });
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Outra cópia do sistema gravou este mesmo aviso um instante antes (índice único): o aviso dela vale, este é descartado e o ciclo segue.
                    db.ChangeTracker.Clear();
                    avisados.Add((custo.Id, competencia, momento));
                    continue;
                }
                avisados.Add((custo.Id, competencia, momento));
                enviados++;

                try
                {
                    await push.EnviarAsync(custo.UsuarioId, new PushMensagem(aviso.Titulo, $"{aviso.Mensagem} · R$ {valor:N2}", "/app/portal", $"custo-{custo.Id}-{competencia:yyyyMM}-{(int)momento}"), ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Falha ao enviar o push da parcela {N} do custo {CustoId} ao consultor {ConsultorId}.", n, custo.Id, custo.UsuarioId);
                }
            }
        }

        if (enviados > 0) eventos?.PublicarQuadroAtualizado("avisos");
        return enviados;
    }
}

/// <summary>Confere a cada meia hora se há parcela de custo de consultor vencendo (cada aviso sai uma vez só).</summary>
public sealed class CustoVencimentoBackgroundService(IServiceScopeFactory scopeFactory, ILogger<CustoVencimentoBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                await using var escopo = scopeFactory.CreateAsyncScope();
                var enviados = await escopo.ServiceProvider.GetRequiredService<ICustoVencimentoNotificador>().NotificarAsync(HorarioBrasilia.Hoje, stoppingToken);
                if (enviados > 0) logger.LogInformation("{Enviados} aviso(s) de parcela de custo enviados.", enviados);
                var lembretes = await escopo.ServiceProvider.GetRequiredService<ILembreteCobrancaNotificador>().NotificarAsync(HorarioBrasilia.Hoje, stoppingToken);
                if (lembretes > 0) logger.LogInformation("{Lembretes} lembrete(s) de pagamento de cobrança enviados.", lembretes);
                var seguros = await escopo.ServiceProvider.GetRequiredService<ISeguroVencimentoNotificador>().NotificarAsync(HorarioBrasilia.Hoje, stoppingToken);
                if (seguros > 0) logger.LogInformation("{Seguros} aviso(s) de vencimento de seguro enviados.", seguros);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha nos avisos de vencimento das parcelas de custo — tenta de novo no próximo ciclo.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
