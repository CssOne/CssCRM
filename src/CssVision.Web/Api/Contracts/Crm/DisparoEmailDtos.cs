using System.ComponentModel.DataAnnotations;

namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Disparo de e-mail do consultor para os clientes dele (com e-mail) que estão em uma etapa do quadro de leads.</summary>
public record DisparoEmailRequest(
    [Required] Guid EtapaId,
    [Required, StringLength(150, MinimumLength = 3)] string Assunto,
    [Required, StringLength(5000, MinimumLength = 3)] string Mensagem);

/// <summary>Quantos clientes da carteira receberiam o disparo naquela etapa.</summary>
public record DisparoEmailPreviaDto(int Destinatarios, int Limite);

public record DisparoEmailResultadoDto(int Destinatarios, int Enviados, int Falhas);

/// <summary>Conta de e-mail do consultor (sem a senha). Host/Port em branco = detectados pelo provedor do e-mail (Gmail, Outlook...).</summary>
public record ContaEmailRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(200, MinimumLength = 4)] string Senha,
    [StringLength(200)] string? Host = null,
    int? Port = null);

public record ContaEmailDto(string Email, string Host, int Port);
