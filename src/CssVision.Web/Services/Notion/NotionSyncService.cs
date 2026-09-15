using System.Text.Json;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Notion;

/// <summary>
/// Sincronização incremental com as 4 bases do Notion: a cada execução, busca só as páginas
/// editadas depois do último checkpoint e cria/atualiza o lead (e a oportunidade, se for uma
/// venda concluída) correspondente. Substitui a migração manual pontual por um processo contínuo.
/// </summary>
public sealed class NotionSyncService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILogger<NotionSyncService> logger)
{
    private static readonly (string DataSourceId, string RegionalName)[] DataSources =
    [
        ("0f91c248-497e-4369-aad9-1d4266a49db4", "MG132"),
        ("1a163799-99a8-81e2-82be-000bb2817da0", "MG134"),
        ("31763799-99a8-8118-b573-000b24781bfe", "MG134 Consultores Externos"),
        ("31763799-99a8-81b2-b83b-000bfca82506", "CSS Growth Sales"),
    ];

    private static readonly Dictionary<string, string> StatusParaEtapaLead = new()
    {
        ["EM ATENDIMENTO"] = "Em atendimento",
        ["COTAÇÃO"] = "Cotação",
        ["PERDIDO"] = "Perdido",
        ["NÃO FAZEMOS"] = "Não fazemos",
        ["RECUSA/INATIVA"] = "Recusa/Inativa",
    };

    public async Task<string> SincronizarTudoAsync(string token, CancellationToken ct = default)
    {
        var notion = new NotionClient(token);
        var relatorios = new List<string>();
        foreach (var spec in DataSources)
        {
            try
            {
                relatorios.Add(await SincronizarDataSourceAsync(notion, spec.DataSourceId, spec.RegionalName, ct));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha sincronizando data source {DataSourceId} ({Regional})", spec.DataSourceId, spec.RegionalName);
                relatorios.Add($"{spec.RegionalName}: FALHOU ({ex.Message})");
            }
        }
        return string.Join(" | ", relatorios);
    }

    private async Task<string> SincronizarDataSourceAsync(NotionClient notion, string dataSourceId, string regionalNome, CancellationToken ct)
    {
        var inicioDaExecucao = DateTimeOffset.UtcNow;
        var checkpoint = await db.CrmNotionSyncCheckpoints.FindAsync([dataSourceId], ct);
        // Sem checkpoint (primeira ativação): só olha pra frente — o histórico já foi trazido pela
        // migração manual em lote, não faz sentido reprocessar tudo de novo aqui.
        var desde = checkpoint?.UltimaSincronizacaoEm ?? inicioDaExecucao;

        var regional = await db.CrmRegionais.FirstOrDefaultAsync(r => r.Nome == regionalNome, ct);
        if (regional is null)
        {
            regional = new CrmRegional { Nome = regionalNome };
            db.CrmRegionais.Add(regional);
            await db.SaveChangesAsync(ct);
        }

        var etapasPorNome = await db.CrmLeadStages.ToDictionaryAsync(s => s.Nome.Trim(), s => s.Id, ct);
        var vendaConcluidaStageId = etapasPorNome["Venda concluída"];
        var ganhoStageId = (await db.CrmPipelineStages.FirstAsync(s => s.Tipo == TipoEtapaPipeline.Ganho, ct)).Id;
        var placeholderVendedorId = await ObterOuCriarVendedorPlaceholderAsync(regional.Id, regionalNome, ct);

        int processados = 0, criados = 0, atualizados = 0, erros = 0;

        await foreach (var page in notion.QueryEditadasDesdeAsync(dataSourceId, desde, ct))
        {
            processados++;
            try
            {
                var resultado = await ProcessarPaginaAsync(page, regional.Id, regionalNome, etapasPorNome, vendaConcluidaStageId, ganhoStageId, placeholderVendedorId, ct);
                if (resultado) criados++; else atualizados++;
            }
            catch (Exception ex)
            {
                erros++;
                logger.LogWarning(ex, "Erro sincronizando pagina {PageId} ({Regional})", page.PageId(), regionalNome);
            }
        }

        if (checkpoint is null)
        {
            db.CrmNotionSyncCheckpoints.Add(new CrmNotionSyncCheckpoint { DataSourceId = dataSourceId, RegionalNome = regionalNome, UltimaSincronizacaoEm = inicioDaExecucao });
        }
        else
        {
            checkpoint.UltimaSincronizacaoEm = inicioDaExecucao;
        }
        await db.SaveChangesAsync(ct);

        return $"{regionalNome}: {processados} processados, {criados} criados, {atualizados} atualizados, {erros} erros";
    }

