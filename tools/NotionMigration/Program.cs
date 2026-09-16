using System.Text.Json;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotionMigration;

var token = Environment.GetEnvironmentVariable("NOTION_TOKEN")
    ?? throw new InvalidOperationException("Defina a variável de ambiente NOTION_TOKEN antes de rodar.");
var connectionString = Environment.GetEnvironmentVariable("MIGRATION_CONNECTION_STRING")
    ?? "Host=localhost;Port=5434;Database=cssvision_crm;Username=postgres;Password=postgres";
var limitePorBase = int.TryParse(Environment.GetEnvironmentVariable("MIGRATION_LIMIT_PER_DB"), out var limite) ? limite : (int?)null;

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddSingleton<ICurrentUserService, MigrationCurrentUser>();
services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure(5)));
services.AddIdentity<ApplicationUser, ApplicationRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

var notion = new NotionClient(token);

DatabaseSpec[] specs =
[
    new("0f91c248-497e-4369-aad9-1d4266a49db4", "MG132"),
    new("1a163799-99a8-81e2-82be-000bb2817da0", "MG134"),
    new("31763799-99a8-8118-b573-000b24781bfe", "MG134 Consultores Externos"),
    new("31763799-99a8-81b2-b83b-000bfca82506", "CSS Growth Sales"),
];

if (Environment.GetEnvironmentVariable("MIGRATION_MODE") == "fixdates")
{
    await CorrigirDatasCriacaoAsync();
    return;
}

if (Environment.GetEnvironmentVariable("MIGRATION_MODE") == "fixorigin")
{
    await CorrigirOrigemEIndicacaoAsync();
    return;
}

var ganhoStage = await db.CrmPipelineStages.FirstAsync(s => s.Tipo == TipoEtapaPipeline.Ganho);
var vendaConcluidaLeadStageId = (await db.CrmLeadStages.FirstAsync(s => s.Nome == "Venda concluída")).Id;

var documentosExistentes = (await db.CrmLeads.AsNoTracking().Where(l => l.DocumentoNormalizado != null && !l.Arquivado)
    .Select(l => l.DocumentoNormalizado!).ToListAsync()).ToHashSet();
var emailsExistentes = (await db.CrmLeads.AsNoTracking().Where(l => l.EmailNormalizado != null && !l.Arquivado)
    .Select(l => l.EmailNormalizado!).ToListAsync()).ToHashSet();

var vendedorPorEmail = await db.Users.AsNoTracking().Where(u => u.Email != null)
    .ToDictionaryAsync(u => u.Email!.ToLowerInvariant(), u => u.Id);
var vendedoresCriados = new List<(string Nome, string Email, string Regional)>();
var placeholdersPorRegional = new Dictionary<Guid, Guid>();

var relatorio = new List<string>();

if (Environment.GetEnvironmentVariable("MIGRATION_MODE") == "enrich")
{
    await EnriquecerVendaConcluidaAsync();
    return;
}

if (Environment.GetEnvironmentVariable("MIGRATION_MODE") == "importnovos")
{
    await ImportarNovosLeadsAsync();
    return;
}

if (Environment.GetEnvironmentVariable("MIGRATION_MODE") == "fixvendors")
{
    await CorrigirVendedoresAsync();
    return;
}

