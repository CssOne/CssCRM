using System.ComponentModel.DataAnnotations;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

public record SuporteChamadoCreateRequest(
    [property: Required, StringLength(200, MinimumLength = 3)] string Assunto,
    [property: Required, StringLength(40)] string Categoria,
    [property: Required, StringLength(4000, MinimumLength = 3)] string Mensagem);

public record SuporteMensagemRequest([property: Required, StringLength(4000, MinimumLength = 1)] string Texto);

public record SuporteStatusRequest(StatusChamadoSuporte Status);

public record SuporteMensagemDto(Guid Id, string AutorNome, bool DoSuporte, string Texto, DateTimeOffset CriadoEm);

public record SuporteChamadoResumoDto(
    Guid Id, string Assunto, string Categoria, StatusChamadoSuporte Status, Guid SolicitanteId, string SolicitanteNome,
    string? SolicitanteEmail, DateTimeOffset CriadoEm, DateTimeOffset UltimaMensagemEm, int Mensagens);

public record SuporteChamadoDto(SuporteChamadoResumoDto Resumo, IReadOnlyList<SuporteMensagemDto> Mensagens);

/// <summary><c>Atende</c>: o usuário é quem recebe e responde os chamados (vê todos).</summary>
public record SuporteListaDto(bool Atende, int Abertos, IReadOnlyList<SuporteChamadoResumoDto> Chamados);
