using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public interface IDisparoEmailService
{
    Task<DisparoEmailPreviaDto> PreviaAsync(Guid etapaId, CancellationToken ct);
    Task<DisparoEmailResultadoDto> EnviarAsync(DisparoEmailRequest request, CancellationToken ct);
}

/// <summary>
/// Disparo de e-mail do consultor para a própria carteira: os leads dele (não arquivados) com e-mail na etapa escolhida. O consultor nunca
/// escolhe os destinatários por id — a lista sai sempre do responsável do lead, então ninguém dispara para clientes de outro consultor.
/// O e-mail sai pelo SMTP da empresa com o nome do consultor e o e-mail dele em "Responder para".
/// </summary>
public sealed class DisparoEmailService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    IAuditSink audit,
    ILogger<DisparoEmailService> logger) : IDisparoEmailService
{
    /// <summary>Teto por disparo: o envio é feito dentro da requisição, e um volume maior pede fila/provedor de e-mail em massa.</summary>
    public const int LimiteDestinatarios = 300;

    private async Task<List<(string Email, string Nome)>> DestinatariosAsync(Guid etapaId, CancellationToken ct)
    {
        var linhas = await db.CrmLeads.AsNoTracking()
            .Where(l => !l.Arquivado && l.ResponsavelId == currentUser.UserId && l.EtapaId == etapaId && l.Email != null && l.Email != "")
            .OrderBy(l => l.NomeOuRazaoSocial)
            .Select(l => new { Email = l.Email!, Nome = l.NomeOuRazaoSocial })
            .ToListAsync(ct);

        // Um cliente com vários cards (um por veículo) recebe uma vez só.
        return linhas
            .Select(l => (Email: l.Email.Trim(), l.Nome))
            .Where(l => l.Email.Contains('@'))
            .DistinctBy(l => l.Email.ToLowerInvariant())
            .ToList();
    }

    public async Task<DisparoEmailPreviaDto> PreviaAsync(Guid etapaId, CancellationToken ct) =>
        new((await DestinatariosAsync(etapaId, ct)).Count, LimiteDestinatarios);

    public async Task<DisparoEmailResultadoDto> EnviarAsync(DisparoEmailRequest request, CancellationToken ct)
    {
        var assunto = request.Assunto.Trim();
        var mensagem = request.Mensagem.Trim();
        if (assunto.Length < 3) throw new CrmBusinessException("Escreva o assunto do e-mail.", "assunto_obrigatorio");
        if (mensagem.Length < 3) throw new CrmBusinessException("Escreva a mensagem do e-mail.", "mensagem_obrigatoria");

        var etapa = await db.CrmLeadStages.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EtapaId, ct)
            ?? throw new CrmNotFoundException("Etapa", request.EtapaId);

        var consultor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        if (consultor is null || string.IsNullOrWhiteSpace(consultor.Email))
        {
            throw new CrmBusinessException("Seu usuário não tem e-mail cadastrado para receber as respostas.", "consultor_sem_email");
        }

        var destinatarios = await DestinatariosAsync(request.EtapaId, ct);
        if (destinatarios.Count == 0)
        {
            throw new CrmBusinessException("Nenhum cliente seu com e-mail nessa etapa.", "sem_destinatarios");
        }
        if (destinatarios.Count > LimiteDestinatarios)
        {
            throw new CrmBusinessException($"São {destinatarios.Count} clientes nessa etapa; o limite por disparo é {LimiteDestinatarios}. Peça apoio da gestão para esse volume.", "limite_excedido");
        }

        var enviados = 0;
        foreach (var (email, nome) in destinatarios)
        {
            ct.ThrowIfCancellationRequested();
            var corpo = MontarCorpo(mensagem, nome, consultor.NomeCompleto, consultor.Email!);
            if (await emailSender.EnviarEmNomeDeAsync(email, nome, assunto, corpo, consultor.NomeCompleto, consultor.Email!, ct)) enviados++;
        }

        var falhas = destinatarios.Count - enviados;
        if (enviados == 0)
        {
            logger.LogWarning("Disparo de e-mail do consultor {ConsultorId} não enviou nenhum e-mail (SMTP não configurado ou fora do ar).", currentUser.UserId);
            throw new CrmBusinessException("Não foi possível enviar os e-mails agora (servidor de e-mail indisponível). Tente de novo mais tarde.", "envio_indisponivel");
        }

        await audit.RegistrarAsync("DisparoEmailEnviado", nameof(Domain.Crm.CrmLeadStage), etapa.Id,
            new { Etapa = etapa.Nome, Assunto = assunto, Destinatarios = destinatarios.Count, Enviados = enviados, Falhas = falhas }, ct);
        return new DisparoEmailResultadoDto(destinatarios.Count, enviados, falhas);
    }

    /// <summary>Escapa só o que é markup — mantém acentos legíveis no HTML (WebUtility.HtmlEncode viraria "&#225;").</summary>
    private static string Escapar(string texto) => texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>Texto simples do consultor vira HTML seguro (escapado); {nome} é trocado pelo primeiro nome do cliente.</summary>
    private static string MontarCorpo(string mensagem, string nomeCliente, string nomeConsultor, string emailConsultor)
    {
        var primeiroNome = nomeCliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nomeCliente;
        var texto = Escapar(mensagem.Replace("{nome}", primeiroNome, StringComparison.OrdinalIgnoreCase))
            .Replace("\r\n", "\n").Replace("\n", "<br>");
        return $"""
            <div style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#222">
              <p>{texto}</p>
              <hr style="border:none;border-top:1px solid #ddd;margin:24px 0 12px">
              <p style="font-size:13px;color:#666">{Escapar(nomeConsultor)} · CSS Brasil<br>Responda este e-mail para falar diretamente comigo ({Escapar(emailConsultor)}).</p>
            </div>
            """;
    }
}
