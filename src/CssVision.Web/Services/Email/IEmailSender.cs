namespace CssVision.Web.Services.Email;

public interface IEmailSender
{
    /// <summary>
    /// Envia um e-mail HTML. Não lança em caso de falha — apenas loga e retorna false, seguindo
    /// o mesmo padrão de "falha silenciosa" usado nas integrações de marketing (ver
    /// IMetaConversionService): o SMTP pode estar mal configurado ou fora do ar, mas isso nunca
    /// deve virar um erro 500 pro usuário final numa ação como "esqueci minha senha".
    /// </summary>
    Task<bool> EnviarAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, CancellationToken ct);
}
