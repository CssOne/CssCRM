using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Carga única que copia, para o cadastro do cliente (lead), os dados que foram digitados só na oportunidade antes da correção do card
/// (CPF, estado, canal de aquisição e placa) — o card do cliente lê o cadastro do lead e ficava em branco. Mesmas regras de
/// <c>OpportunityService.CompletarCadastroDoLeadAsync</c>: só preenche o que o lead não tem, nunca sobrescreve, o canal só completa quem já é Indicação (cadastro manual) e nunca com "Lead", o CPF/CNPJ só entra se for válido
/// e nenhum outro cliente ativo o usa. Usa a oportunidade mais recente que tenha o dado. Roda uma vez (marcador em CrmParametros); falha só vai para o log.
/// </summary>
public sealed class PreencherCadastroDosLeadsBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<PreencherCadastroDosLeadsBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "crm:cadastro-leads-preenchido-v2-em";
    public const string ChaveStatus = "notion:cadastro-leads-preenchimento-v2";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(2);
    private const int TamanhoDoLote = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(120));

            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var (analisados, atualizados) = await ExecutarAsync(db, stoppingToken);
            var resumo = $"{DateTimeOffset.UtcNow:O} | leads analisados: {analisados}; leads atualizados: {atualizados}";
            await GravarAsync(db, ChaveStatus, resumo, stoppingToken);
            await GravarAsync(db, ChaveExecutado, resumo, stoppingToken);
            logger.LogInformation("Cadastro dos leads: {Analisados} analisado(s), {Atualizados} atualizado(s).", analisados, atualizados);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao preencher o cadastro dos leads com os dados das oportunidades — o app segue; tenta de novo na próxima inicialização.");
        }
    }

    public static async Task<(int Analisados, int Atualizados)> ExecutarAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var ids = await db.CrmLeads.AsNoTracking()
            .Where(l => !l.Arquivado && (
                ((l.Estado == null || l.Estado == "" || l.Estado.Trim() == "") && db.CrmOpportunities.Any(o => o.LeadId == l.Id && !o.Arquivado && o.Estado != null))
                || ((l.Placa == null || l.Placa == "" || l.Placa.Trim() == "") && db.CrmOpportunities.Any(o => o.LeadId == l.Id && !o.Arquivado && o.Veiculo != null && o.Veiculo.Placa != null))
                || ((l.TipoIndicacao == null || l.TipoIndicacao == "" || l.TipoIndicacao.Trim() == "") && l.CriadoManualmente && db.CrmOpportunities.Any(o => o.LeadId == l.Id && !o.Arquivado && o.TipoIndicacao != null))
                || ((l.DocumentoNormalizado == null || l.DocumentoNormalizado == "") && db.CrmOpportunities.Any(o => o.LeadId == l.Id && !o.Arquivado && o.Cpf != null))))
            .Select(l => l.Id)
            .ToListAsync(ct);

        int atualizados = 0;
        foreach (var leadId in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (await PreencherAsync(db, leadId, ct)) atualizados++;
        }
        return (ids.Count, atualizados);
    }

    private static async Task<bool> PreencherAsync(ApplicationDbContext db, Guid leadId, CancellationToken ct)
    {
        var lead = await db.CrmLeads.AsNoTracking().Where(l => l.Id == leadId)
            .Select(l => new { l.Estado, l.Placa, l.TipoIndicacao, l.CriadoManualmente, l.DocumentoNormalizado }).FirstOrDefaultAsync(ct);
        if (lead is null) return false;

        var vendas = db.CrmOpportunities.AsNoTracking().Where(o => o.LeadId == leadId && !o.Arquivado).OrderByDescending(o => o.CriadoEm);
        var alterou = false;

        string? estado = null, placa = null, tipo = null, documento = null;

        if (string.IsNullOrWhiteSpace(lead.Estado))
        {
            var e = await vendas.Where(o => o.Estado != null && o.Estado != "").Select(o => o.Estado).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(e) && e.Trim().Length <= 2) estado = e.Trim().ToUpperInvariant();
        }
        if (string.IsNullOrWhiteSpace(lead.Placa))
        {
            var p = await vendas.Where(o => o.Veiculo != null && o.Veiculo.Placa != null && o.Veiculo.Placa != "").Select(o => o.Veiculo!.Placa).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(p) && p.Trim().Length <= 10) placa = p.Trim().ToUpperInvariant();
        }
        if (lead.CriadoManualmente && string.IsNullOrWhiteSpace(lead.TipoIndicacao))
        {
            var t = await vendas.Where(o => o.TipoIndicacao != null && o.TipoIndicacao != "").Select(o => o.TipoIndicacao).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(t) && t.Trim().Length <= 80 && !TipoIndicacaoLead.EhLead(t)) tipo = t.Trim();
        }
        if (string.IsNullOrEmpty(lead.DocumentoNormalizado))
        {
            var cpf = await vendas.Where(o => o.Cpf != null && o.Cpf != "").Select(o => o.Cpf).FirstOrDefaultAsync(ct);
            var normalizado = DocumentValidation.NormalizarDocumento(cpf, out var valido);
            if (valido && normalizado is not null
                && !await db.CrmLeads.AnyAsync(l => l.Id != leadId && l.DocumentoNormalizado == normalizado && !l.Arquivado && l.VeiculoAdicionalDeLeadId == null, ct))
            {
                documento = normalizado;
            }
        }

        // Cada campo só é gravado se ainda estiver vazio no banco (a condição vai no UPDATE, não só na leitura acima).
        if (estado is not null) alterou |= await db.CrmLeads.Where(l => l.Id == leadId && (l.Estado == null || l.Estado.Trim() == "")).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, estado), ct) > 0;
        if (placa is not null) alterou |= await db.CrmLeads.Where(l => l.Id == leadId && (l.Placa == null || l.Placa.Trim() == "")).ExecuteUpdateAsync(s => s.SetProperty(l => l.Placa, placa), ct) > 0;
        if (tipo is not null) alterou |= await db.CrmLeads.Where(l => l.Id == leadId && (l.TipoIndicacao == null || l.TipoIndicacao.Trim() == "")).ExecuteUpdateAsync(s => s.SetProperty(l => l.TipoIndicacao, tipo), ct) > 0;
        if (documento is not null) alterou |= await db.CrmLeads.Where(l => l.Id == leadId && (l.DocumentoNormalizado == null || l.DocumentoNormalizado == "")).ExecuteUpdateAsync(s => s.SetProperty(l => l.DocumentoNormalizado, documento), ct) > 0;
        return alterou;
    }

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