foreach (var spec in specs)
{
    Console.WriteLine($"\n=== {spec.RegionalName} ===");

    var regional = await db.CrmRegionais.FirstOrDefaultAsync(r => r.Nome == spec.RegionalName);
    if (regional is null)
    {
        regional = new CrmRegional { Nome = spec.RegionalName };
        db.CrmRegionais.Add(regional);
        await db.SaveChangesAsync();
        Console.WriteLine($"  Regional '{spec.RegionalName}' criada.");
    }

    var totalLidos = 0;
    var importados = 0;
    var duplicados = 0;
    var comErro = 0;
    var lote = new List<(CrmLead Lead, CrmOpportunity Oportunidade, CrmVeiculo Veiculo)>();

    await foreach (var page in notion.QueryVendaConcluidaAsync(spec.DataSourceId))
    {
        if (limitePorBase.HasValue && totalLidos >= limitePorBase.Value) break;
        totalLidos++;
        try
        {
            var nome = page.Text("Name");
            if (string.IsNullOrWhiteSpace(nome))
            {
                comErro++;
                continue;
            }
            if (nome.Length > 200) nome = nome[..200];

            // CPF é texto no MG.132 mas número nas outras 3 bases — tenta os dois.
            var cpfBruto = page.Text("CPF") ?? page.Number("CPF")?.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            var documentoNormalizado = DocumentValidation.NormalizarDocumento(cpfBruto, out _);
            if (documentoNormalizado is { Length: > 14 }) documentoNormalizado = null;

            var emailBruto = page.Text("E-mail", "[META] Email");
            var emailNormalizado = DocumentValidation.NormalizarEmail(emailBruto);

            if ((documentoNormalizado is not null && documentosExistentes.Contains(documentoNormalizado)) ||
                (emailNormalizado is not null && emailsExistentes.Contains(emailNormalizado)))
            {
                duplicados++;
                continue;
            }

            var vendedorInfo = page.PrimeiroVendedor("Vendedor");
            var vendedorId = await ResolverVendedorAsync(vendedorInfo, regional.Id);

            var whatsapp = page.Text("WhatsApp");
            var telefoneMeta = page.Text("[META] Phone Number");
            var telefone = whatsapp ?? telefoneMeta;
            var telefoneNormalizado = DocumentValidation.NormalizarTelefone(telefone);
            if (telefoneNormalizado is { Length: > 20 }) telefoneNormalizado = null;

            // "ESTADO" é texto livre (às vezes lixo); só aceita se já vier como sigla de 2 letras.
            var estadoTexto = page.Text("ESTADO");
            var estado = page.Select("Estado") ?? (estadoTexto is { Length: 2 } ? estadoTexto : null);

            var lead = new CrmLead
            {
                NomeOuRazaoSocial = nome.Trim(),
                TipoPessoa = documentoNormalizado?.Length == 14 ? TipoPessoa.Juridica : TipoPessoa.Fisica,
                DocumentoNormalizado = documentoNormalizado,
                Telefone = telefone,
                TelefoneNormalizado = telefoneNormalizado,
                WhatsApp = whatsapp,
                Email = emailBruto,
                EmailNormalizado = emailNormalizado,
                Cidade = page.Text("Cidade"),
                Estado = estado,
                Regional = spec.RegionalName,
                Origem = "Migração Notion",
                Campanha = page.Select("Campanha", "CAMPANHA"),
                ProdutoInteresse = page.Select("O que"),
                ResponsavelId = vendedorId,
                EtapaId = vendaConcluidaLeadStageId,
                TipoIndicacao = NotionLeadClassifier.Classificar(page.Select("O que")),
                ConsentimentoContato = true,
                ConsentimentoOrigem = "Migração da base histórica (Notion)",
                Arquivado = false,
            };

            var dataVendaTexto = page.DateStart("Data da venda");
            var dataVenda = ParseUtc(dataVendaTexto);
            var criadoEmTexto = page.CreatedTime("Data de chegada");
            if (ParseUtc(criadoEmTexto) is { } criadoEm) lead.CriadoEm = criadoEm;

            var mensalidade = page.Number("Mensalidade") is { } m ? (decimal)m : (decimal?)null;
            var mensalidadeComDesconto = page.FormulaDecimal("Mensalidade com desconto");
            var adesao = page.Number("Adesão") is { } a ? (decimal)a : (decimal?)null;
            var porcentagem = page.Number("Porcentagem") is { } pc ? (decimal)pc : (decimal?)null;
            var total = page.FormulaDecimal("Total");
            var ativoEmTexto = page.DateStart("Ativo em");

            var oportunidade = new CrmOpportunity
            {
                Lead = lead,
                Titulo = page.Select("O que") ?? "Proteção veicular",
                ResponsavelId = vendedorId,
                EtapaId = ganhoStage.Id,
                EtapaDesde = dataVenda ?? lead.CriadoEm,
                ProdutoOuServico = page.Select("O que"),
                ValorEstimado = mensalidade ?? total ?? 0m,
                ValorFinal = total ?? mensalidade,
                DataEfetivaFechamento = dataVenda,
                DataAdesao = dataVenda.HasValue ? DateOnly.FromDateTime(dataVenda.Value.UtcDateTime) : null,
                AtivoEm = ParseUtc(ativoEmTexto),
                Mensalidade = mensalidade,
                MensalidadeComDesconto = mensalidadeComDesconto,
                PagamentoAdesao = adesao,
                Porcentagem = porcentagem,
                TermoAdesaoAceito = page.HasFiles("Termo Adesão"),
                Migracao = page.Select("Migração", "Migração?") is not null,
            };

            var veiculo = new CrmVeiculo
            {
                Opportunity = oportunidade,
                Descricao = page.Text("Veiculo"),
                Placa = page.Text("Placa") is { Length: <= 10 } placaValida ? placaValida : null,
                Fipe = page.Number("FIPE") is { } fipe ? (decimal)fipe : (decimal?)null,
                Rastreador = page.Number("Rastreador")?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DataChegada = ParseUtc(criadoEmTexto),
            };
            oportunidade.Veiculo = veiculo;

            if (documentoNormalizado is not null) documentosExistentes.Add(documentoNormalizado);
            if (emailNormalizado is not null) emailsExistentes.Add(emailNormalizado);

            lote.Add((lead, oportunidade, veiculo));
            importados++;
        }
        catch (Exception ex)
        {
            comErro++;
            relatorio.Add($"  [{spec.RegionalName}] Erro na linha {totalLidos}: {ex.Message}");
        }

        if (lote.Count >= 200)
        {
            await SalvarLoteAsync(lote);
            lote.Clear();
            Console.WriteLine($"  ... {totalLidos} lidos, {importados} importados, {duplicados} duplicados, {comErro} com erro");
        }
    }

    if (lote.Count > 0) await SalvarLoteAsync(lote);

    Console.WriteLine($"  Total: {totalLidos} lidos | {importados} importados | {duplicados} duplicados | {comErro} com erro");
    relatorio.Add($"{spec.RegionalName}: {totalLidos} lidos, {importados} importados, {duplicados} duplicados, {comErro} com erro");
}

Console.WriteLine("\n=== Resumo ===");
foreach (var linha in relatorio) Console.WriteLine(linha);

Console.WriteLine($"\n=== {vendedoresCriados.Count} vendedores criados (senha temporária: Senha@123) ===");
foreach (var (nome, email, regionalNome) in vendedoresCriados)
{
    Console.WriteLine($"  {nome} <{email}> — {regionalNome}");
}

