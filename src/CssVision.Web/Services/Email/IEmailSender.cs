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

    /// <summary>
    /// Envia vários e-mails numa só conexão, pela conta SMTP do próprio consultor (o remetente é o e-mail dele). Diferente de
    /// <see cref="EnviarAsync"/>, falha de conexão/login LANÇA <see cref="EmailContaException"/> — o consultor precisa saber que a
    /// senha dele foi recusada. Falha em uma mensagem só não derruba as outras; retorna, na mesma ordem das mensagens, se cada uma saiu.
    /// </summary>
    Task<IReadOnlyList<bool>> EnviarLoteComContaAsync(ContaSmtp conta, string remetenteNome, IReadOnlyList<MensagemEmail> mensagens, CancellationToken ct);

    /// <summary>Conecta e autentica na conta sem enviar nada; lança <see cref="EmailContaException"/> se não der.</summary>
    Task ValidarContaAsync(ContaSmtp conta, CancellationToken ct);
}

/// <summary>Credenciais SMTP de uma caixa de e-mail (a do consultor).</summary>
public sealed record ContaSmtp(string Host, int Port, string UserName, string Senha, string Email);

/// <param name="UrlDescadastro">Link de descadastro: vai também nos cabeçalhos List-Unsubscribe (o Gmail mostra "Cancelar inscrição").</param>
public sealed record MensagemEmail(string DestinatarioEmail, string DestinatarioNome, string Assunto, string CorpoHtml, string? UrlDescadastro = null);

/// <summary>Conta de e-mail inválida/recusada pelo provedor — a mensagem é segura para mostrar ao usuário.</summary>
public sealed class EmailContaException(string mensagem) : Exception(mensagem);
