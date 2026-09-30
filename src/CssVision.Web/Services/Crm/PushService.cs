using System.Net;
using System.Text.Json;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace CssVision.Web.Services.Crm;

public record PushInscricaoRequest(string Endpoint, string P256dh, string Auth, string? Navegador);

/// <summary>O que o service worker (wwwroot/sw-push.js) mostra na notificação.</summary>
public record PushMensagem(string Titulo, string Corpo, string Url, string Tag);

public interface IPushService
{
    Task<string> ChavePublicaAsync(CancellationToken ct);
    Task InscreverAsync(Guid usuarioId, PushInscricaoRequest request, CancellationToken ct);
    Task RemoverAsync(Guid usuarioId, string endpoint, CancellationToken ct);
    /// <returns>Quantos navegadores receberam.</returns>
    Task<int> EnviarAsync(Guid usuarioId, PushMensagem mensagem, CancellationToken ct);
}

/// <summary>
/// Notificações push (Web Push/VAPID): chegam ao sistema operacional mesmo com o CRM fechado,
/// enquanto o navegador estiver aberto. As chaves VAPID são geradas no primeiro uso e guardadas no
/// banco (CrmParametros) — nenhuma configuração extra no servidor.
/// </summary>
public sealed class PushService(ApplicationDbContext db, ILogger<PushService> logger) : IPushService
{
    private const string ChavePublica = "push:vapid:public";
    private const string ChavePrivada = "push:vapid:private";
    private const string Assunto = "mailto:suporte@cssbrasil.com.br";

    private static readonly WebPushClient Cliente = new();
    private static readonly SemaphoreSlim GerandoChaves = new(1, 1);

    public async Task<string> ChavePublicaAsync(CancellationToken ct) => (await ChavesAsync(ct)).Publica;

    private async Task<(string Publica, string Privada)> ChavesAsync(CancellationToken ct)
    {
        var publica = await db.CrmParametros.AsNoTracking().Where(p => p.Chave == ChavePublica).Select(p => p.Valor).FirstOrDefaultAsync(ct);
        var privada = await db.CrmParametros.AsNoTracking().Where(p => p.Chave == ChavePrivada).Select(p => p.Valor).FirstOrDefaultAsync(ct);
        if (publica is not null && privada is not null) return (publica, privada);

        await GerandoChaves.WaitAsync(ct);
        try
        {
            publica = await db.CrmParametros.AsNoTracking().Where(p => p.Chave == ChavePublica).Select(p => p.Valor).FirstOrDefaultAsync(ct);
            privada = await db.CrmParametros.AsNoTracking().Where(p => p.Chave == ChavePrivada).Select(p => p.Valor).FirstOrDefaultAsync(ct);
            if (publica is not null && privada is not null) return (publica, privada);

            var novas = VapidHelper.GenerateVapidKeys();
            db.CrmParametros.AddRange(
                new CrmParametro { Chave = ChavePublica, Valor = novas.PublicKey },
                new CrmParametro { Chave = ChavePrivada, Valor = novas.PrivateKey });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Chaves VAPID do Web Push geradas.");
            return (novas.PublicKey, novas.PrivateKey);
        }
        finally
        {
            GerandoChaves.Release();
        }
    }

