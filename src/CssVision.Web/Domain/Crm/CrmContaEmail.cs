namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Caixa de e-mail do consultor usada nos disparos para a carteira dele (SMTP do próprio provedor — Gmail, Outlook etc.). A senha
/// (senha de app) fica protegida com o Data Protection do ASP.NET e nunca volta para a tela.
/// </summary>
public class CrmContaEmail : CrmEntityBase
{
    public Guid UsuarioId { get; set; }

    /// <summary>Endereço do consultor — é o remetente dos disparos.</summary>
    public string Email { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = string.Empty;
    public string SenhaProtegida { get; set; } = string.Empty;
}
