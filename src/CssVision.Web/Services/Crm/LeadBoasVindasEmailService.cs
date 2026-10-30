using CssVision.Web.Data;
using CssVision.Web.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Crm;

/// <summary>Seção "BoasVindasEmail" (env: BoasVindasEmail__Ativo=true). Desligado por padrão: só liga com o e-mail do sistema (SES) pronto.</summary>
public class BoasVindasEmailOptions
{
    public const string SectionName = "BoasVindasEmail";

    public bool Ativo { get; set; }
}

public interface ILeadBoasVindasEmailService
{
    /// <summary>Dispara o e-mail sem atrasar quem chamou (a resposta do formulário). Nunca lança.</summary>
    void DispararEmSegundoPlano(Guid leadId);

    /// <summary>Envia o e-mail de boas-vindas do lead, uma única vez. Devolve true só se saiu.</summary>
    Task<bool> EnviarAsync(Guid leadId, CancellationToken ct);
}

public sealed class LeadBoasVindasEmailService(
    ApplicationDbContext db,
    IEmailSender emailSender,
    IDescadastroEmailTokens tokens,
    IConfiguration configuracao,
    IOptions<BoasVindasEmailOptions> opcoes,
    IServiceScopeFactory escopos,
    ILogger<LeadBoasVindasEmailService> logger,
    TimeProvider? relogio = null) : ILeadBoasVindasEmailService
{
    private static readonly string[] DominiosDeTeste = ["example.com", "example.org", "example.net", "teste.local"];

    public void DispararEmSegundoPlano(Guid leadId)
    {
        if (!opcoes.Value.Ativo) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var servico = escopo.ServiceProvider.GetRequiredService<ILeadBoasVindasEmailService>();
                await servico.EnviarAsync(leadId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao enviar o e-mail de boas-vindas do lead {LeadId}", leadId);
            }
        });
    }

    public async Task<bool> EnviarAsync(Guid leadId, CancellationToken ct)
    {
        if (!opcoes.Value.Ativo) return false;

        var lead = await db.CrmLeads.AsNoTracking()
            .Where(l => l.Id == leadId)
            .Select(l => new { l.NomeOuRazaoSocial, l.Email, l.ProdutoInteresse, l.ResponsavelId, l.EmailDescadastradoEm, l.BoasVindasEmailEnviadoEm })
            .FirstOrDefaultAsync(ct);

        if (lead is null || lead.BoasVindasEmailEnviadoEm is not null || lead.EmailDescadastradoEm is not null || lead.ResponsavelId is null) return false;
        if (lead.ProdutoInteresse is null || !lead.ProdutoInteresse.StartsWith("AGV", StringComparison.OrdinalIgnoreCase)) return false;

        var email = lead.Email?.Trim();
        if (!EmailServe(email)) return false;

        var baseUrl = configuracao["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            logger.LogWarning("E-mail de boas-vindas não enviado (lead {LeadId}): App:PublicBaseUrl não está configurada (sem ela não há como montar o link de cancelar inscrição nem a foto).", leadId);
            return false;
        }

        var consultor = await db.Users.AsNoTracking()
            .Where(u => u.Id == lead.ResponsavelId)
            .Select(u => new { u.NomeCompleto, u.FotoUrl, u.PhoneNumber })
            .FirstOrDefaultAsync(ct);
        if (consultor is null) return false;

        // Reserva o envio antes de mandar: duas chamadas ao mesmo tempo não mandam o e-mail duas vezes.
        var agora = (relogio ?? TimeProvider.System).GetUtcNow();
        var reservado = await db.CrmLeads
            .Where(l => l.Id == leadId && l.BoasVindasEmailEnviadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.BoasVindasEmailEnviadoEm, agora), ct);
        if (reservado == 0) return false;

        var html = LeadBoasVindasEmail.Montar(
            lead.NomeOuRazaoSocial,
            consultor.NomeCompleto,
            UrlAbsoluta(consultor.FotoUrl, baseUrl),
            LeadBoasVindasEmail.LinkWhatsApp(consultor.PhoneNumber, consultor.NomeCompleto),
            $"{baseUrl}/api/publico/descadastro?t={Uri.EscapeDataString(tokens.Gerar(leadId))}");

        var saiu = await emailSender.EnviarAsync(email!, lead.NomeOuRazaoSocial, LeadBoasVindasEmail.Assunto, html, ct);
        if (!saiu)
        {
            await db.CrmLeads.Where(l => l.Id == leadId).ExecuteUpdateAsync(s => s.SetProperty(l => l.BoasVindasEmailEnviadoEm, (DateTimeOffset?)null), ct);
            logger.LogWarning("E-mail de boas-vindas do lead {LeadId} não saiu (ver o erro do e-mail do sistema acima).", leadId);
            return false;
        }

        logger.LogInformation("E-mail de boas-vindas enviado ao lead {LeadId} (consultor {Consultor})", leadId, consultor.NomeCompleto);
        return true;
    }

    internal static bool EmailServe(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Any(char.IsWhiteSpace)) return false;
        var partes = email.Split('@');
        if (partes.Length != 2 || partes[0].Length == 0 || !partes[1].Contains('.') || partes[1].StartsWith('.') || partes[1].EndsWith('.')) return false;
        return !DominiosDeTeste.Contains(partes[1], StringComparer.OrdinalIgnoreCase);
    }

    internal static string? UrlAbsoluta(string? url, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url;
        return baseUrl + (url.StartsWith('/') ? url : "/" + url);
    }
}