var caminhoRelatorio = Path.Combine(AppContext.BaseDirectory, $"migration-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
await File.WriteAllLinesAsync(caminhoRelatorio, relatorio.Concat(
    vendedoresCriados.Select(v => $"Vendedor criado: {v.Nome} <{v.Email}> — {v.Regional} — senha temporária Senha@123")));
Console.WriteLine($"\nRelatório salvo em: {caminhoRelatorio}");

async Task<Guid> ResolverVendedorAsync(NotionPageExtensions.VendedorInfo? vendedor, Guid regionalId)
{
    if (vendedor is null || string.IsNullOrWhiteSpace(vendedor.Email))
    {
        return await ObterOuCriarVendedorPlaceholderAsync(regionalId);
    }

    var email = vendedor.Email.Trim().ToLowerInvariant();
    if (vendedorPorEmail.TryGetValue(email, out var idExistente)) return idExistente;

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
    if (!resultado.Succeeded)
    {
        // E-mail pode já existir com capitalização diferente ou ter colidido — tenta reaproveitar.
        var existente = await userManager.FindByEmailAsync(email);
        if (existente is not null)
        {
            vendedorPorEmail[email] = existente.Id;
            return existente.Id;
        }
        return await ObterOuCriarVendedorPlaceholderAsync(regionalId);
    }

    await userManager.AddToRoleAsync(usuario, Roles.Comercial);
    vendedorPorEmail[email] = usuario.Id;
    var regional = await db.CrmRegionais.FindAsync(regionalId);
    vendedoresCriados.Add((vendedor.Nome, email, regional!.Nome));
    return usuario.Id;
}

async Task<Guid> ObterOuCriarVendedorPlaceholderAsync(Guid regionalId)
{
    if (placeholdersPorRegional.TryGetValue(regionalId, out var idCache)) return idCache;

    var regionalNome = (await db.CrmRegionais.FindAsync(regionalId))!.Nome;
    var email = $"vendedor.nao.identificado.{regionalNome.ToLowerInvariant().Replace(" ", "-")}@cssvision.local";
    var existente = await userManager.FindByEmailAsync(email);
    if (existente is not null)
    {
        placeholdersPorRegional[regionalId] = existente.Id;
        return existente.Id;
    }

    var usuario = new ApplicationUser
    {
        UserName = email,
        Email = email,
        EmailConfirmed = true,
        NomeCompleto = $"Vendedor não identificado ({regionalNome}) — migração",
        RegionalId = regionalId,
        Ativo = false,
    };
    await userManager.CreateAsync(usuario, "Senha@123");
    await userManager.AddToRoleAsync(usuario, Roles.Comercial);
    placeholdersPorRegional[regionalId] = usuario.Id;
    return usuario.Id;
}

async Task SalvarLoteAsync(List<(CrmLead Lead, CrmOpportunity Oportunidade, CrmVeiculo Veiculo)> lote)
{
    // Adiciona pela Oportunidade: ela referencia Lead (.Lead) e Veiculo (.Veiculo), então o Add()
    // descobre e rastreia os três via essas navegações populadas — só adicionar o Lead não pegaria
    // a Oportunidade, já que não há navegação Lead -> Oportunidades preenchida aqui.
    foreach (var (_, oportunidade, _) in lote) db.CrmOpportunities.Add(oportunidade);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        // Algo no lote violou uma constraint (ex: duplicidade que escapou da checagem em memória, ou
        // um valor grande demais pra coluna). Descarta o contexto principal (evita mexer manualmente
        // no change tracker, que gera erros de FK severed) e refaz linha por linha num DbContext novo
        // e isolado, pra achar só a(s) linha(s) problemática(s) sem perder o resto do lote.
        db.ChangeTracker.Clear();

        foreach (var (lead, oportunidade, _) in lote)
        {
            await using var retryScope = provider.CreateAsyncScope();
            var retryDb = retryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            retryDb.CrmOpportunities.Add(oportunidade);
            try
            {
                await retryDb.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                relatorio.Add($"  Lead '{lead.NomeOuRazaoSocial}' rejeitado: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        return;
    }

    db.ChangeTracker.Clear();
}

/// <summary>Npgsql só aceita DateTimeOffset com Offset=0 em colunas timestamptz — normaliza pra UTC.</summary>
static DateTimeOffset? ParseUtc(string? texto) =>
    DateTimeOffset.TryParse(texto, out var valor) ? valor.ToUniversalTime() : null;

/// <summary>
/// Corrige o "CriadoEm" dos leads já migrados: o insert original passou pela auditoria automática
/// do ApplicationDbContext, que sobrescreve CriadoEm com a data/hora do próprio SaveChanges para
/// toda entidade nova — por isso todo lead migrado ficou com a data da migração em vez da data real
/// do Notion. Casa por CPF (chave já usada na deduplicação) e corrige via SQL bruto, que não passa
/// pelo SaveChanges/auditoria.
/// </summary>
async Task CorrigirDatasCriacaoAsync()
{
    var corrigidosPorCpf = 0;
    var corrigidosPorTelefone = 0;
    var semChave = 0;

    foreach (var spec in specs)
    {
        Console.WriteLine($"\n=== Corrigindo datas: {spec.RegionalName} ===");
        var total = 0;

        await foreach (var page in notion.QueryVendaConcluidaAsync(spec.DataSourceId))
        {
            total++;

            var criadoEm = ParseUtc(page.CreatedTime("Data de chegada"));
            if (criadoEm is null)
            {
                semChave++;
                continue;
            }

            var cpfBruto = page.Text("CPF") ?? page.Number("CPF")?.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            var documentoNormalizado = DocumentValidation.NormalizarDocumento(cpfBruto, out _);
            if (documentoNormalizado is { Length: > 14 }) documentoNormalizado = null;

            if (documentoNormalizado is not null)
            {
                var linhas = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"CrmLeads\" SET \"CriadoEm\" = {criadoEm.Value} WHERE \"DocumentoNormalizado\" = {documentoNormalizado} AND \"Origem\" = 'Migração Notion'");
                corrigidosPorCpf += linhas;
            }
            else
            {
                // Sem CPF: usa o telefone como chave alternativa, só pra leads que ainda não foram
                // corrigidos por CPF (DocumentoNormalizado nulo) e ainda estão com a data da migração
                // — evita sobrescrever um lead que por acaso tenha o mesmo telefone mas já foi casado certo.
                var telefoneBruto = page.Text("WhatsApp") ?? page.Text("[META] Phone Number");
                var telefoneNormalizado = DocumentValidation.NormalizarTelefone(telefoneBruto);
                if (telefoneNormalizado is { Length: > 0 and <= 20 })
                {
                    var linhas = await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE \"CrmLeads\" SET \"CriadoEm\" = {criadoEm.Value} WHERE \"TelefoneNormalizado\" = {telefoneNormalizado} AND \"DocumentoNormalizado\" IS NULL AND \"Origem\" = 'Migração Notion' AND \"CriadoEm\" > NOW() - INTERVAL '3 days'");
                    corrigidosPorTelefone += linhas;
                }
                else
                {
                    semChave++;
                }
            }

            if (total % 1000 == 0) Console.WriteLine($"  ... {total} processados (cpf: {corrigidosPorCpf}, telefone: {corrigidosPorTelefone})");
        }

        Console.WriteLine($"  Total: {total} lidos");
    }

    Console.WriteLine($"\n=== {corrigidosPorCpf} corrigidos por CPF | {corrigidosPorTelefone} corrigidos por telefone | {semChave} sem nenhuma chave para casar ===");
}

/// <summary>
/// Corrige dois campos dos leads já migrados que a migração original não capturou:
/// - CriadoManualmente: false quando a propriedade "O que" do Notion tem qualquer valor (AGV,
///   APVS, Loovi etc. — veio de tráfego pago/campanha), true quando está vazia (indicação/venda
///   sem campanha identificada). Casa por CPF (chave primária) ou telefone (chave alternativa,
///   só para leads sem CPF), igual ao fixdates.
/// - TipoIndicacao: valor de "Tpo de Indicação?"/"TIPO INDICAÇAO?"/"Tipo de indicação" (nome varia
///   por base), que a migração original nunca gravou apesar do campo já existir no CRM.
/// </summary>
async Task CorrigirOrigemEIndicacaoAsync()
{
    var corrigidosPorCpf = 0;
    var corrigidosPorTelefone = 0;
    var semChave = 0;

    foreach (var spec in specs)
    {
        Console.WriteLine($"\n=== Corrigindo origem/indicação: {spec.RegionalName} ===");
        var total = 0;

        await foreach (var page in notion.QueryVendaConcluidaAsync(spec.DataSourceId))
        {
            total++;

            var oQue = page.Select("O que");
            var criadoManualmente = string.IsNullOrWhiteSpace(oQue);
            var tipoIndicacao = page.Select("Tpo de Indicação?", "Tpo de Indicação? ", "TIPO INDICAÇAO?", "TIPO INDICAÇAO? ", "Tipo de indicação");

            var cpfBruto = page.Text("CPF") ?? page.Number("CPF")?.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            var documentoNormalizado = DocumentValidation.NormalizarDocumento(cpfBruto, out _);
            if (documentoNormalizado is { Length: > 14 }) documentoNormalizado = null;

            if (documentoNormalizado is not null)
            {
                var linhas = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"CrmLeads\" SET \"CriadoManualmente\" = {criadoManualmente}, \"TipoIndicacao\" = {tipoIndicacao} WHERE \"DocumentoNormalizado\" = {documentoNormalizado} AND \"Origem\" = 'Migração Notion'");
                corrigidosPorCpf += linhas;
            }
            else
            {
                var telefoneBruto = page.Text("WhatsApp") ?? page.Text("[META] Phone Number");
                var telefoneNormalizado = DocumentValidation.NormalizarTelefone(telefoneBruto);
                if (telefoneNormalizado is { Length: > 0 and <= 20 })
                {
                    var linhas = await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE \"CrmLeads\" SET \"CriadoManualmente\" = {criadoManualmente}, \"TipoIndicacao\" = {tipoIndicacao} WHERE \"TelefoneNormalizado\" = {telefoneNormalizado} AND \"DocumentoNormalizado\" IS NULL AND \"Origem\" = 'Migração Notion'");
                    corrigidosPorTelefone += linhas;
                }
                else
                {
                    semChave++;
                }
            }

            if (total % 1000 == 0) Console.WriteLine($"  ... {total} processados (cpf: {corrigidosPorCpf}, telefone: {corrigidosPorTelefone})");
        }

        Console.WriteLine($"  Total: {total} lidos");
    }

    Console.WriteLine($"\n=== {corrigidosPorCpf} corrigidos por CPF | {corrigidosPorTelefone} corrigidos por telefone | {semChave} sem nenhuma chave para casar ===");
}

