using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Ponto único de gravação de auditoria do CRM. Ao integrar este módulo à AplicacaoDashboard,
/// troque a implementação registrada em DI (<see cref="CrmAuditLogSink"/>) por uma que escreva
/// na tabela de auditoria já existente naquele projeto, sem alterar os serviços de domínio.
/// </summary>
public interface IAuditSink
{
    Task RegistrarAsync(string acao, string entidadeTipo, Guid? entidadeId, object? detalhes, CancellationToken ct);
}

public sealed class CrmAuditLogSink(ApplicationDbContext db, ICurrentUserService currentUser) : IAuditSink
{
    public async Task RegistrarAsync(string acao, string entidadeTipo, Guid? entidadeId, object? detalhes, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return;

        db.CrmAuditLogs.Add(new CrmAuditLog
        {
            UsuarioId = currentUser.UserId,
            Acao = acao,
            EntidadeTipo = entidadeTipo,
            EntidadeId = entidadeId,
            DetalhesJson = detalhes is null ? null : System.Text.Json.JsonSerializer.Serialize(detalhes),
            OcorridoEm = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }
}
