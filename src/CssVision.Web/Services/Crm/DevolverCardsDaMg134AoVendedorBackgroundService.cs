using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Correção pontual (14/10/2026) de cards da MG134 que o Notion entrega a um consultor ativo mas que ficaram arquivados pelo sistema, parados em
/// "Vendedor não identificado (MG134)": a volta da MG134 (ReativarLeadsMg134BackgroundService) deixou para trás os que repetiam e-mail/CPF de outro lead
/// ativo, e a sincronização não os reprocessa porque o card não foi editado depois. Cada item é um lead identificado na produção com o vendedor do card
/// no Notion. Só age se o lead ainda estiver arquivado pelo sistema (nunca por uma pessoa), ainda com o vendedor provisório e sem outro lead ativo com o
/// mesmo e-mail/CPF; senão deixa como está e registra o motivo. Roda uma vez (marcador em CrmParametros); falha só vai para o log.
/// </summary>
public sealed class DevolverCardsDaMg134AoVendedorBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DevolverCardsDaMg134AoVendedorBackgroundService> logger) : BackgroundService
{
    public const string ChaveExecutado = "mg134:cards-devolvidos-ao-vendedor-em";
    public const string ChaveStatus = "notion:mg134-cards-devolvidos";

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(3);

    /// <summary>Lead (arquivado pelo sistema) e o nome do consultor que é o vendedor do card no Notion.</summary>
    private static readonly (Guid LeadId, string Cliente, string Consultor)[] Cards =
    [
        (Guid.Parse("53123650-a359-4896-a155-8745107df648"), "Jose Brune ray", "Nayrana Abreu"),
        (Guid.Parse("bb857aa3-b8dc-4d41-85b7-fb965aa2faab"), "Paulo Cesar de jesus messias", "carolina gomes"),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(60));

            if (await db.CrmParametros.AsNoTracking().AnyAsync(p => p.Chave == ChaveExecutado, stoppingToken)) return;

            var resultado = new List<string>();
            foreach (var (leadId, cliente, consultor) in Cards)
            {
                resultado.Add($"{cliente}: {await DevolverAsync(db, leadId, consultor, stoppingToken)}");
            }

            var resumo = $"{DateTimeOffset.UtcNow:O} | {string.Join("; ", resultado)}";
            await GravarAsync(db, ChaveStatus, resumo, stoppingToken);
            await GravarAsync(db, ChaveExecutado, resumo, stoppingToken);
            logger.LogInformation("Cards da MG134 devolvidos ao vendedor do Notion: {Resumo}", resumo);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao devolver cards da MG134 ao vendedor do Notion — o app segue; tenta de novo na próxima inicialização.");
        }
    }

    /// <returns>O que foi feito (ou por que nada foi feito).</returns>
    public static async Task<string> DevolverAsync(ApplicationDbContext db, Guid leadId, string nomeDoConsultor, CancellationToken ct)
    {
        var lead = await db.CrmLeads.AsNoTracking().Where(l => l.Id == leadId)
            .Select(l => new { l.Arquivado, l.ArquivadoPorId, ResponsavelNome = l.Responsavel != null ? l.Responsavel.NomeCompleto : null, l.EmailNormalizado, l.DocumentoNormalizado })
            .FirstOrDefaultAsync(ct);
        if (lead is null) return "lead não encontrado";
        if (!lead.Arquivado) return "já estava ativo";
        if (lead.ArquivadoPorId is not null) return "foi excluído por uma pessoa — mantido na lixeira";
        if (lead.ResponsavelNome is null || !lead.ResponsavelNome.StartsWith("Vendedor não identificado", StringComparison.Ordinal))
            return $"já tem vendedor ({lead.ResponsavelNome}) — mantido";

        var consultores = await db.Users.AsNoTracking().Where(u => u.NomeCompleto == nomeDoConsultor && u.Ativo).Select(u => u.Id).ToListAsync(ct);
        if (consultores.Count != 1) return $"consultor \"{nomeDoConsultor}\" não é único/ativo ({consultores.Count}) — mantido";

        var repetido = await db.CrmLeads.AnyAsync(o => o.Id != leadId && !o.Arquivado && o.VeiculoAdicionalDeLeadId == null
            && ((lead.EmailNormalizado != null && o.EmailNormalizado == lead.EmailNormalizado)
                || (lead.DocumentoNormalizado != null && o.DocumentoNormalizado == lead.DocumentoNormalizado)), ct);
        if (repetido) return "e-mail/CPF já usado por outro lead ativo — mantido arquivado";

        var consultorId = consultores[0];
        var atualizados = await db.CrmLeads.Where(l => l.Id == leadId && l.Arquivado && l.ArquivadoPorId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Arquivado, false)
                .SetProperty(l => l.ArquivadoEm, (DateTimeOffset?)null)
                .SetProperty(l => l.ResponsavelId, (Guid?)consultorId), ct);
        return atualizados > 0 ? $"desarquivado e entregue a {nomeDoConsultor}" : "nada alterado";
    }

    private static async Task GravarAsync(ApplicationDbContext db, string chave, string valor, CancellationToken ct)
    {
        var existente = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (existente is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else existente.Valor = valor;
        await db.SaveChangesAsync(ct);
    }
}
