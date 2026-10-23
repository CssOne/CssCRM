using System.ComponentModel.DataAnnotations;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Disparo de e-mail do consultor para os clientes dele (com e-mail) que estão em uma etapa do quadro de leads.</summary>
public record DisparoEmailRequest(
    [Required] Guid EtapaId,
    [Required, StringLength(150, MinimumLength = 3)] string Assunto,
    [Required, StringLength(5000, MinimumLength = 3)] string Mensagem);

/// <summary>Quantos clientes da carteira receberiam o disparo naquela etapa (e quantos ficam de fora por terem cancelado a inscrição).</summary>
public record DisparoEmailPreviaDto(int Destinatarios, int Limite, int Descadastrados);

/// <summary>Disparo na fila ou já enviado — é também a linha do histórico do consultor.</summary>
public record DisparoEmailDto(
    Guid Id, string EtapaNome, string Assunto, StatusDisparoEmail Status, int Total, int Enviados, int Falhas, string? Erro,
    DateTimeOffset CriadoEm, DateTimeOffset? ConcluidoEm);

/// <summary>Conta de e-mail do consultor (sem a senha). Host/Port em branco = detectados pelo provedor do e-mail (Gmail, Outlook...).</summary>
public record ContaEmailRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(200, MinimumLength = 4)] string Senha,
    [StringLength(200)] string? Host = null,
    int? Port = null);

public record ContaEmailDto(string Email, string Host, int Port);
