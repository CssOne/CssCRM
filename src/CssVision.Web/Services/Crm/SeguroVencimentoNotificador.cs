using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// No dia em que vence o seguro do associado (data que o consultor informou ao mandar o lead para "Follow Up" por "Já tem seguro"), avisa o
/// consultor responsável — aviso dentro do CRM e push. A atividade no calendário já foi criada quando a data foi informada.
/// </summary>
public interface ISeguroVencimentoNotificador
{
    /// <returns>Quantos avisos foram enviados.</returns>
    Task<int> NotificarAsync(DateOnly hoje, CancellationToken ct);
}

public sealed class SeguroVencimentoNotificador(ApplicationDbContext db, IPushService push, ILogger<SeguroVencimentoNotificador> logger, ICrmEventHub? eventos = null) : ISeguroVencimentoNotificador
{
    private const string EtapaFollowUp = "Follow Up";

    public async Task<int> NotificarAsync(DateOnly hoje, CancellationToken ct)
    {
        var leads = await db.CrmLeads
            .Where(l => l.SeguroVenceEm != null && l.SeguroVenceEm <= hoje && l.SeguroAvisoEnviadoEm == null && !l.Arquivado
                        && l.ResponsavelId != null && l.Etapa != null && l.Etapa.Nome == EtapaFollowUp
                        && db.Users.Any(u => u.Id == l.ResponsavelId && u.Ativo))
            .ToListAsync(ct);

        var enviados = 0;
        foreach (var lead in leads)
        {
            var consultorId = lead.ResponsavelId!.Value;
            var quando = lead.SeguroVenceEm == hoje ? "vence hoje" : $"venceu em {lead.SeguroVenceEm:dd/MM/yyyy}";
            var aviso = new CrmAvisoConsultor
            {
                ConsultorId = consultorId,
                Titulo = "Seguro do associado vence",
                Mensagem = $"O seguro de {lead.NomeOuRazaoSocial} {quando}. Retome o contato (Follow Up).",
                Referencia = lead.NomeOuRazaoSocial,
                EnviadoPorId = consultorId,
            };
            db.CrmAvisosConsultor.Add(aviso);
            lead.SeguroAvisoEnviadoEm = lead.SeguroVenceEm;
            await db.SaveChangesAsync(ct);
            enviados++;

            try
            {
                await push.EnviarAsync(consultorId, new PushMensagem(aviso.Titulo, aviso.Mensagem, $"/app/crm/leads/{lead.Id}", $"seguro-{lead.Id}-{lead.SeguroVenceEm:yyyyMMdd}"), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao enviar o push do vencimento do seguro do lead {LeadId} ao consultor {ConsultorId}.", lead.Id, consultorId);
            }
        }

        if (enviados > 0) eventos?.PublicarQuadroAtualizado("avisos");
        return enviados;
    }
}
