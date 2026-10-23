using System.ComponentModel.DataAnnotations;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Api.Contracts.Crm;

public record SuporteChamadoCreateRequest(
    [Required, StringLength(200, MinimumLength = 3)] string Assunto,
    [Required, StringLength(40)] string Categoria,
    [Required, StringLength(4000, MinimumLength = 3)] string Mensagem);

public record SuporteMensagemRequest([Required, StringLength(4000, MinimumLength = 1)] string Texto);

public record SuporteStatusRequest(StatusChamadoSuporte Status);

/// <summary>Mensagem do chamado; <c>AnexoUrl</c>/<c>AnexoNome</c> vêm preenchidos quando a pessoa enviou um print ou PDF.</summary>
public record SuporteMensagemDto(Guid Id, string AutorNome, bool DoSuporte, string Texto, DateTimeOffset CriadoEm, string? AnexoUrl = null, string? AnexoNome = null);

/// <param name="AguardandoSuporte">Chamado em aberto cuja última mensagem é do solicitante: está esperando resposta.</param>
/// <param name="AguardandoDesde">Desde quando está esperando (data da última mensagem do solicitante).</param>
public record SuporteChamadoResumoDto(
    Guid Id, string Assunto, string Categoria, StatusChamadoSuporte Status, Guid SolicitanteId, string SolicitanteNome,
    string? SolicitanteEmail, DateTimeOffset CriadoEm, DateTimeOffset UltimaMensagemEm, int Mensagens,
    bool AguardandoSuporte = false, DateTimeOffset? AguardandoDesde = null);

public record SuporteChamadoDto(SuporteChamadoResumoDto Resumo, IReadOnlyList<SuporteMensagemDto> Mensagens);

/// <summary><c>Atende</c>: o usuário é quem recebe e responde os chamados (vê todos).</summary>
public record SuporteListaDto(bool Atende, int Abertos, IReadOnlyList<SuporteChamadoResumoDto> Chamados);
