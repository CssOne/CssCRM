using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public interface IDisparoEmailService
{
    Task<ContaEmailDto?> ObterContaAsync(CancellationToken ct);
    Task<ContaEmailDto> SalvarContaAsync(ContaEmailRequest request, CancellationToken ct);
    Task RemoverContaAsync(CancellationToken ct);
    Task<DisparoEmailPreviaDto> PreviaAsync(Guid etapaId, CancellationToken ct);
    Task<DisparoEmailResultadoDto> EnviarAsync(DisparoEmailRequest request, CancellationToken ct);
}

/// <summary>
/// Disparo de e-mail do consultor para a própria carteira: os leads dele (não arquivados) com e-mail na etapa escolhida. O consultor nunca
/// escolhe os destinatários por id — a lista sai sempre do responsável do lead, então ninguém dispara para clientes de outro consultor.
/// O e-mail sai da caixa do próprio consultor (SMTP do provedor dele, com a senha de app guardada protegida), assinado com nome e telefone dele.
/// </summary>
public sealed class DisparoEmailService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    IDataProtectionProvider protecao,
    IAuditSink audit,
    ILogger<DisparoEmailService> logger) : IDisparoEmailService
{
    /// <summary>Teto por disparo: o envio é feito dentro da requisição, e um volume maior pede fila/provedor de e-mail em massa.</summary>
    public const int LimiteDestinatarios = 300;

    private IDataProtector Protetor => protecao.CreateProtector("CssVision.CrmContaEmail");

    private static (string Host, int Port) ServidorPadrao(string email)
    {
        var dominio = email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant();
        return dominio switch
        {
            "gmail.com" or "googlemail.com" => ("smtp.gmail.com", 587),
            "outlook.com" or "hotmail.com" or "live.com" or "msn.com" or "outlook.com.br" or "hotmail.com.br" => ("smtp-mail.outlook.com", 587),
            "yahoo.com" or "yahoo.com.br" => ("smtp.mail.yahoo.com", 587),
            _ => ("", 587),
        };
    }

    public async Task<ContaEmailDto?> ObterContaAsync(CancellationToken ct)
    {
        var c = await db.CrmContasEmail.AsNoTracking().FirstOrDefaultAsync(x => x.UsuarioId == currentUser.UserId, ct);
        return c is null ? null : new ContaEmailDto(c.Email, c.Host, c.Port);
    }

    public async Task<ContaEmailDto> SalvarContaAsync(ContaEmailRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        var senha = request.Senha.Trim();
        var padrao = ServidorPadrao(email);
        var host = string.IsNullOrWhiteSpace(request.Host) ? padrao.Host : request.Host.Trim();
        var port = request.Port is > 0 and <= 65535 ? request.Port.Value : padrao.Port;
        if (host.Length == 0)
        {
            throw new CrmBusinessException("Não reconheci o provedor desse e-mail. Informe o servidor SMTP (ex.: smtp.seudominio.com.br) e a porta.", "servidor_obrigatorio");
        }

        // Testa o login antes de guardar: o consultor descobre na hora se a senha de app está errada.
        try
        {
            await emailSender.ValidarContaAsync(new ContaSmtp(host, port, email, senha, email), ct);
        }
        catch (EmailContaException ex)
        {
            throw new CrmBusinessException(ex.Message, "conta_invalida");
        }

        var conta = await db.CrmContasEmail.FirstOrDefaultAsync(x => x.UsuarioId == currentUser.UserId, ct);
        if (conta is null)
        {
            conta = new CrmContaEmail { UsuarioId = currentUser.UserId };
            db.CrmContasEmail.Add(conta);
        }
        conta.Email = email;
        conta.Host = host;
        conta.Port = port;
        conta.UserName = email;
        conta.SenhaProtegida = Protetor.Protect(senha);
        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("ContaEmailConfigurada", nameof(CrmContaEmail), conta.Id, new { conta.Email, conta.Host }, ct);
        return new ContaEmailDto(conta.Email, conta.Host, conta.Port);
    }

    public async Task RemoverContaAsync(CancellationToken ct)
    {
        var conta = await db.CrmContasEmail.FirstOrDefaultAsync(x => x.UsuarioId == currentUser.UserId, ct);
        if (conta is null) return;
        db.CrmContasEmail.Remove(conta);
        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("ContaEmailRemovida", nameof(CrmContaEmail), conta.Id, new { conta.Email }, ct);
    }

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

        var conta = await db.CrmContasEmail.AsNoTracking().FirstOrDefaultAsync(x => x.UsuarioId == currentUser.UserId, ct)
            ?? throw new CrmBusinessException("Conecte o seu e-mail antes de disparar.", "conta_nao_configurada");
        var consultor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct)
            ?? throw new CrmBusinessException("Usuário não encontrado.", "consultor_inexistente");

        var destinatarios = await DestinatariosAsync(request.EtapaId, ct);
        if (destinatarios.Count == 0)
        {
            throw new CrmBusinessException("Nenhum cliente seu com e-mail nessa etapa.", "sem_destinatarios");
        }
        if (destinatarios.Count > LimiteDestinatarios)
        {
            throw new CrmBusinessException($"São {destinatarios.Count} clientes nessa etapa; o limite por disparo é {LimiteDestinatarios}. Peça apoio da gestão para esse volume.", "limite_excedido");
        }

        string senha;
        try
        {
            senha = Protetor.Unprotect(conta.SenhaProtegida);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogWarning(ex, "Senha da conta de e-mail do consultor {ConsultorId} ilegível (chaves de proteção mudaram?).", currentUser.UserId);
            throw new CrmBusinessException("Não consegui ler a senha do seu e-mail. Conecte o e-mail de novo.", "conta_invalida");
        }

        var mensagens = destinatarios
            .Select(d => new MensagemEmail(d.Email, d.Nome, SubstituirNome(assunto, d.Nome), MontarCorpo(mensagem, d.Nome, consultor.NomeCompleto, consultor.PhoneNumber, conta.Email)))
            .ToList();

        int enviados;
        try
        {
            enviados = await emailSender.EnviarLoteComContaAsync(new ContaSmtp(conta.Host, conta.Port, conta.UserName, senha, conta.Email), consultor.NomeCompleto, mensagens, ct);
        }
        catch (EmailContaException ex)
        {
            throw new CrmBusinessException(ex.Message, "conta_invalida");
        }

        var falhas = destinatarios.Count - enviados;
        if (enviados == 0)
        {
            throw new CrmBusinessException("Nenhum e-mail foi aceito pelo seu provedor. Confira a conta e tente de novo.", "envio_indisponivel");
        }

        await audit.RegistrarAsync("DisparoEmailEnviado", nameof(CrmLeadStage), etapa.Id,
            new { Etapa = etapa.Nome, Assunto = assunto, Destinatarios = destinatarios.Count, Enviados = enviados, Falhas = falhas }, ct);
        return new DisparoEmailResultadoDto(destinatarios.Count, enviados, falhas);
    }

    /// <summary>Escapa só o que é markup — mantém acentos legíveis no HTML (WebUtility.HtmlEncode viraria "&amp;#225;").</summary>
    private static string Escapar(string texto) => texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>Link que abre a conversa no WhatsApp do consultor (telefone BR com DDD, 10 ou 11 dígitos); nulo se o telefone não serve.</summary>
    internal static string? LinkWhatsApp(string? telefone, string nomeConsultor)
    {
        var digitos = DocumentValidation.NormalizarTelefone(telefone);
        if (digitos is not { Length: 10 or 11 }) return null;
        var texto = Uri.EscapeDataString($"Olá {nomeConsultor.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}, recebi o seu e-mail e quero falar com você.");
        return $"https://wa.me/55{digitos}?text={texto}";
    }

    /// <summary>Texto simples do consultor vira HTML seguro (escapado); {nome} é trocado pelo primeiro nome do cliente; a assinatura leva nome e telefone do consultor.</summary>
    private static string PrimeiroNome(string nomeCliente) => nomeCliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nomeCliente;

    /// <summary>{nome} vira o primeiro nome do cliente — vale para o assunto e para a mensagem.</summary>
    private static string SubstituirNome(string texto, string nomeCliente) => texto.Replace("{nome}", PrimeiroNome(nomeCliente), StringComparison.OrdinalIgnoreCase);

    private static string MontarCorpo(string mensagem, string nomeCliente, string nomeConsultor, string? telefoneConsultor, string emailConsultor)
    {
        var texto = Escapar(SubstituirNome(mensagem, nomeCliente))
            .Replace("\r\n", "\n").Replace("\n", "<br>");
        var linhaTelefone = string.IsNullOrWhiteSpace(telefoneConsultor) ? "" : $"<br>Telefone/WhatsApp: {Escapar(telefoneConsultor)}";
        var whatsapp = LinkWhatsApp(telefoneConsultor, nomeConsultor);
        var botaoWhatsApp = whatsapp is null ? "" : $"<p><a href=\"{whatsapp}\" style=\"display:inline-block;background:#25D366;color:#fff;text-decoration:none;font-weight:bold;font-size:14px;padding:10px 18px;border-radius:6px\">Chamar no WhatsApp</a></p>";
        return $"""
            <div style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#222">
              <p>{texto}</p>
              <hr style="border:none;border-top:1px solid #ddd;margin:24px 0 12px">
              {botaoWhatsApp}
              <p style="font-size:13px;color:#666"><strong>{Escapar(nomeConsultor)}</strong> · CSS Brasil{linhaTelefone}<br>{Escapar(emailConsultor)}</p>
            </div>
            """;
    }
}
