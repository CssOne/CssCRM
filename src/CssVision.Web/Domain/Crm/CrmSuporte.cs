using System.Text.Json.Serialization;

namespace CssVision.Web.Domain.Crm;

/// <summary>Vai e volta como texto ("Aberto", "EmAtendimento", "Resolvido") na API — é o que a tela de Suporte usa.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusChamadoSuporte
{
    Aberto = 1,
    EmAtendimento = 2,
    Resolvido = 3,
}

/// <summary>
/// Chamado aberto por qualquer usuário na aba Suporte. Quem atende é o administrador do sistema
/// (configurado em Suporte:Atendentes; padrão <see cref="Services.Crm.SuporteService.EmailAtendente"/>); o solicitante só enxerga os próprios chamados.
/// </summary>
public class CrmSuporteChamado : CrmEntityBase
{
    public string Assunto { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public StatusChamadoSuporte Status { get; set; } = StatusChamadoSuporte.Aberto;
    public Guid SolicitanteId { get; set; }
    public DateTimeOffset UltimaMensagemEm { get; set; }

    public List<CrmSuporteMensagem> Mensagens { get; set; } = [];
}

public class CrmSuporteMensagem : CrmEntityBase
{
    public Guid ChamadoId { get; set; }
    public CrmSuporteChamado Chamado { get; set; } = null!;
    public Guid AutorId { get; set; }
    public bool DoSuporte { get; set; }
    public string Texto { get; set; } = string.Empty;

    /// <summary>Print ou PDF enviado junto (guardado no armazenamento de arquivos; a URL é a devolvida por ele).</summary>
    public string? AnexoUrl { get; set; }
    public string? AnexoNome { get; set; }
}
