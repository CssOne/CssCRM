using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Marketing;

/// <summary>
/// Orquestra a criação de um CrmLead a partir de um evento de leadgen do Meta: busca o lead
/// completo na Graph API, normaliza os campos e grava — porta de notion-lead-automation/src/
/// index.ts + notion.ts, adaptada ao domínio do CssCRM (Notion "Tags" multi-select vira
/// CrmTag/CrmLeadTag; "Campanha"/"O que" viram os campos já existentes em CrmLead).
/// </summary>
public sealed class MetaLeadIngestionService(
    ApplicationDbContext db,
    IMetaGraphClient graph,
    ILeadAssignmentService assignment,
    ILogger<MetaLeadIngestionService> logger)
{
    public async Task ProcessLeadEventAsync(string leadgenId, string? formId, CancellationToken ct)
    {
        if (await db.CrmLeads.AnyAsync(l => l.MetaLeadId == leadgenId, ct))
        {
            logger.LogInformation("Lead {LeadgenId} já processado anteriormente, ignorando (idempotência)", leadgenId);
            return;
        }

        var metaLead = await graph.FetchLeadAsync(leadgenId, ct);
        var fields = MetaFieldMapping.NormalizeFieldData(metaLead.FieldData);

        var email = MetaFieldMapping.GetMappedValue(fields, "email");
        var telefone = MetaFieldMapping.GetMappedValue(fields, "phone_number");
        var nome = MetaFieldMapping.ResolveName(fields);
        var clickId = fields.GetValueOrDefault("fbclid") ?? fields.GetValueOrDefault("click_id");

        var emailNormalizado = DocumentValidation.NormalizarEmail(email);
        var telefoneNormalizado = DocumentValidation.NormalizarTelefone(telefone);

        var overrideCfg = formId is not null && MetaFormConfig.Overrides.TryGetValue(formId, out var cfg) ? cfg : null;
        var tags = overrideCfg?.Tags ?? MetaFormConfig.DefaultTags;
        var campanha = overrideCfg?.Campanha ?? MetaFormConfig.DefaultCampanha;
        var produtoInteresse = overrideCfg?.ProdutoInteresse;

        // Contato já conhecido (mesmo e-mail/telefone de um lead não arquivado): não cria um
        // registro novo (o índice único de e-mail bloquearia mesmo) — só soma a campanha/tags
        // novas no lead existente, evitando fragmentar o histórico do mesmo cliente entre
        // campanhas diferentes.
        var existente = await EncontrarLeadExistenteAsync(emailNormalizado, telefoneNormalizado, ct);
        if (existente is not null)
        {
            await AplicarTagsAsync(existente, tags, ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Lead {LeadgenId} (form {FormId}, campanha {Campanha}) associado ao contato já existente {LeadId}",
                leadgenId, formId, campanha, existente.Id);
            return;
        }

        var responsavelId = await assignment.ProximoResponsavelAsync(ct);

        var lead = new CrmLead
        {
            // Sem etapa de propósito, igual à criação manual — vendedora vê "ninguém pegou ainda".
            NomeOuRazaoSocial = string.IsNullOrWhiteSpace(nome) ? "Lead Meta Ads (sem nome)" : nome,
            TipoPessoa = TipoPessoa.Fisica,
            Email = email,
            EmailNormalizado = emailNormalizado,
            Telefone = telefone,
            TelefoneNormalizado = telefoneNormalizado,
            WhatsApp = telefone,
            MetaLeadId = leadgenId,
            MetaFormId = formId,
            MetaClickId = clickId,
            Origem = "Meta ads",
            Campanha = campanha,
            ProdutoInteresse = produtoInteresse,
            ResponsavelId = responsavelId,
        };

        await AplicarTagsAsync(lead, tags, ct);

        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Lead {LeadgenId} criado a partir do webhook da Meta (form {FormId}, campanha {Campanha})", leadgenId, formId, campanha);
    }

    private async Task<CrmLead?> EncontrarLeadExistenteAsync(string? emailNormalizado, string? telefoneNormalizado, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(emailNormalizado))
        {
            var porEmail = await db.CrmLeads.Include(l => l.LeadTags)
                .FirstOrDefaultAsync(l => l.EmailNormalizado == emailNormalizado && !l.Arquivado, ct);
            if (porEmail is not null) return porEmail;
        }

        if (!string.IsNullOrEmpty(telefoneNormalizado))
        {
            return await db.CrmLeads.Include(l => l.LeadTags)
                .FirstOrDefaultAsync(l => l.TelefoneNormalizado == telefoneNormalizado && !l.Arquivado, ct);
        }

        return null;
    }

    private async Task AplicarTagsAsync(CrmLead lead, IReadOnlyList<string> nomes, CancellationToken ct)
    {
        if (nomes.Count == 0) return;

        var jaAplicadas = lead.LeadTags.Select(lt => lt.TagId).ToHashSet();
        var existentes = await db.CrmTags.Where(t => nomes.Contains(t.Nome)).ToListAsync(ct);
        var faltantes = nomes.Where(n => !existentes.Any(e => e.Nome.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
        var novas = faltantes.Select(n => new CrmTag { Nome = n }).ToList();
        if (novas.Count > 0)
        {
            db.CrmTags.AddRange(novas);
            existentes.AddRange(novas);
        }

        foreach (var tag in existentes)
        {
            if (jaAplicadas.Contains(tag.Id)) continue;
            lead.LeadTags.Add(new CrmLeadTag { LeadId = lead.Id, TagId = tag.Id });
        }
    }
}
