using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Avisa por notificação (push) administradores, gestores master e gestores comerciais quando há leads
/// do tráfego pago parados porque todos os consultores aptos bateram o limite diário/mensal — para
/// decidirem se continuam a distribuição automática mesmo assim (Gestão comercial). Avisa uma vez
/// quando o problema começa e repete de tempos em tempos enquanto ele durar; ao resolver, zera.
/// </summary>
public sealed class AlertaDistribuicaoService(
    ApplicationDbContext db,
    ILeadAssignmentService distribuicao,
    IPushService push,
    ILogger<AlertaDistribuicaoService> logger,
    ICrmEventHub? eventos = null,
    TimeProvider? relogio = null)
{
    private const string ChaveUltimoAviso = "distribuicao:alerta-enviado-em";

    /// <summary>Enquanto o problema durar sem decisão, repete o aviso a cada tanto.</summary>
    public static readonly TimeSpan IntervaloReaviso = TimeSpan.FromHours(3);

    /// <summary>Texto do aviso conforme o motivo: limite diário/mensal, horário/dia de recebimento ou os dois.</summary>
    public static PushMensagem MontarMensagem(AlertaDistribuicaoDto estado)
    {
        var porLimite = estado.LeadsBloqueadosPorLimite > 0;
        var porHorario = estado.LeadsBloqueadosPorHorario > 0;
        var titulo = porLimite && porHorario ? "Leads parados: limite atingido e consultores fora do horário"
            : porHorario ? "Leads parados: todos os consultores estão fora do horário"
            : "Leads parados: todos os consultores atingiram o limite";
        var motivo = porLimite && porHorario ? "por limite de leads e fora do horário de recebimento"
            : porHorario ? "porque ninguém está dentro do dia/horário de recebimento"
            : "porque todos atingiram o limite diário ou mensal";
        return new PushMensagem(
            titulo,
            $"{estado.LeadsBloqueados} lead(s) sem responsável {motivo}. Abra a Gestão comercial para continuar a distribuição mesmo assim.",
            "/app/crm/gestao",
            "alerta-limite-distribuicao");
    }

    /// <returns>true se mandou um novo aviso.</returns>
    public async Task<bool> VerificarAsync(CancellationToken ct)
    {
        var estado = await distribuicao.ObterEstadoDistribuicaoAsync(ct);
        var parametro = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == ChaveUltimoAviso, ct);

        if (!estado.Bloqueada)
        {
            if (parametro is not null)
            {
                db.CrmParametros.Remove(parametro);
                await db.SaveChangesAsync(ct);
            }
            return false;
        }

        var agora = (relogio ?? TimeProvider.System).GetUtcNow();
        if (parametro is not null
            && DateTimeOffset.TryParse(parametro.Valor, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ultimo)
            && agora - ultimo < IntervaloReaviso)
        {
            return false;
        }

        var valor = agora.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        if (parametro is null) db.CrmParametros.Add(new CrmParametro { Chave = ChaveUltimoAviso, Valor = valor });
        else parametro.Valor = valor;
        await db.SaveChangesAsync(ct);

        var destinatarios = await db.UserRoles
            .Join(db.Roles.Where(r => Roles.GestaoComercial.Contains(r.Name!)), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.Ativo), id => id, u => u.Id, (_, u) => u.Id)
            .Distinct()
            .ToListAsync(ct);

        var mensagem = MontarMensagem(estado);
        var enviados = 0;
        foreach (var usuarioId in destinatarios)
        {
            try
            {
                enviados += await push.EnviarAsync(usuarioId, mensagem, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao avisar o usuário {UsuarioId} sobre o limite da distribuição.", usuarioId);
            }
        }

        logger.LogWarning("Distribuição parada (limite: {PorLimite}, horário: {PorHorario}): {Leads} lead(s) sem responsável; {Enviados} notificação(ões) enviada(s) a {Gestores} gestor(es).",
            estado.LeadsBloqueadosPorLimite, estado.LeadsBloqueadosPorHorario, estado.LeadsBloqueados, enviados, destinatarios.Count);
        // Telas abertas de gestores mostram o aviso na hora (AlertaDistribuicaoLimites).
        eventos?.PublicarQuadroAtualizado("distribuicao");
        return true;
    }
}
