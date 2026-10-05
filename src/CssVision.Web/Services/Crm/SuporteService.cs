using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public interface ISuporteService
{
    Task<SuporteListaDto> ListarAsync(CancellationToken ct);
    Task<SuporteChamadoDto> ObterAsync(Guid id, CancellationToken ct);
    Task<SuporteChamadoDto> CriarAsync(SuporteChamadoCreateRequest request, CancellationToken ct);
    Task<SuporteChamadoDto> ResponderAsync(Guid id, SuporteMensagemRequest request, CancellationToken ct);
    Task<SuporteChamadoDto> AlterarStatusAsync(Guid id, StatusChamadoSuporte status, CancellationToken ct);
}

/// <summary>
/// Aba Suporte: todo usuário abre chamados e acompanha os seus; os chamados chegam ao atendente
/// (<see cref="EmailAtendente"/>), que vê todos, responde e muda o status. Novo chamado e respostas avisam por push.
/// </summary>
public sealed class SuporteService(ApplicationDbContext db, ICurrentUserService currentUser, IPushService push, ILogger<SuporteService> logger) : ISuporteService
{
    public const string EmailAtendente = "naome1248@gmail.com";
    private static readonly string EmailAtendenteNormalizado = EmailAtendente.ToUpperInvariant();

    private static readonly string[] Categorias = ["Dúvida", "Problema", "Sugestão", "Acesso", "Outro"];

    private async Task<bool> AtendeAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().AnyAsync(u => u.Id == currentUser.UserId && u.NormalizedEmail == EmailAtendenteNormalizado, ct);

