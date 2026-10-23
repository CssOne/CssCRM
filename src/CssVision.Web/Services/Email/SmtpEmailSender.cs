using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CssVision.Web.Services.Email;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public Task<bool> EnviarAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, CancellationToken ct) =>
        EnviarInternoAsync(destinatarioEmail, destinatarioNome, assunto, corpoHtml, null, null, ct);

    public Task<bool> EnviarEmNomeDeAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, string remetenteNome, string responderParaEmail, CancellationToken ct) =>
        EnviarInternoAsync(destinatarioEmail, destinatarioNome, assunto, corpoHtml, remetenteNome, responderParaEmail, ct);

    private async Task<bool> EnviarInternoAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, string? remetenteNome, string? responderParaEmail, CancellationToken ct)
    {
        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            logger.LogWarning("Envio de e-mail ignorado: SMTP não configurado (seção Smtp do appsettings vazia). Destinatário: {Email}", destinatarioEmail);
            return false;
        }

        try
        {
            var mensagem = new MimeMessage();
            mensagem.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(remetenteNome) ? smtp.FromName : $"{remetenteNome} ({smtp.FromName})", smtp.FromEmail));
            if (!string.IsNullOrWhiteSpace(responderParaEmail))
            {
                mensagem.ReplyTo.Add(new MailboxAddress(remetenteNome, responderParaEmail));
            }
            mensagem.To.Add(new MailboxAddress(destinatarioNome, destinatarioEmail));
            mensagem.Subject = assunto;
            mensagem.Body = new BodyBuilder { HtmlBody = corpoHtml }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(smtp.Host, smtp.Port, smtp.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
            if (!string.IsNullOrEmpty(smtp.UserName))
            {
                await client.AuthenticateAsync(smtp.UserName, smtp.Password, ct);
            }
            await client.SendAsync(mensagem, ct);
            await client.DisconnectAsync(true, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar e-mail para {Email}", destinatarioEmail);
            return false;
        }
    }
}