/// <summary>
/// Revisita todas as páginas do Notion (as 4 bases) e, pra cada lead/oportunidade já migrado cujo
/// responsável hoje é o placeholder "Vendedor não identificado", tenta resolver o vendedor de
/// verdade de novo — útil depois de habilitar "Read user information including email addresses"
/// na integração do Notion, que antes fazia a API devolver as pessoas sem nome/e-mail.
/// </summary>
async Task CorrigirVendedoresAsync()
{
    var corrigidosLead = 0;
    var corrigidosOportunidade = 0;
    var semCorrespondencia = 0;
    var aindaSemVendedor = 0;

    async Task<CrmLead?> BuscarLeadAsync(JsonElement page)
    {
        var cpfBruto = page.Text("CPF") ?? page.Number("CPF")?.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        var documentoNormalizado = DocumentValidation.NormalizarDocumento(cpfBruto, out _);
        if (documentoNormalizado is { Length: > 14 }) documentoNormalizado = null;
        if (documentoNormalizado is not null)
        {
            var porDocumento = await db.CrmLeads.FirstOrDefaultAsync(l => l.DocumentoNormalizado == documentoNormalizado);
            if (porDocumento is not null) return porDocumento;
        }

        var emailNormalizado = DocumentValidation.NormalizarEmail(page.Text("E-mail", "[META] Email"));
        if (emailNormalizado is not null)
        {
            var porEmail = await db.CrmLeads.FirstOrDefaultAsync(l => l.EmailNormalizado == emailNormalizado);
            if (porEmail is not null) return porEmail;
        }

        var telefoneNormalizado = DocumentValidation.NormalizarTelefone(page.Text("WhatsApp") ?? page.Text("[META] Phone Number"));
        if (telefoneNormalizado is { Length: > 0 and <= 20 })
        {
            return await db.CrmLeads.FirstOrDefaultAsync(l => l.TelefoneNormalizado == telefoneNormalizado);
        }

        return null;
    }

    async Task ProcessarPaginaAsync(JsonElement page, Guid regionalId, Guid placeholderId)
    {
        var vendedorInfo = page.PrimeiroVendedor("Vendedor");
        var vendedorId = await ResolverVendedorAsync(vendedorInfo, regionalId);
        if (vendedorId == placeholderId) { aindaSemVendedor++; return; }

        var lead = await BuscarLeadAsync(page);
        if (lead is null) { semCorrespondencia++; return; }

        if (lead.ResponsavelId is null || lead.ResponsavelId == placeholderId)
        {
            lead.ResponsavelId = vendedorId;
            corrigidosLead++;
        }

        var oportunidade = await db.CrmOpportunities.FirstOrDefaultAsync(o => o.LeadId == lead.Id && !o.Arquivado);
        if (oportunidade is not null && oportunidade.ResponsavelId == placeholderId)
        {
            oportunidade.ResponsavelId = vendedorId;
            corrigidosOportunidade++;
        }
    }

    foreach (var spec in specs)
    {
        Console.WriteLine($"\n=== Corrigindo vendedores (venda concluída): {spec.RegionalName} ===");
        var regional = await db.CrmRegionais.FirstAsync(r => r.Nome == spec.RegionalName);
        var placeholderId = await ObterOuCriarVendedorPlaceholderAsync(regional.Id);

        var total = 0;
        await foreach (var page in notion.QueryVendaConcluidaAsync(spec.DataSourceId))
        {
            total++;
            await ProcessarPaginaAsync(page, regional.Id, placeholderId);
            if (total % 500 == 0)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"  ... {total} lidos, {corrigidosLead} leads corrigidos, {corrigidosOportunidade} oportunidades corrigidas");
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"  Total: {total} lidos");
    }

    // CSS Growth Sales também tem leads fora da "venda concluída" (fatiado pelo mesmo motivo do
    // importnovos: teto de ~10 mil resultados por consulta na base maior).
    var growthSales = specs.First(s => s.RegionalName == "CSS Growth Sales");
    var growthSalesRegional = await db.CrmRegionais.FirstAsync(r => r.Nome == growthSales.RegionalName);
    var growthSalesPlaceholderId = await ObterOuCriarVendedorPlaceholderAsync(growthSalesRegional.Id);

    (string? Status, DateOnly? Antes, DateOnly? Apartir)[] fatias =
    [
        ("COTAÇÃO", null, null),
        ("PERDIDO", null, null),
        ("NÃO FAZEMOS ", null, null),
        ("RECUSA/INATIVA", null, null),
        ("EM ATENDIMENTO", new DateOnly(2025, 1, 1), null),
        ("EM ATENDIMENTO", null, new DateOnly(2025, 1, 1)),
    ];

    foreach (var (status, antes, apartir) in fatias)
    {
        Console.WriteLine($"\n=== Corrigindo vendedores (CSS Growth Sales, não venda concluída): status={status} antes={antes} apartir={apartir} ===");
        var total = 0;
        await foreach (var page in notion.QueryNaoVendaConcluidaAsync(growthSales.DataSourceId, status, antes, apartir))
        {
            total++;
            await ProcessarPaginaAsync(page, growthSalesRegional.Id, growthSalesPlaceholderId);
            if (total % 500 == 0)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"  ... {total} lidos, {corrigidosLead} leads corrigidos, {corrigidosOportunidade} oportunidades corrigidas");
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"  Total: {total} lidos");
    }

    Console.WriteLine($"\n=== {corrigidosLead} leads corrigidos | {corrigidosOportunidade} oportunidades corrigidas | {semCorrespondencia} sem lead correspondente | {aindaSemVendedor} ainda sem vendedor resolvido ===");
}