    public async Task InscreverAsync(Guid usuarioId, PushInscricaoRequest request, CancellationToken ct)
    {
        var existente = await db.CrmPushInscricoes.FirstOrDefaultAsync(i => i.Endpoint == request.Endpoint, ct);
        var inscricao = existente ?? new CrmPushInscricao();
        // O mesmo navegador pode passar a ser de outra pessoa (outro login): fica com quem inscreveu por último.
        inscricao.UsuarioId = usuarioId;
        inscricao.Endpoint = request.Endpoint;
        inscricao.P256dh = request.P256dh;
        inscricao.Auth = request.Auth;
        inscricao.Navegador = request.Navegador is { Length: > 300 } n ? n[..300] : request.Navegador;
        if (existente is null) db.CrmPushInscricoes.Add(inscricao);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync(Guid usuarioId, string endpoint, CancellationToken ct)
    {
        var inscricao = await db.CrmPushInscricoes.FirstOrDefaultAsync(i => i.Endpoint == endpoint && i.UsuarioId == usuarioId, ct);
        if (inscricao is null) return;
        db.CrmPushInscricoes.Remove(inscricao);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> EnviarAsync(Guid usuarioId, PushMensagem mensagem, CancellationToken ct)
    {
        var inscricoes = await db.CrmPushInscricoes.Where(i => i.UsuarioId == usuarioId).ToListAsync(ct);
        if (inscricoes.Count == 0) return 0;

        var (publica, privada) = await ChavesAsync(ct);
        var vapid = new VapidDetails(Assunto, publica, privada);
        var conteudo = JsonSerializer.Serialize(mensagem, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var enviados = 0;

        foreach (var inscricao in inscricoes)
        {
            try
            {
                await Cliente.SendNotificationAsync(new PushSubscription(inscricao.Endpoint, inscricao.P256dh, inscricao.Auth), conteudo, vapid, ct);
                inscricao.UltimoEnvioEm = DateTimeOffset.UtcNow;
                enviados++;
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                // Navegador cancelou a inscrição (desinstalou, limpou dados): não tenta mais.
                db.CrmPushInscricoes.Remove(inscricao);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao enviar push para o usuário {UsuarioId}.", usuarioId);
            }
        }

        await db.SaveChangesAsync(ct);
        return enviados;
    }
}

/// <summary>
/// A cada poucos segundos, avisa por push quem recebeu lead novo — pelo mesmo critério do aviso
/// dentro do CRM (LeadAssignmentService.NovosLeadsAsync): lead atribuído a alguém, que não foi a
/// própria pessoa que cadastrou/pegou. Só olha para quem tem navegador inscrito.
/// </summary>
public sealed class PushNovosLeadsBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PushNovosLeadsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var desde = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(Intervalo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                desde = await VerificarAsync(desde, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao verificar leads novos para notificação push.");
            }
        }
    }

    public async Task<DateTimeOffset> VerificarAsync(DateTimeOffset desde, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var push = scope.ServiceProvider.GetRequiredService<IPushService>();
        var agora = DateTimeOffset.UtcNow;

        var inscritos = await db.CrmPushInscricoes.AsNoTracking().Select(i => i.UsuarioId).Distinct().ToListAsync(ct);
        if (inscritos.Count == 0) return agora;

        var leads = await db.CrmLeads.AsNoTracking()
            .Where(l => l.ResponsavelId != null && inscritos.Contains(l.ResponsavelId.Value) && !l.Arquivado
                && l.ResponsavelAtribuidoEm > desde && l.ResponsavelAtribuidoEm <= agora
                && (l.AtualizadoPorId ?? l.CriadoPorId) != l.ResponsavelId)
            .Select(l => new { l.Id, l.NomeOuRazaoSocial, l.ProdutoInteresse, ResponsavelId = l.ResponsavelId!.Value })
            .ToListAsync(ct);

        foreach (var doUsuario in leads.GroupBy(l => l.ResponsavelId))
        {
            var lista = doUsuario.ToList();
            var mensagem = lista.Count == 1
                ? new PushMensagem("Novo lead para você", Nome(lista[0].NomeOuRazaoSocial, lista[0].ProdutoInteresse),
                    $"/app/crm/leads/kanban?lead={lista[0].Id}", $"lead-{lista[0].Id}")
                : new PushMensagem($"{lista.Count} novos leads para você", string.Join(", ", lista.Take(5).Select(l => l.NomeOuRazaoSocial)),
                    "/app/crm/leads/kanban", $"leads-{agora:yyyyMMddHHmmss}");
            await push.EnviarAsync(doUsuario.Key, mensagem, ct);
        }

        return agora;
    }

    private static string Nome(string nome, string? oQue) => string.IsNullOrWhiteSpace(oQue) ? nome : $"{nome} · {oQue}";
}
