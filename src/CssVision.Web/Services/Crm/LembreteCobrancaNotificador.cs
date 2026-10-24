using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>Lembra o consultor, no dia que ele escolheu em "Pagar depois", de pagar a cobrança (aviso dentro do CRM e push).</summary>
public interface ILembreteCobrancaNotificador
{
    /// <returns>Quantos lembretes foram enviados.</returns>
    Task<int> NotificarAsync(DateOnly hoje, CancellationToken ct);
}

public sealed class LembreteCobrancaNotificador(ApplicationDbContext db, IPushService push, ILogger<LembreteCobrancaNotificador> logger, ICrmEventHub? eventos = null) : ILembreteCobrancaNotificador
{
    public async Task<int> NotificarAsync(DateOnly hoje, CancellationToken ct)
    {
        var pendentes = await db.CrmCobrancasPagamento.AsNoTracking()
            .Where(p => p.LembreteEm != null && p.LembreteEm <= hoje && p.LembreteEnviadoEm == null && !p.Pago && p.InformadoEm == null)
            .Where(p => db.Users.Any(u => u.Id == p.UsuarioId && u.Ativo))
            .ToListAsync(ct);

        var enviados = 0;
        foreach (var grupo in pendentes.GroupBy(p => p.UsuarioId))
        {
            var cobrancas = (await CobrancasCalculo.CarregarAsync(db, grupo.Key, hoje, ct)).Cobrancas;
            foreach (var registro in grupo)
            {
                // "Reivindica" o lembrete antes de enviar, numa atualização única e atômica no banco: se duas cópias do sistema rodarem ao mesmo tempo, só uma
                // consegue marcar (1 linha alterada) e envia; a outra vê 0 linhas e pula.
                var agora = DateTimeOffset.UtcNow;
                var reivindicou = await db.CrmCobrancasPagamento.Where(p => p.Id == registro.Id && p.LembreteEnviadoEm == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.LembreteEnviadoEm, agora), ct) == 1;
                if (!reivindicou) continue;

                var cobranca = cobrancas.FirstOrDefault(c => c.Item == registro.Item.ToString() && c.Competencia == registro.Competencia && c.CustoId == registro.CustoId);
                if (cobranca is null || cobranca.Pago) continue;   // a cobrança mudou ou já foi paga: o lembrete não faz mais sentido

                var aviso = new CrmAvisoConsultor
                {
                    ConsultorId = registro.UsuarioId, Titulo = "Lembrete de pagamento",
                    Mensagem = $"Você pediu para ser lembrado hoje: {cobranca.Rotulo} ({cobranca.Descricao}) ainda está em aberto.",
                    Valor = cobranca.Valor, Referencia = cobranca.Rotulo, EnviadoPorId = registro.UsuarioId,
                };
                db.CrmAvisosConsultor.Add(aviso);
                await db.SaveChangesAsync(ct);
                enviados++;
                try
                {
                    await push.EnviarAsync(registro.UsuarioId, new PushMensagem(aviso.Titulo, $"{aviso.Mensagem} · R$ {cobranca.Valor:N2}", "/app/portal/controle-financeiro", $"lembrete-cobranca-{registro.Id}"), ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Falha ao enviar o push do lembrete da cobrança {RegistroId} ao consultor {ConsultorId}.", registro.Id, registro.UsuarioId);
                }
            }
        }

        if (enviados > 0) eventos?.PublicarQuadroAtualizado("avisos");
        return enviados;
    }
}