/// <summary>
/// Preenche, nas oportunidades "Ganho" já migradas (Venda concluída), os campos que a migração
/// original não tinha (foram adicionados ao CrmOpportunity depois): Cpf, Estado, Indicação,
/// ValorIndicacao, TipoIndicacao, Total e os anexos (Termo de Adesão / Comprovante de pagamento,
/// baixados do Notion e salvos em wwwroot/uploads). Casa por CPF (chave primária) ou telefone
/// (chave alternativa, só quando não há CPF), igual ao fixdates/fixorigin. Só escreve em campos
/// que ainda estão vazios — nunca sobrescreve algo que um consultor já tenha preenchido à mão.
/// </summary>
async Task EnriquecerVendaConcluidaAsync()
{
    var wwwroot = Environment.GetEnvironmentVariable("WWWROOT_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CssVision.Web", "wwwroot");
    wwwroot = Path.GetFullPath(wwwroot);
    Console.WriteLine($"wwwroot: {wwwroot}");

    var atualizados = 0;
    var semCorrespondencia = 0;
    var comArquivo = 0;
    var total = 0;

    foreach (var spec in specs)
    {
        Console.WriteLine($"\n=== Enriquecendo Venda concluída: {spec.RegionalName} ===");
        var totalBase = 0;

        await foreach (var page in notion.QueryVendaConcluidaAsync(spec.DataSourceId))
        {
            if (limitePorBase.HasValue && totalBase >= limitePorBase.Value) break;
            totalBase++;
            total++;
            try
            {
                var cpfBruto = page.Text("CPF") ?? page.Number("CPF")?.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                var documentoNormalizado = DocumentValidation.NormalizarDocumento(cpfBruto, out _);
                if (documentoNormalizado is { Length: > 14 }) documentoNormalizado = null;

                CrmLead? lead = null;
                if (documentoNormalizado is not null)
                {
                    lead = await db.CrmLeads.FirstOrDefaultAsync(l => l.DocumentoNormalizado == documentoNormalizado && l.Origem == "Migração Notion");
                }
                if (lead is null)
                {
                    var telefoneBruto = page.Text("WhatsApp") ?? page.Text("[META] Phone Number");
                    var telefoneNormalizado = DocumentValidation.NormalizarTelefone(telefoneBruto);
                    if (telefoneNormalizado is { Length: > 0 and <= 20 })
                    {
                        lead = await db.CrmLeads.FirstOrDefaultAsync(l =>
                            l.TelefoneNormalizado == telefoneNormalizado && l.DocumentoNormalizado == null && l.Origem == "Migração Notion");
                    }
                }

                if (lead is null)
                {
                    semCorrespondencia++;
                    continue;
                }

                var oportunidade = await db.CrmOpportunities.Include(o => o.Veiculo)
                    .FirstOrDefaultAsync(o => o.LeadId == lead.Id && o.Etapa.Tipo == TipoEtapaPipeline.Ganho);
                if (oportunidade is null)
                {
                    semCorrespondencia++;
                    continue;
                }

                var estadoTexto = page.Text("ESTADO");
                var estado = page.Select("Estado") ?? (estadoTexto is { Length: 2 } ? estadoTexto : null);
                var tipoIndicacao = page.Select("Tpo de Indicação?", "Tpo de Indicação? ", "TIPO INDICAÇAO?", "TIPO INDICAÇAO? ", "Tipo de indicação");
                var indicacaoFlag = page.Select("Indicação?");

                oportunidade.Cpf ??= documentoNormalizado;
                oportunidade.Estado ??= estado;
                oportunidade.TipoIndicacao ??= tipoIndicacao;
                oportunidade.Indicacao ??= indicacaoFlag is not null;
                oportunidade.ValorIndicacao ??= page.Number("Indicação") is { } vi ? (decimal)vi : null;
                oportunidade.Total ??= page.FormulaDecimal("Total");

                if (string.IsNullOrEmpty(oportunidade.TermoAdesaoArquivoUrl) && page.PrimeiroArquivo("Termo Adesão") is { } termo)
                {
                    var url = await SalvarAnexoAsync(wwwroot, oportunidade.Id, "termo-adesao", termo);
                    if (url is not null) { oportunidade.TermoAdesaoArquivoUrl = url; oportunidade.TermoAdesaoAceito = true; comArquivo++; }
                }
                if (string.IsNullOrEmpty(oportunidade.PagamentoAdesaoArquivoUrl) && page.PrimeiroArquivo("Pagamento Adesão") is { } pagamento)
                {
                    var url = await SalvarAnexoAsync(wwwroot, oportunidade.Id, "pagamento-adesao", pagamento);
                    if (url is not null) { oportunidade.PagamentoAdesaoArquivoUrl = url; comArquivo++; }
                }

                atualizados++;
            }
            catch (Exception ex)
            {
                relatorio.Add($"  [enrich/{spec.RegionalName}] Erro na linha {totalBase}: {ex.Message}");
            }

            if (total % 100 == 0)
            {
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                Console.WriteLine($"  ... {total} lidos, {atualizados} atualizados, {comArquivo} anexos, {semCorrespondencia} sem correspondência");
            }
        }

        Console.WriteLine($"  Total {spec.RegionalName}: {totalBase} lidos");
    }

    await db.SaveChangesAsync();
    Console.WriteLine($"\n=== {atualizados} oportunidades atualizadas | {comArquivo} anexos salvos | {semCorrespondencia} sem correspondência (de {total} lidos) ===");
}