    private Task<Guid?> IdDoAtendenteAsync(CancellationToken ct) =>
        db.Users.AsNoTracking().Where(u => u.NormalizedEmail == EmailAtendenteNormalizado).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);

    public async Task<SuporteListaDto> ListarAsync(CancellationToken ct)
    {
        var atende = await AtendeAsync(ct);
        var query = db.CrmSuporteChamados.AsNoTracking().AsQueryable();
        if (!atende) query = query.Where(c => c.SolicitanteId == currentUser.UserId);

        var linhas = await query
            .OrderBy(c => c.Status == StatusChamadoSuporte.Resolvido).ThenByDescending(c => c.UltimaMensagemEm)
            .Take(300)
            .Select(c => new
            {
                c.Id, c.Assunto, c.Categoria, c.Status, c.SolicitanteId, c.CriadoEm, c.UltimaMensagemEm, Mensagens = c.Mensagens.Count,
            })
            .ToListAsync(ct);

        var ids = linhas.Select(l => l.SolicitanteId).Distinct().ToList();
        var pessoas = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.NomeCompleto, u.Email }).ToDictionaryAsync(u => u.Id, ct);

        var chamados = linhas.Select(l => new SuporteChamadoResumoDto(
            l.Id, l.Assunto, l.Categoria, l.Status, l.SolicitanteId,
            pessoas.TryGetValue(l.SolicitanteId, out var p) ? p.NomeCompleto : "Usuário removido",
            atende && p is not null ? p.Email : null, l.CriadoEm, l.UltimaMensagemEm, l.Mensagens)).ToList();

        return new SuporteListaDto(atende, chamados.Count(c => c.Status != StatusChamadoSuporte.Resolvido), chamados);
    }

    public async Task<SuporteChamadoDto> ObterAsync(Guid id, CancellationToken ct)
    {
        var atende = await AtendeAsync(ct);
        return await MontarAsync(await CarregarAsync(id, atende, ct), atende, ct);
    }

    public async Task<SuporteChamadoDto> CriarAsync(SuporteChamadoCreateRequest request, CancellationToken ct)
    {
        var categoria = Categorias.FirstOrDefault(c => string.Equals(c, request.Categoria?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Outro";
        var chamado = new CrmSuporteChamado
        {
            Assunto = request.Assunto.Trim(),
            Categoria = categoria,
            SolicitanteId = currentUser.UserId,
            UltimaMensagemEm = DateTimeOffset.UtcNow,
            Mensagens = [new CrmSuporteMensagem { AutorId = currentUser.UserId, Texto = request.Mensagem.Trim() }],
        };
        db.CrmSuporteChamados.Add(chamado);
        await db.SaveChangesAsync(ct);

        var solicitante = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.NomeCompleto).FirstOrDefaultAsync(ct);
        var atendente = await IdDoAtendenteAsync(ct);
        if (atendente is not null && atendente != currentUser.UserId)
        {
            await AvisarAsync(atendente.Value, new PushMensagem("Novo chamado de suporte", $"{solicitante}: {chamado.Assunto}", $"/app/suporte?chamado={chamado.Id}", $"suporte-{chamado.Id}"), ct);
        }

        return await MontarAsync(chamado, await AtendeAsync(ct), ct);
    }

    public async Task<SuporteChamadoDto> ResponderAsync(Guid id, SuporteMensagemRequest request, CancellationToken ct)
    {
        var atende = await AtendeAsync(ct);
        var chamado = await CarregarAsync(id, atende, ct);
        var doSuporte = atende && chamado.SolicitanteId != currentUser.UserId;

        // Pelo DbSet (estado Added): um filho com Guid já preenchido adicionado só pela coleção vira UPDATE; o vínculo com o chamado já carregado é feito pelo EF.
        db.CrmSuporteMensagens.Add(new CrmSuporteMensagem { ChamadoId = chamado.Id, AutorId = currentUser.UserId, DoSuporte = doSuporte, Texto = request.Texto.Trim() });
        chamado.UltimaMensagemEm = DateTimeOffset.UtcNow;
        // Resposta do suporte põe em atendimento; o solicitante respondendo reabre um chamado resolvido.
        if (doSuporte && chamado.Status == StatusChamadoSuporte.Aberto) chamado.Status = StatusChamadoSuporte.EmAtendimento;
        if (!doSuporte && chamado.Status == StatusChamadoSuporte.Resolvido) chamado.Status = StatusChamadoSuporte.Aberto;
        await db.SaveChangesAsync(ct);

        if (doSuporte)
        {
            await AvisarAsync(chamado.SolicitanteId, new PushMensagem("Suporte respondeu", chamado.Assunto, $"/app/suporte?chamado={chamado.Id}", $"suporte-{chamado.Id}"), ct);
        }
        else if (await IdDoAtendenteAsync(ct) is { } atendente && atendente != currentUser.UserId)
        {
            await AvisarAsync(atendente, new PushMensagem("Nova mensagem no suporte", chamado.Assunto, $"/app/suporte?chamado={chamado.Id}", $"suporte-{chamado.Id}"), ct);
        }

        return await MontarAsync(chamado, atende, ct);
    }

    public async Task<SuporteChamadoDto> AlterarStatusAsync(Guid id, StatusChamadoSuporte status, CancellationToken ct)
    {
        if (!Enum.IsDefined(status)) throw new CrmBusinessException("Status inválido.", "status_invalido");

        var atende = await AtendeAsync(ct);
        var chamado = await CarregarAsync(id, atende, ct);
        // O solicitante só pode encerrar (ou reabrir) o próprio chamado; colocar em atendimento é do suporte.
        if (!atende && status == StatusChamadoSuporte.EmAtendimento)
        {
            throw new CrmForbiddenException("Apenas o suporte pode colocar o chamado em atendimento.");
        }

        chamado.Status = status;
        await db.SaveChangesAsync(ct);

        if (atende && chamado.SolicitanteId != currentUser.UserId && status == StatusChamadoSuporte.Resolvido)
        {
            await AvisarAsync(chamado.SolicitanteId, new PushMensagem("Chamado resolvido", chamado.Assunto, $"/app/suporte?chamado={chamado.Id}", $"suporte-{chamado.Id}"), ct);
        }

        return await MontarAsync(chamado, atende, ct);
    }

    private async Task<CrmSuporteChamado> CarregarAsync(Guid id, bool atende, CancellationToken ct)
    {
        var chamado = await db.CrmSuporteChamados.Include(c => c.Mensagens).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new CrmNotFoundException("Chamado", id);
        // Chamado de outra pessoa se comporta como inexistente (não revela que existe).
        if (!atende && chamado.SolicitanteId != currentUser.UserId) throw new CrmNotFoundException("Chamado", id);
        return chamado;
    }

    private async Task<SuporteChamadoDto> MontarAsync(CrmSuporteChamado chamado, bool atende, CancellationToken ct)
    {
        var ids = chamado.Mensagens.Select(m => m.AutorId).Append(chamado.SolicitanteId).Distinct().ToList();
        var pessoas = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.NomeCompleto, u.Email }).ToDictionaryAsync(u => u.Id, ct);
        string Nome(Guid id) => pessoas.TryGetValue(id, out var p) ? p.NomeCompleto : "Usuário removido";

        var resumo = new SuporteChamadoResumoDto(
            chamado.Id, chamado.Assunto, chamado.Categoria, chamado.Status, chamado.SolicitanteId, Nome(chamado.SolicitanteId),
            atende && pessoas.TryGetValue(chamado.SolicitanteId, out var s) ? s.Email : null,
            chamado.CriadoEm, chamado.UltimaMensagemEm, chamado.Mensagens.Count);
        var mensagens = chamado.Mensagens.OrderBy(m => m.CriadoEm)
            .Select(m => new SuporteMensagemDto(m.Id, m.DoSuporte ? "Suporte" : Nome(m.AutorId), m.DoSuporte, m.Texto, m.CriadoEm)).ToList();
        return new SuporteChamadoDto(resumo, mensagens);
    }

    private async Task AvisarAsync(Guid usuarioId, PushMensagem mensagem, CancellationToken ct)
    {
        try { await push.EnviarAsync(usuarioId, mensagem, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao avisar o usuário {UsuarioId} do suporte.", usuarioId); }
    }
}
