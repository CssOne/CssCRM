namespace CssVision.Web.Domain.Crm;

/// <summary>
/// E-mail antigo de uma conta que foi unificada em outra. O Notion ainda traz o e-mail antigo no campo
/// "Vendedor" dos cards: a sincronização usa este alias para ligar o card à conta que ficou, em vez de criar
/// a conta duplicada de novo. Não herda CrmEntityBase (dado de referência, sem auditoria).
/// </summary>
public class CrmUsuarioAlias
{
    /// <summary>E-mail antigo, normalizado em maiúsculas (como NormalizedEmail do Identity) — chave primária.</summary>
    public string EmailNormalizado { get; set; } = string.Empty;

    /// <summary>Conta que ficou no lugar.</summary>
    public Guid UsuarioId { get; set; }
}
