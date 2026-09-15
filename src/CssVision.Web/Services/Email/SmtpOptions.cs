namespace CssVision.Web.Services.Email;

/// <summary>
/// Configuração do servidor SMTP usado para disparo de e-mails (redefinição de senha etc.).
/// Vinculada à seção "Smtp" do appsettings — em produção, defina via variáveis de ambiente
/// (Smtp__Host, Smtp__UserName, Smtp__Password etc.) ou `dotnet user-secrets`, nunca
/// commitando credenciais reais. Compatível com qualquer provedor SMTP padrão (Gmail, Outlook,
/// SendGrid, Amazon SES etc.) — só troque host/porta/usuário/senha.
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "CSS Brasil CRM";
}