    /// <returns>true se criou um lead novo, false se atualizou um existente.</returns>
    private async Task<bool> ProcessarPaginaAsync(
        JsonElement page, Guid regionalId, string regionalNome, Dictionary<string, Guid> etapasPorNome,
        Guid vendaConcluidaStageId, Guid ganhoStageId, Guid placeholderVendedorId, CancellationToken ct)
    {
        var nome = page.Text("Name");
        if (string.IsNullOrWhiteSpace(nome)) throw new InvalidOperationException("Página sem nome (title vazio).");
        if (nome.Length > 200) nome = nome[..200];

        var cpfBruto = page.Text("CPF") ?? page.Number("CPF")?.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        var documentoNormalizado = DocumentValidation.NormalizarDocumento(cpfBruto, out _);
        if (documentoNormalizado is { Length: > 14 }) documentoNormalizado = null;

        var emailBruto = page.Text("E-mail", "[META] Email");
        var emailNormalizado = DocumentValidation.NormalizarEmail(emailBruto);

        var whatsapp = page.Text("WhatsApp");
        var telefoneMeta = page.Text("[META] Phone Number");
        var telefone = whatsapp ?? telefoneMeta;
        var telefoneNormalizado = DocumentValidation.NormalizarTelefone(telefone);
        if (telefoneNormalizado is { Length: > 20 }) telefoneNormalizado = null;

        var estadoTexto = page.Text("ESTADO");
        var estado = page.Select("Estado") ?? (estadoTexto is { Length: 2 } ? estadoTexto : null);
        var cidade = page.Text("Cidade");
        var oQue = page.Select("O que");
        var tipoIndicacao = NotionLeadClassifier.Classificar(oQue);

        var vendedorInfo = page.PrimeiroVendedor("Vendedor");
        var vendedorId = await ResolverVendedorAsync(vendedorInfo, regionalId, regionalNome, placeholderVendedorId, ct);

        var status = page.Select("Status");
        var isVendaConcluida = string.Equals(status, "VENDA CONCLUIDA", StringComparison.OrdinalIgnoreCase);

        var lead = documentoNormalizado is not null
            ? await db.CrmLeads.FirstOrDefaultAsync(l => l.DocumentoNormalizado == documentoNormalizado && !l.Arquivado, ct)
            : null;
        lead ??= emailNormalizado is not null
            ? await db.CrmLeads.FirstOrDefaultAsync(l => l.EmailNormalizado == emailNormalizado && !l.Arquivado, ct)
            : null;
        lead ??= telefoneNormalizado is not null
            ? await db.CrmLeads.FirstOrDefaultAsync(l => l.TelefoneNormalizado == telefoneNormalizado && !l.Arquivado, ct)
            : null;

        var criadoAgora = lead is null;
        if (lead is null)
        {
            lead = new CrmLead
            {
                TipoPessoa = documentoNormalizado?.Length == 14 ? TipoPessoa.Juridica : TipoPessoa.Fisica,
                Regional = regionalNome,
                Origem = "Sincronização Notion",
                ResponsavelId = vendedorId,
                ConsentimentoContato = true,
                ConsentimentoOrigem = "Sincronização automática (Notion)",
                Arquivado = false,
            };
            if (ParseUtc(page.CreatedTime("Data de chegada")) is { } criadoEm) lead.CriadoEm = criadoEm;
            db.CrmLeads.Add(lead);
        }

        lead.NomeOuRazaoSocial = nome.Trim();
        lead.DocumentoNormalizado = documentoNormalizado ?? lead.DocumentoNormalizado;
        lead.Telefone = telefone ?? lead.Telefone;
        lead.TelefoneNormalizado = telefoneNormalizado ?? lead.TelefoneNormalizado;
        lead.WhatsApp = whatsapp ?? lead.WhatsApp;
        lead.Email = emailBruto ?? lead.Email;
        lead.EmailNormalizado = emailNormalizado ?? lead.EmailNormalizado;
        lead.Cidade = cidade ?? lead.Cidade;
        lead.Estado = estado ?? lead.Estado;
        lead.Campanha = page.Select("Campanha", "CAMPANHA") ?? lead.Campanha;
        lead.ProdutoInteresse = oQue ?? lead.ProdutoInteresse;
        lead.TipoIndicacao = tipoIndicacao;
        if (vendedorId != placeholderVendedorId) lead.ResponsavelId = vendedorId;

        if (isVendaConcluida)
        {
            lead.EtapaId = vendaConcluidaStageId;
            await CriarOuAtualizarOportunidadeAsync(page, lead, vendedorId, ganhoStageId, ct);
        }
        else if (criadoAgora && status is not null && StatusParaEtapaLead.TryGetValue(status, out var etapaNome) && etapasPorNome.TryGetValue(etapaNome, out var etapaId))
        {
            lead.EtapaId = etapaId;
        }

        await db.SaveChangesAsync(ct);
        return criadoAgora;
    }

