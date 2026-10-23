using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Envia, em lotes, os disparos que estão na fila (<see cref="CrmDisparoEmail"/>): um lote por chamada, pela caixa do consultor dono do disparo, numa só
/// conexão SMTP. Chamado em repetição por <see cref="DisparoEmailBackgroundService"/>. Cada e-mail leva o link de descadastro; quem cancelou a inscrição
/// entre o pedido e o envio é pulado. Para cada e-mail enviado o lead ganha uma nota na linha do tempo e o "último contato" é atualizado.
/// </summary>
public sealed class DisparoEmailProcessador(
    ApplicationDbContext db,
    IEmailSender emailSender,
    ISegredoProtetor segredos,
    IDescadastroEmailTokens tokens,
    IConfiguration configuracao,
    IHostEnvironment ambiente,
    ILogger<DisparoEmailProcessador> logger)
{
    public const int TamanhoLote = 40;

    /// <summary>Processa um lote do disparo mais antigo da fila. Devolve false quando não há nada na fila.</summary>
    public async Task<bool> ProcessarProximoAsync(CancellationToken ct)
    {
        var disparo = await db.CrmDisparosEmail
            .Where(d => d.Status == StatusDisparoEmail.Pendente || d.Status == StatusDisparoEmail.Enviando)
            .OrderBy(d => d.CriadoEm)
            .FirstOrDefaultAsync(ct);
        if (disparo is null) return false;

        if (disparo.Status == StatusDisparoEmail.Pendente)
        {
            disparo.Status = StatusDisparoEmail.Enviando;
            disparo.IniciadoEm ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        var pendentes = await db.CrmDisparosEmailDestinatarios
            .Where(d => d.DisparoId == disparo.Id && d.Status == StatusDestinatarioDisparo.Pendente)
            .OrderBy(d => d.Nome)
            .Take(TamanhoLote)
            .ToListAsync(ct);
        if (pendentes.Count == 0)
        {
            Concluir(disparo);
            await db.SaveChangesAsync(ct);
            return true;
        }

        var conta = await db.CrmContasEmail.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == disparo.UsuarioId, ct);
        var consultor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == disparo.UsuarioId, ct);
        if (conta is null || consultor is null)
        {
            await FalharAsync(disparo, "A conta de e-mail do consultor foi removida antes do envio.", ct);
            return true;
        }

        var baseUrl = configuracao["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            if (!ambiente.IsDevelopment())
            {
                await FalharAsync(disparo, "App:PublicBaseUrl não está configurada: sem ela o e-mail não teria o link de cancelar inscrição.", ct);
                return true;
            }
            baseUrl = "http://localhost:5299";
        }

        string senha;
        try
        {
            senha = segredos.Revelar(conta.SenhaProtegida);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogWarning(ex, "Senha da conta de e-mail do consultor {ConsultorId} ilegível (chave de proteção mudou?).", disparo.UsuarioId);
            await FalharAsync(disparo, "Não consegui ler a senha do seu e-mail. Conecte o e-mail de novo.", ct);
            return true;
        }

        // Quem cancelou a inscrição depois de o disparo entrar na fila não recebe.
        var leadIds = pendentes.Select(p => p.LeadId).ToList();
        var emailsDoLote = pendentes.Select(p => p.Email.Trim().ToLowerInvariant()).Distinct().ToList();
        var emailsCancelados = (await db.CrmLeads.AsNoTracking()
                .Where(l => l.EmailDescadastradoEm != null && l.Email != null && emailsDoLote.Contains(l.Email.ToLower()))
                .Select(l => l.Email!).ToListAsync(ct))
            .Select(e => e.Trim().ToLowerInvariant()).ToHashSet();
        var idsCancelados = (await db.CrmLeads.AsNoTracking().Where(l => leadIds.Contains(l.Id) && l.EmailDescadastradoEm != null).Select(l => l.Id).ToListAsync(ct)).ToHashSet();
        var agora = DateTimeOffset.UtcNow;
        foreach (var p in pendentes.Where(p => idsCancelados.Contains(p.LeadId) || emailsCancelados.Contains(p.Email.Trim().ToLowerInvariant())).ToList())
        {
            p.Status = StatusDestinatarioDisparo.Falha;
            disparo.Falhas++;
            pendentes.Remove(p);
        }
        await db.SaveChangesAsync(ct);

        if (pendentes.Count > 0)
        {
            var mensagens = pendentes.Select(p =>
            {
                var url = $"{baseUrl}/api/publico/descadastro?t={Uri.EscapeDataString(tokens.Gerar(p.LeadId))}";
                return new MensagemEmail(p.Email, p.Nome, DisparoEmailCorpo.SubstituirNome(disparo.Assunto, p.Nome),
                    DisparoEmailCorpo.Montar(disparo.Mensagem, p.Nome, consultor.NomeCompleto, consultor.PhoneNumber, conta.Email, url), url);
            }).ToList();

            IReadOnlyList<bool> resultado;
            try
            {
                resultado = await emailSender.EnviarLoteComContaAsync(new ContaSmtp(conta.Host, conta.Port, conta.UserName, senha, conta.Email), consultor.NomeCompleto, mensagens, ct);
            }
            catch (EmailContaException ex)
            {
                await FalharAsync(disparo, ex.Message, ct);
                return true;
            }

            var enviadosAgora = new List<CrmDisparoEmailDestinatario>();
            for (var i = 0; i < pendentes.Count; i++)
            {
                var ok = i < resultado.Count && resultado[i];
                pendentes[i].Status = ok ? StatusDestinatarioDisparo.Enviado : StatusDestinatarioDisparo.Falha;
                if (ok)
                {
                    pendentes[i].EnviadoEm = agora;
                    disparo.Enviados++;
                    enviadosAgora.Add(pendentes[i]);
                }
                else
                {
                    disparo.Falhas++;
                }
            }

            await RegistrarContatoNosLeadsAsync(disparo, consultor.Id, enviadosAgora, agora, ct);
        }

        await db.SaveChangesAsync(ct);

        if (!await db.CrmDisparosEmailDestinatarios.AnyAsync(d => d.DisparoId == disparo.Id && d.Status == StatusDestinatarioDisparo.Pendente, ct))
        {
            Concluir(disparo);
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>Cada cliente que recebeu ganha uma nota na linha do tempo e o último contato passa a ser agora.</summary>
    private async Task RegistrarContatoNosLeadsAsync(CrmDisparoEmail disparo, Guid consultorId, List<CrmDisparoEmailDestinatario> enviados, DateTimeOffset agora, CancellationToken ct)
    {
        if (enviados.Count == 0) return;
        var ids = enviados.Select(e => e.LeadId).ToList();
        foreach (var lead in await db.CrmLeads.Where(l => ids.Contains(l.Id)).ToListAsync(ct)) lead.UltimoContatoEm = agora;
        db.CrmNotes.AddRange(enviados.Select(e => new CrmNote { LeadId = e.LeadId, AutorId = consultorId, Texto = $"E-mail enviado pelo disparo: “{disparo.Assunto}”." }));
    }

    private static void Concluir(CrmDisparoEmail disparo)
    {
        disparo.ConcluidoEm = DateTimeOffset.UtcNow;
        if (disparo.Enviados == 0 && disparo.Falhas > 0)
        {
            disparo.Status = StatusDisparoEmail.Falhou;
            disparo.Erro ??= "Nenhum e-mail foi aceito pelo provedor. Confira a conta e tente de novo.";
        }
        else
        {
            disparo.Status = StatusDisparoEmail.Concluido;
        }
    }

    /// <summary>Falha do disparo inteiro (login recusado, conta removida...): o que ainda estava na fila vira falha.</summary>
    private async Task FalharAsync(CrmDisparoEmail disparo, string motivo, CancellationToken ct)
    {
        var restantes = await db.CrmDisparosEmailDestinatarios.Where(d => d.DisparoId == disparo.Id && d.Status == StatusDestinatarioDisparo.Pendente).ToListAsync(ct);
        foreach (var r in restantes) r.Status = StatusDestinatarioDisparo.Falha;
        disparo.Falhas += restantes.Count;
        disparo.Status = StatusDisparoEmail.Falhou;
        disparo.Erro = motivo.Length > 500 ? motivo[..500] : motivo;
        disparo.ConcluidoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Roda o <see cref="DisparoEmailProcessador"/> em repetição enquanto o app está no ar: lote a lote, com uma pausa entre eles para não estourar o provedor.</summary>
public sealed class DisparoEmailBackgroundService(IServiceScopeFactory scopeFactory, ILogger<DisparoEmailBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PausaEntreLotes = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PausaOciosa = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PausaAposErro = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            var pausa = PausaOciosa;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processou = await scope.ServiceProvider.GetRequiredService<DisparoEmailProcessador>().ProcessarProximoAsync(stoppingToken);
                if (processou) pausa = PausaEntreLotes;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar a fila de disparos de e-mail.");
                pausa = PausaAposErro;
            }

            try
            {
                await Task.Delay(pausa, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
