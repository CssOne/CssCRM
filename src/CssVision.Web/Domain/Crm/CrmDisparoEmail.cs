using System.Text.Json.Serialization;

namespace CssVision.Web.Domain.Crm;

/// <summary>Andamento do disparo: a fila em segundo plano (DisparoEmailBackgroundService) vai de Pendente a Concluido.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusDisparoEmail
{
    Pendente = 1,
    Enviando = 2,
    Concluido = 3,
    Falhou = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusDestinatarioDisparo
{
    Pendente = 1,
    Enviado = 2,
    Falha = 3,
}

/// <summary>
/// Disparo de e-mail de um consultor para os clientes dele em uma etapa. Fica na fila até o serviço em segundo plano enviar (em lotes, pela caixa do
/// próprio consultor) e vira o histórico que o consultor consulta depois: quantos receberam, quantos falharam e quando.
/// </summary>
public class CrmDisparoEmail : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public Guid EtapaId { get; set; }

    /// <summary>Nome da etapa na hora do disparo (a etapa pode ser renomeada depois).</summary>
    public string EtapaNome { get; set; } = string.Empty;

    public string Assunto { get; set; } = string.Empty;

    /// <summary>Texto escrito pelo consultor, com {nome} ainda sem trocar.</summary>
    public string Mensagem { get; set; } = string.Empty;

    public StatusDisparoEmail Status { get; set; } = StatusDisparoEmail.Pendente;
    public int Total { get; set; }
    public int Enviados { get; set; }
    public int Falhas { get; set; }

    /// <summary>Motivo quando o disparo inteiro falhou (login recusado, servidor fora do ar...).</summary>
    public string? Erro { get; set; }

    public DateTimeOffset? IniciadoEm { get; set; }
    public DateTimeOffset? ConcluidoEm { get; set; }

    public List<CrmDisparoEmailDestinatario> Destinatarios { get; set; } = [];
}

public class CrmDisparoEmailDestinatario : CrmEntityBase
{
    public Guid DisparoId { get; set; }
    public CrmDisparoEmail Disparo { get; set; } = null!;

    public Guid LeadId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public StatusDestinatarioDisparo Status { get; set; } = StatusDestinatarioDisparo.Pendente;
    public DateTimeOffset? EnviadoEm { get; set; }
}
