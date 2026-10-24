using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Crm;

public interface ISuporteService
{
    Task<SuporteListaDto> ListarAsync(CancellationToken ct);
    Task<SuporteChamadoDto> ObterAsync(Guid id, CancellationToken ct);
    Task<SuporteChamadoDto> CriarAsync(SuporteChamadoCreateRequest request, CancellationToken ct);
    Task<SuporteChamadoDto> ResponderAsync(Guid id, SuporteMensagemRequest request, CancellationToken ct);

    /// <summary>Resposta com print ou PDF anexado (o texto é opcional quando há anexo).</summary>
    Task<SuporteChamadoDto> ResponderComAnexoAsync(Guid id, string? texto, string nomeArquivo, long tamanho, string? contentType, Stream conteudo, CancellationToken ct);

    Task<SuporteChamadoDto> AlterarStatusAsync(Guid id, StatusChamadoSuporte status, CancellationToken ct);
}

/// <summary>Quem atende o Suporte: e-mails em <c>Suporte:Atendentes</c> (lista). Sem configuração, vale o atendente padrão (<see cref="SuporteService.EmailAtendente"/>).</summary>
public sealed class SuporteOptions
{
    public const string SectionName = "Suporte";
    public string[] Atendentes { get; set; } = [];
}

/// <summary>
/// Aba Suporte: todo usuário abre chamados e acompanha os seus; os chamados chegam aos atendentes (<c>Suporte:Atendentes</c>), que veem todos, respondem e mudam o status.
/// Novo chamado e respostas avisam por push e por e-mail (e-mail só se o SMTP do sistema estiver configurado). Respostas aceitam print ou PDF.
/// </summary>
public sealed class SuporteService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IPushService push,
    ILogger<SuporteService> logger,
    IOptions<SuporteOptions>? opcoes = null,
    IEmailSender? emailSender = null,
    IFileStorageService? armazenamento = null,
    IConfiguration? configuracao = null) : ISuporteService
{
    /// <summary>Atendente padrão, usado enquanto <c>Suporte:Atendentes</c> não estiver configurado.</summary>
    public const string EmailAtendente = "naome1248@gmail.com";

    public const long TamanhoMaximoAnexo = 5 * 1024 * 1024;
    private static readonly string[] ExtensoesDeAnexo = [".png", ".jpg", ".jpeg", ".webp", ".pdf"];

    private static readonly string[] Categorias = ["Dúvida", "Problema", "Sugestão", "Acesso", "Outro"];

    private List<string> EmailsDosAtendentes() =>
        (opcoes?.Value.Atendentes is { Length: > 0 } configurados ? configurados : [EmailAtendente])
        .Select(e => e.Trim().ToUpperInvariant())
        .Where(e => e.Length > 0)
        .Distinct()
        .ToList();

    private async Task<bool> AtendeAsync(CancellationToken ct)
    {
        var emails = EmailsDosAtendentes();
        return await db.Users.AsNoTracking().AnyAsync(u => u.Id == currentUser.UserId && emails.Contains(u.NormalizedEmail!), ct);
    }

    private async Task<List<(Guid Id, string? Email)>> AtendentesAsync(CancellationToken ct)
    {
        var emails = EmailsDosAtendentes();
        return (await db.Users.AsNoTracking().Where(u => emails.Contains(u.NormalizedEmail!)).Select(u => new { u.Id, u.Email }).ToListAsync(ct))
            .Select(u => (u.Id, u.Email))
            .ToList();
    }

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
                UltimaDoSuporte = c.Mensagens.OrderByDescending(m => m.CriadoEm).Select(m => m.DoSuporte).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var ids = linhas.Select(l => l.SolicitanteId).Distinct().ToList();
        var pessoas = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.NomeCompleto, u.Email }).ToDictionaryAsync(u => u.Id, ct);

        var chamados = linhas.Select(l =>
        {
            var aguardando = l.Status != StatusChamadoSuporte.Resolvido && !l.UltimaDoSuporte;
            return new SuporteChamadoResumoDto(
                l.Id, l.Assunto, l.Categoria, l.Status, l.SolicitanteId,
                pessoas.TryGetValue(l.SolicitanteId, out var p) ? p.NomeCompleto : "Usuário removido",
                atende && p is not null ? p.Email : null, l.CriadoEm, l.UltimaMensagemEm, l.Mensagens,
                aguardando, aguardando ? l.UltimaMensagemEm : null);
        }).ToList();

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
        await AvisarAtendentesAsync(chamado, "Novo chamado de suporte", $"{solicitante}: {chamado.Assunto}", solicitante ?? "Um usuário", request.Mensagem.Trim(), ct);

        return await MontarAsync(chamado, await AtendeAsync(ct), ct);
    }

    public async Task<SuporteChamadoDto> ResponderAsync(Guid id, SuporteMensagemRequest request, CancellationToken ct)
    {
        var atende = await AtendeAsync(ct);
        var chamado = await CarregarAsync(id, atende, ct);
        await RegistrarMensagemAsync(chamado, atende, request.Texto.Trim(), null, null, ct);
        return await MontarAsync(chamado, atende, ct);
    }

    public async Task<SuporteChamadoDto> ResponderComAnexoAsync(Guid id, string? texto, string nomeArquivo, long tamanho, string? contentType, Stream conteudo, CancellationToken ct)
    {
        if (armazenamento is null) throw new CrmBusinessException("Envio de anexos indisponível.", "anexo_indisponivel");
        if (tamanho <= 0) throw new CrmBusinessException("Selecione um arquivo.", "anexo_invalido");
        if (tamanho > TamanhoMaximoAnexo) throw new CrmBusinessException("O anexo deve ter no máximo 5 MB.", "anexo_invalido");
        var extensao = Path.GetExtension(nomeArquivo).ToLowerInvariant();
        if (!ExtensoesDeAnexo.Contains(extensao)) throw new CrmBusinessException("Anexe uma imagem (PNG, JPG ou WEBP) ou um PDF.", "anexo_invalido");
        if (!await AssinaturaDeArquivo.ConfereAsync(conteudo, extensao, ct)) throw new CrmBusinessException("O arquivo não é do tipo informado. Anexe uma imagem ou PDF válido.", "anexo_invalido");

        var atende = await AtendeAsync(ct);
        var chamado = await CarregarAsync(id, atende, ct);

        var url = await armazenamento.SalvarAsync("suporte", $"{Guid.NewGuid():N}{extensao}", conteudo, contentType, ct);
        var nomeLimpo = Path.GetFileName(nomeArquivo);
        await RegistrarMensagemAsync(chamado, atende, texto?.Trim() ?? string.Empty, url, nomeLimpo.Length > 260 ? nomeLimpo[..260] : nomeLimpo, ct);
        return await MontarAsync(chamado, atende, ct);
    }

    private async Task RegistrarMensagemAsync(CrmSuporteChamado chamado, bool atende, string texto, string? anexoUrl, string? anexoNome, CancellationToken ct)
    {
        var doSuporte = atende && chamado.SolicitanteId != currentUser.UserId;

        // Pelo DbSet (estado Added): um filho com Guid já preenchido adicionado só pela coleção vira UPDATE; o vínculo com o chamado já carregado é feito pelo EF.
        db.CrmSuporteMensagens.Add(new CrmSuporteMensagem { ChamadoId = chamado.Id, AutorId = currentUser.UserId, DoSuporte = doSuporte, Texto = texto, AnexoUrl = anexoUrl, AnexoNome = anexoNome });
        chamado.UltimaMensagemEm = DateTimeOffset.UtcNow;
        // Resposta do suporte põe em atendimento; o solicitante respondendo reabre um chamado resolvido.
        if (doSuporte && chamado.Status == StatusChamadoSuporte.Aberto) chamado.Status = StatusChamadoSuporte.EmAtendimento;
        if (!doSuporte && chamado.Status == StatusChamadoSuporte.Resolvido) chamado.Status = StatusChamadoSuporte.Aberto;
        await db.SaveChangesAsync(ct);

        var resumo = string.IsNullOrWhiteSpace(texto) ? "(anexo)" : texto;
        if (doSuporte)
        {
            await AvisarSolicitanteAsync(chamado, "Suporte respondeu", resumo, ct);
        }
        else
        {
            var quem = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.NomeCompleto).FirstOrDefaultAsync(ct) ?? "Um usuário";
            await AvisarAtendentesAsync(chamado, "Nova mensagem no suporte", chamado.Assunto, quem, resumo, ct);
        }
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
            await AvisarSolicitanteAsync(chamado, "Chamado resolvido", "O suporte marcou o seu chamado como resolvido. Se ainda precisar, responda para reabri-lo.", ct);
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

        var ultima = chamado.Mensagens.OrderByDescending(m => m.CriadoEm).FirstOrDefault();
        var aguardando = chamado.Status != StatusChamadoSuporte.Resolvido && ultima is { DoSuporte: false };
        var resumo = new SuporteChamadoResumoDto(
            chamado.Id, chamado.Assunto, chamado.Categoria, chamado.Status, chamado.SolicitanteId, Nome(chamado.SolicitanteId),
            atende && pessoas.TryGetValue(chamado.SolicitanteId, out var s) ? s.Email : null,
            chamado.CriadoEm, chamado.UltimaMensagemEm, chamado.Mensagens.Count,
            aguardando, aguardando ? chamado.UltimaMensagemEm : null);
        var mensagens = chamado.Mensagens.OrderBy(m => m.CriadoEm)
            .Select(m => new SuporteMensagemDto(m.Id, m.DoSuporte ? "Suporte" : Nome(m.AutorId), m.DoSuporte, m.Texto, m.CriadoEm, m.AnexoUrl, m.AnexoNome)).ToList();
        return new SuporteChamadoDto(resumo, mensagens);
    }

    private async Task AvisarAtendentesAsync(CrmSuporteChamado chamado, string titulo, string corpoPush, string quem, string trecho, CancellationToken ct)
    {
        var atendentes = await AtendentesAsync(ct);
        foreach (var (id, email) in atendentes.Where(a => a.Id != currentUser.UserId))
        {
            await AvisarPushAsync(id, new PushMensagem(titulo, corpoPush, $"/app/suporte?chamado={chamado.Id}", $"suporte-{chamado.Id}"), ct);
            await AvisarPorEmailAsync(email, $"{titulo}: {chamado.Assunto}", $"{quem} escreveu no chamado “{chamado.Assunto}”:", trecho, chamado.Id, ct);
        }
    }

    private async Task AvisarSolicitanteAsync(CrmSuporteChamado chamado, string titulo, string trecho, CancellationToken ct)
    {
        await AvisarPushAsync(chamado.SolicitanteId, new PushMensagem(titulo, chamado.Assunto, $"/app/suporte?chamado={chamado.Id}", $"suporte-{chamado.Id}"), ct);
        var email = await db.Users.AsNoTracking().Where(u => u.Id == chamado.SolicitanteId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        await AvisarPorEmailAsync(email, $"{titulo}: {chamado.Assunto}", $"{titulo} no chamado “{chamado.Assunto}”:", trecho, chamado.Id, ct);
    }

    private async Task AvisarPushAsync(Guid usuarioId, PushMensagem mensagem, CancellationToken ct)
    {
        try { await push.EnviarAsync(usuarioId, mensagem, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Falha ao avisar o usuário {UsuarioId} do suporte.", usuarioId); }
    }

    /// <summary>Aviso por e-mail pelo SMTP do sistema. Nunca atrapalha o chamado: sem SMTP configurado ou com falha, só vai para o log.</summary>
    private async Task AvisarPorEmailAsync(string? para, string assunto, string introducao, string trecho, Guid chamadoId, CancellationToken ct)
    {
        if (emailSender is null || string.IsNullOrWhiteSpace(para)) return;
        try
        {
            var baseUrl = configuracao?["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
            var link = string.IsNullOrEmpty(baseUrl) ? "" : $"<p><a href=\"{DisparoEmailCorpo.Escapar($"{baseUrl}/app/suporte?chamado={chamadoId}")}\">Abrir o chamado no CRM</a></p>";
            var resumo = trecho.Length > 400 ? trecho[..400] + "…" : trecho;
            var corpo = $"<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#222\"><p>{DisparoEmailCorpo.Escapar(introducao)}</p>" +
                        $"<blockquote style=\"margin:0 0 12px;padding-left:12px;border-left:3px solid #ccc;color:#555\">{DisparoEmailCorpo.Escapar(resumo).Replace("\n", "<br>")}</blockquote>{link}</div>";
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(15));
            await emailSender.EnviarAsync(para, para, assunto, corpo, limite.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falha ao avisar {Email} do suporte por e-mail.", para);
        }
    }
}
