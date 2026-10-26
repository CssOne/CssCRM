using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Vigia os leads do tráfego pago que já estão com um consultor mas seguem em "Sem etapa" (o cronômetro do cartão do quadro):
/// <list type="bullet">
/// <item>passou <see cref="AvisoConsultorApos"/> sem o consultor pegar → push para ele; passou <see cref="AvisoUrgenteApos"/> → segundo push, mais forte;</item>
/// <item>consultor com <see cref="ReincidenciaLeads"/> ou mais leads que chegaram ao segundo aviso nos últimos <see cref="ReincidenciaDias"/> dias → push para
/// administradores, gestores master, supervisores e o gestor comercial dele, no máximo uma vez por dia por consultor.</item>
/// </list>
/// Cada aviso sai uma vez por lead (<see cref="CrmEsperaLead"/>). Só entra lead que começou a esperar nas últimas <see cref="JanelaMaxima"/>, para o
/// primeiro ciclo depois do deploy não avisar de um acúmulo antigo.
/// </summary>
public sealed class EsperaTrafegoService(
    ApplicationDbContext db,
    IPushService push,
    ILogger<EsperaTrafegoService> logger,
    TimeProvider? relogio = null)
{
    public static readonly TimeSpan AvisoConsultorApos = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AvisoUrgenteApos = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan JanelaMaxima = TimeSpan.FromHours(24);
    public const int ReincidenciaLeads = 3;
    public const int ReincidenciaDias = 7;
    public static readonly TimeSpan ReavisoGestao = TimeSpan.FromHours(24);

    private const string UrlQuadro = "/app/crm/leads/kanban";
    private static string ChaveGestao(Guid consultorId) => $"espera-trafego:gestao:{consultorId}";

    private sealed record Envio(Guid UsuarioId, PushMensagem Mensagem);

    /// <returns>Quantos avisos foram enviados (consultores e gestão).</returns>
    public async Task<int> VerificarAsync(CancellationToken ct)
    {
        var agora = (relogio ?? TimeProvider.System).GetUtcNow();
        var envios = new List<Envio>();

        // 1) Quem já saiu de "Sem etapa" (ou foi arquivado / passou a outro consultor) deixa de esperar.
        await db.CrmEsperasLead
            .Where(e => e.AtendidoEm == null && (e.Lead.EtapaId != null || e.Lead.Arquivado || e.Lead.ResponsavelId != e.ConsultorId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AtendidoEm, agora), ct);

        // 2) Leads do tráfego pago com consultor ativo, em "Sem etapa", esperando há pelo menos o primeiro prazo.
        var maisAntigoQuePrimeiroAviso = agora - AvisoConsultorApos;
        var piso = agora - JanelaMaxima;
        var candidatos = await db.CrmLeads.AsNoTracking()
            .Where(l => l.EtapaId == null && !l.Arquivado && l.ResponsavelId != null)
            .Where(OrigemLead.VeioDoTrafegoPago)
            .Where(l => db.Users.Any(u => u.Id == l.ResponsavelId && u.Ativo))
            .Select(l => new { l.Id, l.NomeOuRazaoSocial, ResponsavelId = l.ResponsavelId!.Value, Inicio = l.ResponsavelAtribuidoEm ?? l.CriadoEm })
            .Where(l => l.Inicio <= maisAntigoQuePrimeiroAviso && l.Inicio >= piso)
            .ToListAsync(ct);

        if (candidatos.Count > 0)
        {
            var ids = candidatos.Select(c => c.Id).ToList();
            var registros = await db.CrmEsperasLead.Where(e => ids.Contains(e.LeadId)).ToDictionaryAsync(e => e.LeadId, ct);

            foreach (var c in candidatos)
            {
                if (!registros.TryGetValue(c.Id, out var registro))
                {
                    registro = new CrmEsperaLead { LeadId = c.Id, ConsultorId = c.ResponsavelId, EsperandoDesde = c.Inicio };
                    db.CrmEsperasLead.Add(registro);
                }
                else if (registro.ConsultorId != c.ResponsavelId || registro.EsperandoDesde != c.Inicio)
                {
                    // Lead passou para outro consultor (ou foi redistribuído): a espera recomeça, com os avisos de novo.
                    registro.ConsultorId = c.ResponsavelId;
                    registro.EsperandoDesde = c.Inicio;
                    registro.AvisoConsultorEm = null;
                    registro.AvisoUrgenteEm = null;
                    registro.AtendidoEm = null;
                }
                else
                {
                    registro.AtendidoEm = null; // voltou a "Sem etapa" na mesma atribuição: continua esperando, sem repetir avisos
                }

                var espera = agora - c.Inicio;
                if (espera >= AvisoUrgenteApos && registro.AvisoUrgenteEm is null)
                {
                    registro.AvisoUrgenteEm = agora;
                    registro.AvisoConsultorEm ??= agora; // se o primeiro não saiu a tempo (servidor parado), não manda os dois juntos
                    envios.Add(new Envio(c.ResponsavelId, new PushMensagem(
                        "Lead esfriando!",
                        $"{c.NomeOuRazaoSocial} está há {FormatarEspera(espera)} sem atendimento. Pegue agora, antes que o cliente seja de outro.",
                        UrlQuadro, $"espera-lead-urgente-{c.Id}")));
                }
                else if (espera >= AvisoConsultorApos && registro.AvisoConsultorEm is null)
                {
                    registro.AvisoConsultorEm = agora;
                    envios.Add(new Envio(c.ResponsavelId, new PushMensagem(
                        "Lead esperando você",
                        $"{c.NomeOuRazaoSocial} chegou há {FormatarEspera(espera)} e ainda está em Sem etapa. Comece o atendimento.",
                        UrlQuadro, $"espera-lead-{c.Id}")));
                }
            }
        }

        // Grava os avisos deste ciclo antes de contar: um lead que acabou de chegar ao segundo aviso já entra na conta da gestão.
        if (!await SalvarAsync(ct)) return 0;

        // 3) Consultor que demora com frequência: avisa a gestão.
        var desde = agora.AddDays(-ReincidenciaDias);
        var reincidentes = await db.CrmEsperasLead.AsNoTracking()
            .Where(e => e.AvisoUrgenteEm != null && e.AvisoUrgenteEm >= desde)
            .GroupBy(e => e.ConsultorId)
            .Select(g => new { ConsultorId = g.Key, Quantidade = g.Count() })
            .Where(g => g.Quantidade >= ReincidenciaLeads)
            .ToListAsync(ct);

        foreach (var r in reincidentes)
        {
            var consultor = await db.Users.AsNoTracking()
                .Where(u => u.Id == r.ConsultorId && u.Ativo)
                .Select(u => new { u.NomeCompleto, u.GestorComercialId })
                .FirstOrDefaultAsync(ct);
            if (consultor is null) continue;

            var chave = ChaveGestao(r.ConsultorId);
            var parametro = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
            if (parametro is not null
                && DateTimeOffset.TryParse(parametro.Valor, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ultimo)
                && agora - ultimo < ReavisoGestao)
            {
                continue;
            }
            var valor = agora.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            if (parametro is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
            else parametro.Valor = valor;

            // Administradores, gestores master e supervisores veem todos; o gestor comercial, só o consultor do time dele.
            var destinatarios = await db.UserRoles
                .Join(db.Roles.Where(x => x.Name == Roles.Admin || x.Name == Roles.GestorMaster || x.Name == Roles.SupervisorComercial), ur => ur.RoleId, x => x.Id, (ur, _) => ur.UserId)
                .Join(db.Users.Where(u => u.Ativo), id => id, u => u.Id, (_, u) => u.Id)
                .Distinct()
                .ToListAsync(ct);
            if (consultor.GestorComercialId is { } gestorId && !destinatarios.Contains(gestorId)
                && await db.Users.AnyAsync(u => u.Id == gestorId && u.Ativo, ct))
            {
                destinatarios.Add(gestorId);
            }

            var mensagem = new PushMensagem(
                "Consultor demorando para pegar leads",
                $"{consultor.NomeCompleto} deixou {r.Quantidade} leads do tráfego pago mais de {FormatarEspera(AvisoUrgenteApos)} sem atendimento nos últimos {ReincidenciaDias} dias.",
                "/app/crm/gestao", $"espera-gestao-{r.ConsultorId}");
            envios.AddRange(destinatarios.Select(d => new Envio(d, mensagem)));
        }

        if (!await SalvarAsync(ct)) return 0;

        var enviados = 0;
        foreach (var e in envios)
        {
            try
            {
                enviados += await push.EnviarAsync(e.UsuarioId, e.Mensagem, ct) > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao enviar o aviso de espera de lead ao usuário {UsuarioId}.", e.UsuarioId);
            }
        }

        if (envios.Count > 0) logger.LogInformation("Espera de leads do tráfego pago: {Avisos} aviso(s) gerado(s), {Entregues} com navegador inscrito.", envios.Count, enviados);
        return envios.Count;
    }

    /// <summary>Painel da Gestão comercial: quem tem lead esperando agora e quem demorou nos últimos dias. <paramref name="visiveis"/> nulo = todos os consultores.</summary>
    public async Task<EsperaTrafegoPainelDto> ObterPainelAsync(List<Guid>? visiveis, CancellationToken ct)
    {
        var agora = (relogio ?? TimeProvider.System).GetUtcNow();
        var desde = agora.AddDays(-ReincidenciaDias);

        var esperando = (await db.CrmLeads.AsNoTracking()
                .Where(l => l.EtapaId == null && !l.Arquivado && l.ResponsavelId != null)
                .Where(OrigemLead.VeioDoTrafegoPago)
                .Select(l => new { ResponsavelId = l.ResponsavelId!.Value, Inicio = l.ResponsavelAtribuidoEm ?? l.CriadoEm })
                .ToListAsync(ct))
            .GroupBy(l => l.ResponsavelId)
            .ToDictionary(g => g.Key, g => (Quantidade: g.Count(), MaisAntigo: g.Min(x => x.Inicio)));

        var historico = (await db.CrmEsperasLead.AsNoTracking()
                .Where(e => e.EsperandoDesde >= desde)
                .Select(e => new { e.ConsultorId, Forte = e.AvisoUrgenteEm != null })
                .ToListAsync(ct))
            .GroupBy(e => e.ConsultorId)
            .ToDictionary(g => g.Key, g => (Primeiro: g.Count(), Forte: g.Count(x => x.Forte)));

        var ids = esperando.Keys.Union(historico.Keys).Where(id => visiveis is null || visiveis.Contains(id)).ToList();
        var nomes = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);

        var consultores = ids
            .Select(id =>
            {
                var (qtd, antigo) = esperando.GetValueOrDefault(id);
                var (primeiro, forte) = historico.GetValueOrDefault(id);
                return new EsperaConsultorDto(id, nomes.GetValueOrDefault(id, "—"), qtd, qtd > 0 ? antigo : null, primeiro, forte, forte >= ReincidenciaLeads);
            })
            .OrderByDescending(c => c.Reincidente).ThenByDescending(c => c.PassaramDoPrazoForte).ThenByDescending(c => c.EsperandoAgora).ThenBy(c => c.Nome)
            .ToList();

        return new EsperaTrafegoPainelDto((int)AvisoConsultorApos.TotalMinutes, (int)AvisoUrgenteApos.TotalMinutes, ReincidenciaLeads, ReincidenciaDias, consultores);
    }

    /// <returns>false se outra cópia do sistema registrou antes: ela é quem envia, para o aviso não sair em dobro.</returns>
    private async Task<bool> SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Conflito ao registrar a espera dos leads do tráfego pago; os avisos deste ciclo ficam com a outra cópia.");
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static string FormatarEspera(TimeSpan espera) =>
        espera.TotalHours >= 1 ? $"{(int)espera.TotalHours}h{espera.Minutes:00}" : $"{(int)espera.TotalMinutes} min";
}

/// <summary>A cada minuto, confere os leads do tráfego pago parados em "Sem etapa" e manda os avisos (ver <see cref="EsperaTrafegoService"/>).</summary>
public sealed class EsperaTrafegoBackgroundService(IServiceScopeFactory scopeFactory, ILogger<EsperaTrafegoBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EsperaTrafegoService>().VerificarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao vigiar a espera dos leads do tráfego pago — tentando de novo no próximo ciclo.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
