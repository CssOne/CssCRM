using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Log de auditoria das operações relevantes do CRM (criação/edição de lead, mudança de etapa,
/// atribuição, ganho/perda, conclusão de atividade). Quando este módulo for integrado à
/// AplicacaoDashboard, o ideal é que <see cref="IAuditSink"/> passe a escrever na tabela de
/// auditoria já existente naquele projeto — a interface foi desenhada exatamente para isso
/// (ver Services/Crm/IAuditSink.cs). Esta tabela é o armazenamento padrão enquanto o CRM roda
/// isoladamente.
/// </summary>
public class CrmAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public string Acao { get; set; } = string.Empty;
    public string EntidadeTipo { get; set; } = string.Empty;
    public Guid? EntidadeId { get; set; }

    /// <summary>Detalhes em JSON, nunca contendo dados sensíveis completos (CPF/CNPJ/telefone/e-mail mascarados).</summary>
    public string? DetalhesJson { get; set; }

    public DateTimeOffset OcorridoEm { get; set; } = DateTimeOffset.UtcNow;
}
