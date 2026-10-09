using System.ComponentModel.DataAnnotations;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Aviso de pagamento em aberto para um ou mais consultores (um aviso por consultor).</summary>
public record AvisoPagamentoCreateRequest(
    [Required, MinLength(1), MaxLength(100)] IReadOnlyList<Guid> ConsultorIds,
    [Required, StringLength(1000, MinimumLength = 3)] string Mensagem,
    decimal? Valor = null,
    [StringLength(200)] string? Referencia = null,
    [StringLength(120)] string? Titulo = null);

/// <summary>Visão de quem envia (gestão/financeiro).</summary>
public record AvisoPagamentoDto(
    Guid Id, Guid ConsultorId, string ConsultorNome, string Titulo, string Mensagem, decimal? Valor, string? Referencia,
    string EnviadoPorNome, DateTimeOffset CriadoEm, StatusAvisoConsultor Status, DateTimeOffset? LidoEm, DateTimeOffset? ResolvidoEm);

/// <summary>Quantos avisos de pagamento em aberto cada consultor tem (para a gestão comercial do financeiro).</summary>
public record AvisoResumoConsultorDto(Guid ConsultorId, int Abertos);

/// <summary>Visão do consultor: o que aparece na notificação e no card "Avisos importantes".</summary>
public record MeuAvisoDto(
    Guid Id, string Titulo, string Mensagem, decimal? Valor, string? Referencia, string EnviadoPorNome, DateTimeOffset CriadoEm, DateTimeOffset? LidoEm);
