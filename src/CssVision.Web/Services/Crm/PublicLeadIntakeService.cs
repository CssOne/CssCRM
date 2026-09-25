using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Cria leads a partir de formulários públicos do site — sem usuário autenticado, então não
/// pode passar por LeadService.CriarAsync (o escopo por carteira do EquipeComercialService
/// rejeitaria o próprio vendedor sorteado pelo rodízio). Vai direto no banco, igual ao
/// MetaLeadIngestionService faz pro webhook de Lead Ads.
/// </summary>
public sealed class PublicLeadIntakeService(
    ApplicationDbContext db,
    ILeadAssignmentService assignment,
    ILogger<PublicLeadIntakeService> logger,
    ICrmEventHub? eventos = null) : IPublicLeadIntakeService
{
    /// <summary>form_ids do formulário de caminhão (AGV Truck) — não passam pelo rodízio, vão direto pra Samys.</summary>
    private static readonly HashSet<string> FormsCaminhaoSamys = ["1148948400894957", "2294287594679432", "1101990252362830"];
    private const string EmailConsultoraCaminhao = "samys.alexandre@gmail.com";

    public async Task<PublicLeadResultDto> CriarAsync(PublicLeadCreateRequest request, CancellationToken ct)
    {
        var emailNormalizado = DocumentValidation.NormalizarEmail(request.Email);
        var telefoneNormalizado = DocumentValidation.NormalizarTelefone(request.WhatsApp);

        var existente = await EncontrarLeadExistenteAsync(emailNormalizado, telefoneNormalizado, ct);
        if (existente is not null)
        {
            await GarantirClassificacaoTrafegoPagoAsync(existente, request.MetaLeadId, ct);
            logger.LogInformation(
                "Lead do site (projeto {Projeto}) associado ao contato já existente {LeadId}", request.Projeto, existente.Id);
            return await MontarResultadoAsync(existente.Id, existente.ResponsavelId, ct);
        }

        var observacoes = string.Join(" | ", new[]
            {
                string.IsNullOrWhiteSpace(request.Veiculo) ? null : $"Veículo: {request.Veiculo}",
            }.Where(s => s is not null));

        var responsavelId = await ResolverResponsavelAsync(request.Projeto, request.Oque, ct);

        var lead = new CrmLead
        {
            // Sem etapa de propósito, igual à criação manual e ao webhook do Meta — vendedora
            // vê "ninguém pegou ainda" até arrastar pra uma etapa ela mesma.
            NomeOuRazaoSocial = string.IsNullOrWhiteSpace(request.Nome) ? "Lead do site (sem nome)" : request.Nome.Trim(),
            TipoPessoa = TipoPessoa.Fisica,
            Telefone = request.WhatsApp,
            TelefoneNormalizado = telefoneNormalizado,
            Telefone2 = request.Telefone2,
            Telefone2Normalizado = DocumentValidation.NormalizarTelefone(request.Telefone2),
            WhatsApp = request.WhatsApp,
            Email = request.Email,
            EmailNormalizado = emailNormalizado,
            Estado = string.IsNullOrWhiteSpace(request.Estado) ? null : request.Estado.ToUpperInvariant(),
            Placa = request.Placa?.Trim().ToUpperInvariant() is { Length: > 0 and <= 10 } placaValida ? placaValida : null,
            TemSeguro = request.TemSeguro,
            UtilidadeVeiculo = request.UtilidadeVeiculo,
            Origem = request.Fonte ?? "Site",
            Campanha = request.Campanha,
            ProdutoInteresse = request.Oque,
            Gclid = request.Gclid,
            UtmSource = request.UtmSource,
            UtmMedium = request.UtmMedium,
            UtmTerm = request.UtmTerm,
            MetaClickId = request.ClickId,
            MetaFormId = request.Projeto,
            MetaLeadId = request.MetaLeadId,
            Observacoes = observacoes.Length == 0 ? null : observacoes,
            ConsentimentoContato = true,
            ConsentimentoDataEm = DateTimeOffset.UtcNow,
            ConsentimentoOrigem = OrigemLead.MarcadorFormularioSite,
            ResponsavelId = responsavelId,
            CriadoManualmente = false,
        };

        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync(ct);
        eventos?.PublicarQuadroAtualizado("site");

        logger.LogInformation("Lead {LeadId} criado via formulário do site (projeto {Projeto})", lead.Id, request.Projeto);

        return await MontarResultadoAsync(lead.Id, responsavelId, ct);
    }

    public async Task<PublicLeadResultDto?> ObterPorMetaLeadIdAsync(string metaLeadId, CancellationToken ct)
    {
        var lead = await db.CrmLeads.AsNoTracking()
            .Where(l => l.MetaLeadId == metaLeadId)
            .Select(l => new { l.Id, l.ResponsavelId })
            .FirstOrDefaultAsync(ct);

        return lead is null ? null : await MontarResultadoAsync(lead.Id, lead.ResponsavelId, ct);
    }

    /// <summary>
    /// Um contato antigo (ex: migrado do Notion) pode mandar um lead novo de verdade pelo tráfego
    /// pago hoje — sem isso, o card ficava com a classificação antiga e sumia do filtro "Tráfego
    /// pago" do quadro, escondendo do time que chegou uma oportunidade nova pra esse contato.
    /// </summary>
    private async Task GarantirClassificacaoTrafegoPagoAsync(CrmLead existente, string? metaLeadId, CancellationToken ct)
    {
        if (OrigemLead.VeioDoTrafegoPago.Compile()(existente)) return;

        existente.ConsentimentoOrigem = OrigemLead.MarcadorFormularioSite;
        existente.MetaLeadId ??= metaLeadId;
        await db.SaveChangesAsync(ct);
        eventos?.PublicarQuadroAtualizado("site");
        logger.LogInformation("Lead {LeadId} reclassificado como tráfego pago (contato antigo recebeu lead novo)", existente.Id);
    }

    /// <summary>
    /// Leads dos formulários de caminhão vão direto pra Samys, sem passar pelo rodízio normal —
    /// só cai no rodízio se a conta dela não existir ou estiver inativa (não deixa o lead sem
    /// responsável só porque a exceção não pôde ser aplicada).
    /// </summary>
    private async Task<Guid?> ResolverResponsavelAsync(string? projeto, string? oQue, CancellationToken ct)
    {
        var formId = projeto?.StartsWith("meta-instant-") == true ? projeto["meta-instant-".Length..] : null;
        if (formId is not null && FormsCaminhaoSamys.Contains(formId))
        {
            var emailNormalizado = EmailConsultoraCaminhao.ToUpperInvariant();
            var consultoraId = await db.Users.AsNoTracking()
                .Where(u => u.NormalizedEmail == emailNormalizado && u.Ativo)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(ct);

            if (consultoraId is not null) return consultoraId;
            logger.LogWarning("Lead de caminhão (form {FormId}) não pôde ser atribuído à consultora fixa — caindo no rodízio normal", formId);
        }

        return await assignment.ProximoResponsavelAsync(oQue, ct);
    }

    private async Task<CrmLead?> EncontrarLeadExistenteAsync(string? emailNormalizado, string? telefoneNormalizado, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(emailNormalizado))
        {
            var porEmail = await db.CrmLeads.FirstOrDefaultAsync(l => l.EmailNormalizado == emailNormalizado && !l.Arquivado, ct);
            if (porEmail is not null) return porEmail;
        }

        if (!string.IsNullOrEmpty(telefoneNormalizado))
        {
            return await db.CrmLeads.FirstOrDefaultAsync(l => l.TelefoneNormalizado == telefoneNormalizado && !l.Arquivado, ct);
        }

        return null;
    }

    private async Task<PublicLeadResultDto> MontarResultadoAsync(Guid leadId, Guid? responsavelId, CancellationToken ct)
    {
        if (responsavelId is null) return new PublicLeadResultDto(leadId, null, null, null);

        var consultor = await db.Users.AsNoTracking()
            .Where(u => u.Id == responsavelId)
            .Select(u => new { u.NomeCompleto, u.FotoUrl, u.PhoneNumber })
            .FirstOrDefaultAsync(ct);

        return new PublicLeadResultDto(leadId, consultor?.NomeCompleto, consultor?.FotoUrl, consultor?.PhoneNumber);
    }
}
