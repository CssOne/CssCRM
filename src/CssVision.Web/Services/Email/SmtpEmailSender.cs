using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CssVision.Web.Services.Email;

public sealed class SmtpEmailSender(IEmailDoSistemaService emailDoSistema, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> EnviarAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, CancellationToken ct)
    {
        // A configuração do painel (Administração → E-mail do sistema) vale antes da do servidor (Smtp__*).
        var smtp = await emailDoSistema.ResolverAsync(ct);
        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromEmail))
        {
            logger.LogError("E-mail NÃO enviado: o e-mail do sistema não está configurado (nem no painel de administração, nem em Smtp__Host/Smtp__FromEmail). Destinatário: {Email}", destinatarioEmail);
            return false;
        }

        try
        {
            var mensagem = new MimeMessage();
            mensagem.From.Add(new MailboxAddress(smtp.FromName, smtp.FromEmail));
            mensagem.To.Add(new MailboxAddress(destinatarioNome, destinatarioEmail));
            mensagem.Subject = assunto;
            mensagem.Body = new BodyBuilder { HtmlBody = corpoHtml }.ToMessageBody();

            using var client = new SmtpClient();
            // Porta 465 é SSL direto; as demais (587...) começam sem criptografia e sobem para STARTTLS.
            await client.ConnectAsync(smtp.Host, smtp.Port, smtp.Port == 465 ? SecureSocketOptions.SslOnConnect : smtp.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
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

    public async Task ValidarContaAsync(ContaSmtp conta, CancellationToken ct)
    {
        using var client = await ConectarAsync(conta, ct);
        await client.DisconnectAsync(true, ct);
    }

    public async Task<IReadOnlyList<bool>> EnviarLoteComContaAsync(ContaSmtp conta, string remetenteNome, IReadOnlyList<MensagemEmail> mensagens, CancellationToken ct)
    {
        using var client = await ConectarAsync(conta, ct);
        var resultado = new List<bool>(mensagens.Count);
        foreach (var m in mensagens)
        {
            ct.ThrowIfCancellationRequested();
            if (!client.IsConnected)
            {
                resultado.Add(false);
                continue;
            }
            try
            {
                var mime = new MimeMessage();
                mime.From.Add(new MailboxAddress(remetenteNome, conta.Email));
                mime.To.Add(new MailboxAddress(m.DestinatarioNome, m.DestinatarioEmail));
                mime.Subject = m.Assunto;
                if (!string.IsNullOrEmpty(m.UrlDescadastro))
                {
                    mime.Headers.Add("List-Unsubscribe", $"<{m.UrlDescadastro}>");
                    mime.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
                }
                mime.Body = new BodyBuilder { HtmlBody = m.CorpoHtml }.ToMessageBody();
                await client.SendAsync(mime, ct);
                resultado.Add(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Falha ao enviar e-mail do consultor {Conta} para {Email}", conta.Email, m.DestinatarioEmail);
                resultado.Add(false);
            }
        }
        if (client.IsConnected) await client.DisconnectAsync(true, ct);
        return resultado;
    }

    private async Task<SmtpClient> ConectarAsync(ContaSmtp conta, CancellationToken ct)
    {
        var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(conta.Host, conta.Port, conta.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(conta.UserName, conta.Senha, ct);
            return client;
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            client.Dispose();
            logger.LogWarning(ex, "Login recusado na conta {Conta}", conta.Email);
            throw new EmailContaException("O provedor recusou o login. No Gmail/Outlook use uma \"senha de app\" (não a senha normal do e-mail).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            logger.LogWarning(ex, "Não conectou no SMTP {Host}:{Port} da conta {Conta}", conta.Host, conta.Port, conta.Email);
            throw new EmailContaException($"Não foi possível conectar em {conta.Host}:{conta.Port}. Confira o servidor e a porta.");
        }
    }
}
