using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public interface IDisparoEmailService
{
    Task<ContaEmailDto?> ObterContaAsync(CancellationToken ct);
    Task<ContaEmailDto> SalvarContaAsync(ContaEmailRequest request, CancellationToken ct);
    Task RemoverContaAsync(CancellationToken ct);
    Task<DisparoEmailPreviaDto> PreviaAsync(Guid etapaId, CancellationToken ct);

    /// <summary>Coloca o disparo na fila e volta na hora; o envio acontece em segundo plano (<see cref="DisparoEmailProcessador"/>).</summary>
    Task<DisparoEmailDto> EnviarAsync(DisparoEmailRequest request, CancellationToken ct);

    /// <summary>Últimos disparos do consultor, do mais novo para o mais antigo.</summary>
    Task<IReadOnlyList<DisparoEmailDto>> HistoricoAsync(CancellationToken ct);
}

/// <summary>
/// Disparo de e-mail do consultor para a própria carteira: os leads dele (não arquivados, que não cancelaram a inscrição) com e-mail na etapa escolhida.
/// O consultor nunca escolhe os destinatários por id — a lista sai sempre do responsável do lead, então ninguém dispara para clientes de outro consultor.
/// O e-mail sai da caixa do próprio consultor (SMTP do provedor dele, com a senha de app guardada protegida), assinado com nome e telefone dele.
/// </summary>
public sealed class DisparoEmailService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    ISegredoProtetor segredos,
    IAuditSink audit) : IDisparoEmailService
{
    /// <summary>Teto por disparo: o Gmail comum limita ~500 e-mails por dia por conta; passar disso faria o provedor bloquear a caixa do consultor.</summary>
    public const int LimiteDestinatarios = 500;

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
        conta.SenhaProtegida = segredos.Proteger(senha);
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

    private IQueryable<CrmLead> LeadsComEmailNaEtapa(Guid etapaId) =>
        db.CrmLeads.AsNoTracking()
            .Where(l => !l.Arquivado && l.ResponsavelId == currentUser.UserId && l.EtapaId == etapaId && l.Email != null && l.Email != "");

    private async Task<List<(Guid LeadId, string Email, string Nome)>> DestinatariosAsync(Guid etapaId, CancellationToken ct)
    {
        var linhas = await LeadsComEmailNaEtapa(etapaId)
            .Where(l => l.EmailDescadastradoEm == null)
            .OrderBy(l => l.NomeOuRazaoSocial)
            .Select(l => new { l.Id, Email = l.Email!, Nome = l.NomeOuRazaoSocial })
            .ToListAsync(ct);

        // Um cliente com vários cards (um por veículo) recebe uma vez só.
        return linhas
            .Select(l => (LeadId: l.Id, Email: l.Email.Trim(), l.Nome))
            .Where(l => l.Email.Contains('@'))
            .DistinctBy(l => l.Email.ToLowerInvariant())
            .ToList();
    }

    public async Task<DisparoEmailPreviaDto> PreviaAsync(Guid etapaId, CancellationToken ct)
    {
        var destinatarios = await DestinatariosAsync(etapaId, ct);
        var emails = destinatarios.Select(d => d.Email.ToLowerInvariant()).ToHashSet();
        var descadastrados = (await LeadsComEmailNaEtapa(etapaId).Where(l => l.EmailDescadastradoEm != null).Select(l => l.Email!).ToListAsync(ct))
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => !emails.Contains(e))
            .Distinct()
            .Count();
        return new DisparoEmailPreviaDto(destinatarios.Count, LimiteDestinatarios, descadastrados);
    }

    public async Task<DisparoEmailDto> EnviarAsync(DisparoEmailRequest request, CancellationToken ct)
    {
        var assunto = request.Assunto.Trim();
        var mensagem = request.Mensagem.Trim();
        if (assunto.Length < 3) throw new CrmBusinessException("Escreva o assunto do e-mail.", "assunto_obrigatorio");
        if (mensagem.Length < 3) throw new CrmBusinessException("Escreva a mensagem do e-mail.", "mensagem_obrigatoria");

        var etapa = await db.CrmLeadStages.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EtapaId, ct)
            ?? throw new CrmNotFoundException("Etapa", request.EtapaId);

        if (!await db.CrmContasEmail.AnyAsync(x => x.UsuarioId == currentUser.UserId, ct))
        {
            throw new CrmBusinessException("Conecte o seu e-mail antes de disparar.", "conta_nao_configurada");
        }

        // Um disparo por vez na fila: evita mandar dois lotes pela mesma caixa ao mesmo tempo (e dobrar o consumo do limite diário do provedor).
        if (await db.CrmDisparosEmail.AnyAsync(d => d.UsuarioId == currentUser.UserId && (d.Status == StatusDisparoEmail.Pendente || d.Status == StatusDisparoEmail.Enviando), ct))
        {
            throw new CrmBusinessException("Você já tem um disparo em andamento. Espere ele terminar para começar outro.", "disparo_em_andamento");
        }

        var destinatarios = await DestinatariosAsync(request.EtapaId, ct);
        if (destinatarios.Count == 0)
        {
            throw new CrmBusinessException("Nenhum cliente seu com e-mail (e sem cancelamento de inscrição) nessa etapa.", "sem_destinatarios");
        }
        if (destinatarios.Count > LimiteDestinatarios)
        {
            throw new CrmBusinessException($"São {destinatarios.Count} clientes nessa etapa; o limite por disparo é {LimiteDestinatarios}. Peça apoio da gestão para esse volume.", "limite_excedido");
        }

        var disparo = new CrmDisparoEmail
        {
            UsuarioId = currentUser.UserId,
            EtapaId = etapa.Id,
            EtapaNome = etapa.Nome,
            Assunto = assunto,
            Mensagem = mensagem,
            Total = destinatarios.Count,
            Destinatarios = destinatarios.Select(d => new CrmDisparoEmailDestinatario { LeadId = d.LeadId, Email = d.Email, Nome = d.Nome }).ToList(),
        };
        db.CrmDisparosEmail.Add(disparo);
        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("DisparoEmailEnfileirado", nameof(CrmDisparoEmail), disparo.Id,
            new { Etapa = etapa.Nome, Assunto = assunto, Destinatarios = disparo.Total }, ct);
        return ParaDto(disparo);
    }

    public async Task<IReadOnlyList<DisparoEmailDto>> HistoricoAsync(CancellationToken ct) =>
        (await db.CrmDisparosEmail.AsNoTracking()
            .Where(d => d.UsuarioId == currentUser.UserId)
            .OrderByDescending(d => d.CriadoEm)
            .Take(30)
            .ToListAsync(ct))
        .Select(ParaDto)
        .ToList();

    internal static DisparoEmailDto ParaDto(CrmDisparoEmail d) =>
        new(d.Id, d.EtapaNome, d.Assunto, d.Status, d.Total, d.Enviados, d.Falhas, d.Erro, d.CriadoEm, d.ConcluidoEm);
}