async Task<string?> SalvarAnexoAsync(string wwwroot, Guid opportunityId, string tipo, NotionPageExtensions.ArquivoInfo arquivo)
{
    var bytes = await notion.DownloadFileAsync(arquivo.Url);
    if (bytes is null || bytes.Length == 0) return null;

    var extensao = Path.GetExtension(arquivo.Nome);
    if (string.IsNullOrWhiteSpace(extensao)) extensao = ".pdf";
    var pasta = Path.Combine(wwwroot, "uploads", "opportunities", opportunityId.ToString());
    Directory.CreateDirectory(pasta);
    var nomeArquivo = $"{tipo}{extensao}";
    await File.WriteAllBytesAsync(Path.Combine(pasta, nomeArquivo), bytes);
    return $"/uploads/opportunities/{opportunityId}/{nomeArquivo}";
}

/// <summary>
/// Importa como leads novos (sem oportunidade) todas as linhas da base CSS Growth Sales cujo Status
/// não é "VENDA CONCLUIDA" (essas já foram migradas) — Em atendimento, Cotação, Perdido, Não fazemos,
/// Recusa/Inativa. Escopo intencionalmente restrito a uma regional (ver conversa com o usuário).
/// </summary>
async Task ImportarNovosLeadsAsync()
{
    const string growthSalesDataSourceId = "31763799-99a8-81b2-b83b-000bfca82506";
    const string regionalNome = "CSS Growth Sales";

    var regional = await db.CrmRegionais.FirstOrDefaultAsync(r => r.Nome == regionalNome)
        ?? throw new InvalidOperationException($"Regional '{regionalNome}' não encontrada — rode a migração original primeiro.");

    var etapasPorNome = await db.CrmLeadStages.ToDictionaryAsync(s => s.Nome.Trim(), s => s.Id);
    var statusParaEtapa = new Dictionary<string, string>
    {
        ["EM ATENDIMENTO"] = "Em atendimento",
        ["COTAÇÃO"] = "Cotação",
        ["PERDIDO"] = "Perdido",
        ["NÃO FAZEMOS"] = "Não fazemos",
        ["RECUSA/INATIVA"] = "Recusa/Inativa",
    };

    var documentosExistentesLocal = (await db.CrmLeads.AsNoTracking().Where(l => l.DocumentoNormalizado != null && !l.Arquivado)
        .Select(l => l.DocumentoNormalizado!).ToListAsync()).ToHashSet();
    var telefonesExistentes = (await db.CrmLeads.AsNoTracking().Where(l => l.TelefoneNormalizado != null && !l.Arquivado)
        .Select(l => l.TelefoneNormalizado!).ToListAsync()).ToHashSet();
    var emailsExistentesLocal = (await db.CrmLeads.AsNoTracking().Where(l => l.EmailNormalizado != null && !l.Arquivado)
        .Select(l => l.EmailNormalizado!).ToListAsync()).ToHashSet();

    var vendedorPlaceholderId = await ObterOuCriarVendedorPlaceholderAsync(regional.Id);

    // Fatiamento opcional (necessário pro teto de ~10 mil resultados por consulta — ver NotionClient).
    var statusFiltro = Environment.GetEnvironmentVariable("MIGRATION_STATUS_FILTER");
    var criadoAntesDe = DateOnly.TryParse(Environment.GetEnvironmentVariable("MIGRATION_CREATED_BEFORE"), out var antes) ? antes : (DateOnly?)null;
    var criadoApartirDe = DateOnly.TryParse(Environment.GetEnvironmentVariable("MIGRATION_CREATED_AFTER"), out var apartir) ? apartir : (DateOnly?)null;
    Console.WriteLine($"Filtro: status={statusFiltro ?? "(todos, exceto Venda concluída)"} antes={criadoAntesDe} apartir={criadoApartirDe}");

    var total = 0;
    var importados = 0;
    var duplicados = 0;
    var comErro = 0;
    var statusNaoMapeado = new Dictionary<string, int>();
    var semNome = 0;
    var excecoes = new List<string>();
    var lote = new List<CrmLead>();

    await foreach (var page in notion.QueryNaoVendaConcluidaAsync(growthSalesDataSourceId, statusFiltro, criadoAntesDe, criadoApartirDe))
    {
        if (limitePorBase.HasValue && total >= limitePorBase.Value) break;
        total++;
        try
        {
            var statusBruto = page.Select("Status")?.Trim();
            if (statusBruto is null || !statusParaEtapa.TryGetValue(statusBruto, out var etapaNome) || !etapasPorNome.TryGetValue(etapaNome, out var etapaId))
            {
                comErro++;
                var chave = statusBruto ?? "(vazio)";
                statusNaoMapeado[chave] = statusNaoMapeado.GetValueOrDefault(chave) + 1;
                continue;
            }

            var nome = page.Text("Name");
            if (string.IsNullOrWhiteSpace(nome)) { comErro++; semNome++; continue; }
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

            if ((documentoNormalizado is not null && documentosExistentesLocal.Contains(documentoNormalizado)) ||
                (telefoneNormalizado is not null && telefonesExistentes.Contains(telefoneNormalizado)) ||
                (emailNormalizado is not null && emailsExistentesLocal.Contains(emailNormalizado)))
            {
                duplicados++;
                continue;
            }

            var estadoTexto = page.Text("ESTADO");
            var estado = page.Select("Estado") ?? (estadoTexto is { Length: 2 } ? estadoTexto : null);
            var oQue = page.Select("O que");
            var criadoEmTexto = page.CreatedTime("Data de chegada");

            var lead = new CrmLead
            {
                NomeOuRazaoSocial = nome.Trim(),
                TipoPessoa = documentoNormalizado?.Length == 14 ? TipoPessoa.Juridica : TipoPessoa.Fisica,
                DocumentoNormalizado = documentoNormalizado,
                Telefone = telefone,
                TelefoneNormalizado = telefoneNormalizado,
                WhatsApp = whatsapp,
                Email = emailBruto,
                EmailNormalizado = emailNormalizado,
                Estado = estado,
                Regional = regionalNome,
                Origem = "Migração Notion",
                Campanha = page.Select("Campanha", "CAMPANHA"),
                ProdutoInteresse = oQue,
                Placa = page.Text("Placa") is { Length: <= 10 } placaValida ? placaValida : null,
                EtapaId = etapaId,
                ResponsavelId = vendedorPlaceholderId,
                CriadoManualmente = string.IsNullOrWhiteSpace(oQue),
                TipoIndicacao = NotionLeadClassifier.Classificar(oQue),
                ConsentimentoContato = true,
                ConsentimentoOrigem = "Migração da base histórica (Notion)",
                Arquivado = false,
            };
            if (ParseUtc(criadoEmTexto) is { } criadoEm) lead.CriadoEm = criadoEm;

            if (documentoNormalizado is not null) documentosExistentesLocal.Add(documentoNormalizado);
            if (telefoneNormalizado is not null) telefonesExistentes.Add(telefoneNormalizado);
            if (emailNormalizado is not null) emailsExistentesLocal.Add(emailNormalizado);

            lote.Add(lead);
            importados++;
        }
        catch (Exception ex)
        {
            comErro++;
            excecoes.Add($"linha {total}: {ex.GetType().Name}: {ex.Message}");
        }

        if (lote.Count >= 200)
        {
            var rejeitados = await SalvarLoteLeadsAsync(lote);
            comErro += rejeitados;
            importados -= rejeitados;
            lote.Clear();
            Console.WriteLine($"  ... {total} lidos, {importados} importados, {duplicados} duplicados, {comErro} com erro");
        }
    }

    if (lote.Count > 0)
    {
        var rejeitados = await SalvarLoteLeadsAsync(lote);
        comErro += rejeitados;
        importados -= rejeitados;
    }

    Console.WriteLine($"\n=== Importação de novos leads (CSS Growth Sales): {total} lidos | {importados} importados | {duplicados} duplicados | {comErro} com erro ===");
    Console.WriteLine($"  Detalhe dos erros: {semNome} sem nome, {excecoes.Count} exceções, {comErro - semNome - excecoes.Count} status não mapeado");
    if (statusNaoMapeado.Count > 0)
    {
        Console.WriteLine("  Status não mapeados (valor => ocorrências):");
        foreach (var (status, qtd) in statusNaoMapeado.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"    \"{status}\" => {qtd}");
    }
    if (excecoes.Count > 0)
    {
        Console.WriteLine("  Primeiras exceções:");
        foreach (var linha in excecoes.Take(20))
            Console.WriteLine($"    {linha}");
    }
    if (relatorio.Count > 0)
    {
        Console.WriteLine("  Leads rejeitados por constraint no banco:");
        foreach (var linha in relatorio)
            Console.WriteLine(linha);
    }
}

/// <summary>Salva o lote de leads; se uma constraint (ex: e-mail duplicado que escapou da checagem em
/// memória) rejeitar o lote inteiro, refaz linha por linha pra isolar só a(s) linha(s) problemática(s).
/// Retorna quantos leads do lote foram rejeitados.</summary>
async Task<int> SalvarLoteLeadsAsync(List<CrmLead> lote)
{
    db.CrmLeads.AddRange(lote);
    try
    {
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return 0;
    }
    catch (DbUpdateException)
    {
        db.ChangeTracker.Clear();
        var rejeitados = 0;
        foreach (var lead in lote)
        {
            await using var retryScope = provider.CreateAsyncScope();
            var retryDb = retryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            retryDb.CrmLeads.Add(lead);
            try
            {
                await retryDb.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                rejeitados++;
                relatorio.Add($"  [importnovos] Lead '{lead.NomeOuRazaoSocial}' rejeitado: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
        return rejeitados;
    }
}

record DatabaseSpec(string DataSourceId, string RegionalName);
