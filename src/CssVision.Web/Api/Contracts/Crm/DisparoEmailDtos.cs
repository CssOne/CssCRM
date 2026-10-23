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
