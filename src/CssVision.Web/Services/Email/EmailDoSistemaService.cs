using CssVision.Web.Api.Contracts.Common;
using System.Text.Json;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Email;

/// <summary>Como o e-mail do sistema está configurado: pelo painel (guardado no banco), pelo servidor (variáveis Smtp__*) ou ainda não.</summary>
public sealed record EmailSistemaDto(bool Configurado, string Origem, string? Host, int? Port, string? UserName, string? FromEmail, string FromName);

public sealed record EmailSistemaRequest(string Host, int Port, string UserName, string? Senha, string FromEmail, string? FromName);

public interface IEmailDoSistemaService
{
    /// <summary>Configuração em uso: a do painel, se houver, senão a do servidor. Nunca lança — se a senha guardada não abrir, cai na do servidor.</summary>
    Task<SmtpOptions> ResolverAsync(CancellationToken ct);

    Task<EmailSistemaDto> ObterAsync(CancellationToken ct);

    /// <summary>Senha guardada (aberta) para testar o login quando o administrador não digitou uma nova.</summary>
    Task<string?> SenhaGuardadaAsync(CancellationToken ct);

    Task<EmailSistemaDto> SalvarAsync(EmailSistemaRequest request, CancellationToken ct);

    /// <summary>Apaga a configuração do painel; volta a valer a do servidor (se houver).</summary>
    Task RemoverAsync(CancellationToken ct);
}

/// <summary>
/// E-mail do sistema (redefinição de senha etc.) configurável pelo painel de administração, para não depender de mexer nas variáveis do servidor.
/// Fica numa linha de <see cref="CrmParametro"/> (JSON) com a senha protegida pelo Data Protection — as chaves ficam no banco (ver AddDataProtection),
/// então a senha continua abrindo depois de cada deploy. O que for configurado aqui tem prioridade sobre <c>Smtp__*</c> do servidor.
/// </summary>
public sealed class EmailDoSistemaService(
    ApplicationDbContext db,
    IOptions<SmtpOptions> servidor,
    IDataProtectionProvider protecao,
    IAuditSink audit,
    ILogger<EmailDoSistemaService> logger) : IEmailDoSistemaService
{
    private const string Chave = "email-sistema";
    private IDataProtector Protetor => protecao.CreateProtector("CssVision.EmailDoSistema");

    private sealed record Guardado(string Host, int Port, string UserName, string SenhaProtegida, string FromEmail, string FromName);

    private async Task<Guardado?> LerAsync(CancellationToken ct)
    {
        var p = await db.CrmParametros.AsNoTracking().FirstOrDefaultAsync(x => x.Chave == Chave, ct);
        if (p is null) return null;
        try
        {
            var g = JsonSerializer.Deserialize<Guardado>(p.Valor);
            return g is { Host.Length: > 0 } ? g : null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "A configuração do e-mail do sistema guardada no banco está ilegível; usando a do servidor.");
            return null;
        }
    }

    private static bool ServidorConfigurado(SmtpOptions o) => !string.IsNullOrWhiteSpace(o.Host) && !string.IsNullOrWhiteSpace(o.FromEmail);

    public async Task<SmtpOptions> ResolverAsync(CancellationToken ct)
    {
        var g = await LerAsync(ct);
        if (g is not null)
        {
            try
            {
                return new SmtpOptions { Host = g.Host, Port = g.Port, UserName = g.UserName, Password = Protetor.Unprotect(g.SenhaProtegida), UseSsl = true, FromEmail = g.FromEmail, FromName = g.FromName };
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                logger.LogError(ex, "Não foi possível abrir a senha do e-mail do sistema guardada no painel; usando a do servidor. Configure o e-mail do sistema de novo.");
            }
        }
        return servidor.Value;
    }

    public async Task<EmailSistemaDto> ObterAsync(CancellationToken ct)
    {
        var g = await LerAsync(ct);
        if (g is not null) return new EmailSistemaDto(true, "painel", g.Host, g.Port, g.UserName, g.FromEmail, g.FromName);
        var s = servidor.Value;
        return ServidorConfigurado(s)
            ? new EmailSistemaDto(true, "servidor", s.Host, s.Port, s.UserName, s.FromEmail, s.FromName)
            : new EmailSistemaDto(false, "nenhuma", null, null, null, null, s.FromName);
    }

    public async Task<string?> SenhaGuardadaAsync(CancellationToken ct)
    {
        var g = await LerAsync(ct);
        if (g is null) return null;
        try { return Protetor.Unprotect(g.SenhaProtegida); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    public async Task<EmailSistemaDto> SalvarAsync(EmailSistemaRequest request, CancellationToken ct)
    {
        var host = request.Host.Trim();
        var usuario = request.UserName.Trim();
        var de = request.FromEmail.Trim();
        if (host.Length == 0) throw new CrmBusinessException("Informe o servidor SMTP (ex.: smtp.gmail.com).", "servidor_obrigatorio");
        if (request.Port is <= 0 or > 65535) throw new CrmBusinessException("A porta do servidor SMTP é inválida (o comum é 587).", "porta_invalida");
        if (usuario.Length == 0) throw new CrmBusinessException("Informe o usuário do servidor SMTP.", "usuario_obrigatorio");
        if (!de.Contains('@') || de.Contains(' ')) throw new CrmBusinessException("Informe o e-mail remetente (ex.: crm@suaempresa.com.br).", "remetente_invalido");

        var senha = request.Senha?.Trim();
        if (string.IsNullOrEmpty(senha)) senha = await SenhaGuardadaAsync(ct);
        if (string.IsNullOrEmpty(senha)) throw new CrmBusinessException("Informe a senha (no Gmail ou Outlook, uma senha de app).", "senha_obrigatoria");

        var guardado = new Guardado(host, request.Port, usuario, Protetor.Protect(senha), de, string.IsNullOrWhiteSpace(request.FromName) ? "CSS Brasil CRM" : request.FromName.Trim());
        var json = JsonSerializer.Serialize(guardado);
        var p = await db.CrmParametros.FirstOrDefaultAsync(x => x.Chave == Chave, ct);
        if (p is null) db.CrmParametros.Add(new CrmParametro { Chave = Chave, Valor = json });
        else p.Valor = json;
        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("EmailDoSistemaConfigurado", nameof(CrmParametro), null, new { host, request.Port, usuario, de }, ct);
        return new EmailSistemaDto(true, "painel", guardado.Host, guardado.Port, guardado.UserName, guardado.FromEmail, guardado.FromName);
    }

    public async Task RemoverAsync(CancellationToken ct)
    {
        var p = await db.CrmParametros.FirstOrDefaultAsync(x => x.Chave == Chave, ct);
        if (p is null) return;
        db.CrmParametros.Remove(p);
        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("EmailDoSistemaRemovido", nameof(CrmParametro), null, null, ct);
    }
}