    private async Task CriarOuAtualizarOportunidadeAsync(JsonElement page, CrmLead lead, Guid vendedorId, Guid ganhoStageId, CancellationToken ct)
    {
        var oportunidade = await db.CrmOpportunities.Include(o => o.Veiculo)
            .FirstOrDefaultAsync(o => o.LeadId == lead.Id && !o.Arquivado, ct);

        var dataVenda = ParseUtc(page.DateStart("Data da venda"));
        var mensalidade = page.Number("Mensalidade") is { } m ? (decimal)m : (decimal?)null;
        var mensalidadeComDesconto = page.FormulaDecimal("Mensalidade com desconto");
        var adesao = page.Number("Adesão") is { } a ? (decimal)a : (decimal?)null;
        var porcentagem = page.Number("Porcentagem") is { } pc ? (decimal)pc : (decimal?)null;
        var total = page.FormulaDecimal("Total");
        var ativoEm = ParseUtc(page.DateStart("Ativo em"));
        var oQue = page.Select("O que");

        if (oportunidade is null)
        {
            oportunidade = new CrmOpportunity { LeadId = lead.Id, Veiculo = new CrmVeiculo() };
            db.CrmOpportunities.Add(oportunidade);
        }

        oportunidade.Titulo = oQue ?? "Proteção veicular";
        oportunidade.ResponsavelId = vendedorId;
        oportunidade.EtapaId = ganhoStageId;
        oportunidade.EtapaDesde = dataVenda ?? oportunidade.EtapaDesde;
        oportunidade.ProdutoOuServico = oQue;
        oportunidade.ValorEstimado = mensalidade ?? total ?? oportunidade.ValorEstimado;
        oportunidade.ValorFinal = total ?? mensalidade ?? oportunidade.ValorFinal;
        oportunidade.DataEfetivaFechamento = dataVenda ?? oportunidade.DataEfetivaFechamento;
        oportunidade.DataAdesao = dataVenda.HasValue ? DateOnly.FromDateTime(dataVenda.Value.UtcDateTime) : oportunidade.DataAdesao;
        oportunidade.AtivoEm = ativoEm ?? oportunidade.AtivoEm;
        oportunidade.Mensalidade = mensalidade ?? oportunidade.Mensalidade;
        oportunidade.MensalidadeComDesconto = mensalidadeComDesconto ?? oportunidade.MensalidadeComDesconto;
        oportunidade.PagamentoAdesao = adesao ?? oportunidade.PagamentoAdesao;
        oportunidade.Porcentagem = porcentagem ?? oportunidade.Porcentagem;
        oportunidade.TermoAdesaoAceito = page.HasFiles("Termo Adesão") || oportunidade.TermoAdesaoAceito;
        oportunidade.Migracao = page.Select("Migração", "Migração?") is not null || oportunidade.Migracao;

        var veiculo = oportunidade.Veiculo ??= new CrmVeiculo { OpportunityId = oportunidade.Id };
        veiculo.Descricao = page.Text("Veiculo") ?? veiculo.Descricao;
        veiculo.Placa = page.Text("Placa") is { Length: <= 10 } placaValida ? placaValida : veiculo.Placa;
        veiculo.Fipe = page.Number("FIPE") is { } fipe ? (decimal)fipe : veiculo.Fipe;
        veiculo.Rastreador = page.Number("Rastreador")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? veiculo.Rastreador;
    }

    private readonly Dictionary<string, Guid> _vendedorPorEmailCache = new();
    private readonly Dictionary<Guid, Guid> _placeholderPorRegionalCache = new();

    private async Task<Guid> ResolverVendedorAsync(NotionPageExtensions.VendedorInfo? vendedor, Guid regionalId, string regionalNome, Guid placeholderVendedorId, CancellationToken ct)
    {
        if (vendedor is null || string.IsNullOrWhiteSpace(vendedor.Email)) return placeholderVendedorId;

        var email = vendedor.Email.Trim().ToLowerInvariant();
        if (_vendedorPorEmailCache.TryGetValue(email, out var idCache)) return idCache;

        var existente = await userManager.FindByEmailAsync(email);
        if (existente is not null)
        {
            _vendedorPorEmailCache[email] = existente.Id;
            return existente.Id;
        }

        var usuario = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            NomeCompleto = vendedor.Nome,
            RegionalId = regionalId,
            Ativo = true,
        };
        var resultado = await userManager.CreateAsync(usuario, "Senha@123");
        if (!resultado.Succeeded) return placeholderVendedorId;

        await userManager.AddToRoleAsync(usuario, Roles.Comercial);
        _vendedorPorEmailCache[email] = usuario.Id;
        return usuario.Id;
    }

    private async Task<Guid> ObterOuCriarVendedorPlaceholderAsync(Guid regionalId, string regionalNome, CancellationToken ct)
    {
        if (_placeholderPorRegionalCache.TryGetValue(regionalId, out var idCache)) return idCache;

        var email = $"vendedor.nao.identificado.{regionalNome.ToLowerInvariant().Replace(" ", "-")}@cssvision.local";
        var existente = await userManager.FindByEmailAsync(email);
        if (existente is not null)
        {
            _placeholderPorRegionalCache[regionalId] = existente.Id;
            return existente.Id;
        }

        var usuario = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            NomeCompleto = $"Vendedor não identificado ({regionalNome})",
            RegionalId = regionalId,
            Ativo = false,
        };
        await userManager.CreateAsync(usuario, "Senha@123");
        await userManager.AddToRoleAsync(usuario, Roles.Comercial);
        _placeholderPorRegionalCache[regionalId] = usuario.Id;
        return usuario.Id;
    }

    private static DateTimeOffset? ParseUtc(string? texto) =>
        DateTimeOffset.TryParse(texto, out var valor) ? valor.ToUniversalTime() : null;
}
