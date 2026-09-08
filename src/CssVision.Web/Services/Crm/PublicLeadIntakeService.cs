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
    ILogger<PublicLeadIntakeService> logger) : IPublicLeadIntakeService
{
    public async Task<PublicLeadResultDto> CriarAsync(PublicLeadCreateRequest request, CancellationToken ct)
    {
        var emailNormalizado = DocumentValidation.NormalizarEmail(request.Email);
        var telefoneNormalizado = DocumentValidation.NormalizarTelefone(request.WhatsApp);

        var existente = await EncontrarLeadExistenteAsync(emailNormalizado, telefoneNormalizado, ct);
        if (existente is not null)
        {
            logger.LogInformation(
                "Lead do site (projeto {Projeto}) associado ao contato já existente {LeadId}", request.Projeto, existente.Id);
            return await MontarResultadoAsync(existente.Id, existente.ResponsavelId, ct);
        }

        var observacoes = string.Join(" | ", new[]
            {
                string.IsNullOrWhiteSpace(request.Placa) ? null : $"Placa: {request.Placa}",
                string.IsNullOrWhiteSpace(request.Veiculo) ? null : $"Veículo: {request.Veiculo}",
            }.Where(s => s is not null));

        var responsavelId = await assignment.ProximoResponsavelAsync(ct);

        var lead = new CrmLead
        {
            // Sem etapa de propósito, igual à criação manual e ao webhook do Meta — vendedora
            // vê "ninguém pegou ainda" até arrastar pra uma etapa ela mesma.
            NomeOuRazaoSocial = string.IsNullOrWhiteSpace(request.Nome) ? "Lead do site (sem nome)" : request.Nome.Trim(),
            TipoPessoa = TipoPessoa.Fisica,
            Telefone = request.WhatsApp,
            TelefoneNormalizado = telefoneNormalizado,
            WhatsApp = request.WhatsApp,
            Email = request.Email,
            EmailNormalizado = emailNormalizado,
            Estado = string.IsNullOrWhiteSpace(request.Estado) ? null : request.Estado.ToUpperInvariant(),
            Origem = request.Fonte ?? "Site",
            Campanha = request.Campanha,
            ProdutoInteresse = request.Oque,
            Gclid = request.Gclid,
            MetaClickId = request.ClickId,
            MetaFormId = request.Projeto,
            MetaLeadId = request.MetaLeadId,
            Observacoes = observacoes.Length == 0 ? null : observacoes,
            ConsentimentoContato = true,
            ConsentimentoDataEm = DateTimeOffset.UtcNow,
            ConsentimentoOrigem = "Formulário do site",
            ResponsavelId = responsavelId,
        };

        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Lead {LeadId} criado via formulário do site (projeto {Projeto})", lead.Id, request.Projeto);

        return await MontarResultadoAsync(lead.Id, responsavelId, ct);
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
