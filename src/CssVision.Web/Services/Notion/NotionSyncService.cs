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
public sealed class NotionSyncService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ILogger<NotionSyncService> logger,
    ICrmEventHub? eventos = null)
{
    private static readonly (string DataSourceId, string RegionalName)[] DataSources =
    [
        ("0f91c248-497e-4369-aad9-1d4266a49db4", "MG132"),
        ("1a163799-99a8-81e2-82be-000bb2817da0", "MG134"),
        ("31763799-99a8-8118-b573-000b24781bfe", "MG134 Consultores Externos"),
        ("31763799-99a8-81b2-b83b-000bfca82506", "CSS Growth Sales"),
    ];

    /// <summary>A partir desta data (Data de chegada do card no Notion), a sincronização periódica
    /// passa a importar/atualizar leads — cards mais antigos deixam de entrar no CRM mesmo que
    /// alguém ainda os edite no Notion.</summary>
    private static readonly DateOnly DataMinimaImportacao = new(2026, 1, 1);

    /// <summary>
    /// last_edited_time do Notion tem precisão de minuto: cada execução relê uma pequena janela antes
    /// do checkpoint para não perder edições feitas no mesmo minuto da execução anterior.
    /// </summary>
    private static readonly TimeSpan JanelaSobreposicao = TimeSpan.FromMinutes(2);

    /// <summary>Leads criados ou que mudaram de coluna nesta execução — dispara a atualização das telas abertas.</summary>
    private int _mudancasNoQuadro;

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

        // Realinhamento (uma vez por base): relê todos os cards desde a data mínima para colocar cada
        // lead na coluna do Status atual do Notion — inclusive os que ficaram em "Sem etapa" porque a
        // sincronização antiga só definia a coluna na criação do lead. Depois disso, só o incremental.
        var realinhar = checkpoint?.RealinhamentoConcluidoEm is null;
        var paginas = realinhar
            ? PaginasParaRealinhamentoAsync(notion, dataSourceId, ct)
            : notion.QueryEditadasDesdeAsync(dataSourceId, checkpoint!.UltimaSincronizacaoEm - JanelaSobreposicao, DataMinimaImportacao, ct);

        var regional = await db.CrmRegionais.FirstOrDefaultAsync(r => r.Nome == regionalNome, ct);
        if (regional is null)
        {
            regional = new CrmRegional { Nome = regionalNome };
            db.CrmRegionais.Add(regional);
            await db.SaveChangesAsync(ct);
        }

        // Só colunas ativas: "Pré-cadastro" e "Recusa/Inativa" foram desativadas (ver NotionEtapaLead).
        var etapasPorNome = await db.CrmLeadStages.Where(s => s.Ativa).ToDictionaryAsync(s => s.Nome.Trim(), s => s.Id, ct);
        var ganhoStageId = (await db.CrmPipelineStages.FirstAsync(s => s.Tipo == TipoEtapaPipeline.Ganho, ct)).Id;
        var placeholderVendedorId = await ObterOuCriarVendedorPlaceholderAsync(regional.Id, regionalNome, ct);

        int processados = 0, criados = 0, atualizados = 0, erros = 0;

        await foreach (var page in paginas)
        {
            processados++;
            try
            {
                var resultado = await ProcessarPaginaAsync(page, regional.Id, regionalNome, etapasPorNome, ganhoStageId, placeholderVendedorId, somenteColuna: realinhar, ct);
                if (resultado) criados++; else atualizados++;
            }
            catch (Exception ex)
            {
                erros++;
                logger.LogWarning(ex, "Erro sincronizando pagina {PageId} ({Regional})", page.PageId(), regionalNome);
                // Uma falha de SaveChangesAsync deixa a entidade inválida presa no change tracker —
                // sem isso, toda gravação seguinte (mesmo de páginas OK, de outras fontes até) falha
                // tentando persistir de novo a mesma entidade quebrada.
                db.ChangeTracker.Clear();
            }
        }

        if (checkpoint is null)
        {
            checkpoint = new CrmNotionSyncCheckpoint { DataSourceId = dataSourceId, RegionalNome = regionalNome };
            db.CrmNotionSyncCheckpoints.Add(checkpoint);
        }
        checkpoint.UltimaSincronizacaoEm = inicioDaExecucao;
        if (realinhar) checkpoint.RealinhamentoConcluidoEm = inicioDaExecucao;
        await db.SaveChangesAsync(ct);

        if (_mudancasNoQuadro > 0)
        {
            eventos?.PublicarQuadroAtualizado("notion");
            _mudancasNoQuadro = 0;
        }

        return $"{regionalNome}: {processados} processados, {criados} criados, {atualizados} atualizados, {erros} erros{(realinhar ? " (realinhamento)" : "")}";
    }

    /// <param name="somenteColuna">
    /// Realinhamento: só ajusta a coluna de leads que já existem no CRM — não cria leads (um lead
    /// arquivado/excluído no CRM voltaria como novo), não cria usuários de vendedor e não mexe em
    /// responsável nem nos demais campos. Isso continua a cargo da sincronização incremental.
    /// </param>
    /// <returns>true se criou um lead novo, false se atualizou um existente.</returns>
    private async Task<bool> ProcessarPaginaAsync(
        JsonElement page, Guid regionalId, string regionalNome, Dictionary<string, Guid> etapasPorNome,
        Guid ganhoStageId, Guid placeholderVendedorId, bool somenteColuna, CancellationToken ct)
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

        // Alguns registros têm nome e telefone trocados na origem (ver NomeTelefoneHeuristica) — se
        // for o caso, corrige aqui antes de gravar.
        if (NomeTelefoneHeuristica.EstaoTrocados(nome, telefone))
        {
            (nome, telefone) = (telefone!.Trim(), nome);
        }

        // Alguns registros têm dois telefones colados no mesmo campo, sem separador — separa aqui.
        var (telefonePrimeiro, telefoneSegundo) = NomeTelefoneHeuristica.SepararTelefones(telefone);
        if (telefonePrimeiro is not null) telefone = telefonePrimeiro;

        var telefoneNormalizado = DocumentValidation.NormalizarTelefone(telefone);
        if (telefoneNormalizado is { Length: > 20 }) telefoneNormalizado = null;
        var telefone2Normalizado = DocumentValidation.NormalizarTelefone(telefoneSegundo);
        if (telefone2Normalizado is { Length: > 20 }) telefone2Normalizado = null;

        var estadoTexto = page.Text("ESTADO");
        var estadoSelect = page.Select("Estado");
        var estado = estadoSelect is { Length: 2 } ? estadoSelect : (estadoTexto is { Length: 2 } ? estadoTexto : null);
        var cidade = page.Text("Cidade");
        var oQue = page.Select("O que");
        var tipoIndicacao = NotionLeadClassifier.Classificar(oQue);

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

        var placa = NormalizarPlaca(page.Text("Placa"));

        if (somenteColuna)
        {
            if (lead is null) return false;
            // Único campo além da coluna que o realinhamento toca: completa a placa (mostrada no
            // cartão do quadro) só quando o lead ainda não tem uma — nunca sobrescreve a do CRM.
            if (string.IsNullOrWhiteSpace(lead.Placa)) lead.Placa = placa;
            if (await AplicarStatusDoNotionAsync(lead, status, page.Select("Motivo da perda"), page.Text("Veiculo"), etapasPorNome, ct))
            {
                _mudancasNoQuadro++;
            }
            await db.SaveChangesAsync(ct);
            return false;
        }

        var vendedorInfo = page.PrimeiroVendedor("Vendedor");
        var vendedorId = await ResolverVendedorAsync(vendedorInfo, regionalId, regionalNome, placeholderVendedorId, ct);

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
        lead.Telefone2 = telefoneSegundo ?? lead.Telefone2;
        lead.Telefone2Normalizado = telefone2Normalizado ?? lead.Telefone2Normalizado;
        lead.WhatsApp = whatsapp ?? lead.WhatsApp;
        lead.Email = emailBruto ?? lead.Email;
        lead.EmailNormalizado = emailNormalizado ?? lead.EmailNormalizado;
        lead.Cidade = cidade ?? lead.Cidade;
        lead.Estado = estado ?? lead.Estado;
        lead.Campanha = page.Select("Campanha", "CAMPANHA") ?? lead.Campanha;
        lead.ProdutoInteresse = oQue ?? lead.ProdutoInteresse;
        lead.TipoIndicacao = tipoIndicacao;
        lead.CriadoManualmente = NotionLeadClassifier.CriadoManualmente(oQue);
        lead.Placa = placa ?? lead.Placa;
        lead.Gclid = page.Text("GCLID") ?? lead.Gclid;
        lead.UtmSource = page.Text("UTM SOURCE") ?? lead.UtmSource;
        lead.UtmMedium = page.Text("UTM MEDIUM") ?? lead.UtmMedium;
        lead.UtmTerm = page.Text("UTM TERM") ?? lead.UtmTerm;
        lead.MetaClickId = page.Text("[META] Click ID") ?? lead.MetaClickId;
        lead.MetaFormId = page.Text("[META] Form") ?? lead.MetaFormId;
        lead.MetaLeadId = page.Text("[META] Lead ID") ?? lead.MetaLeadId;
        if (vendedorId != placeholderVendedorId) lead.ResponsavelId = vendedorId;

        if (isVendaConcluida)
        {
            await CriarOuAtualizarOportunidadeAsync(page, lead, vendedorId, ganhoStageId, ct);
        }

        var mudouDeColuna = await AplicarStatusDoNotionAsync(lead, status, page.Select("Motivo da perda"), page.Text("Veiculo"), etapasPorNome, ct);

        await db.SaveChangesAsync(ct);
        if (criadoAgora || mudouDeColuna) _mudancasNoQuadro++;
        return criadoAgora;
    }

    /// <summary>
    /// Coloca o lead na coluna do quadro correspondente ao Status do Notion, com as mesmas regras de
    /// quem arrasta o cartão no quadro (LeadService.MudarEtapaAsync): "Perdido" sempre com motivo de
    /// perda, "Não fazemos" sempre com o veículo não atendido, e esses campos limpos nas demais colunas.
    /// Só move quando o Status mudou no Notion desde a última sincronização (ou quando o lead ainda
    /// está em "Sem etapa") — um movimento feito no CRM não é desfeito enquanto o Status lá não mudar.
    /// </summary>
    /// <returns>true se o lead mudou de coluna.</returns>
    public async Task<bool> AplicarStatusDoNotionAsync(
        CrmLead lead, string? statusNotion, string? motivoPerdaNotion, string? veiculoNotion,
        IReadOnlyDictionary<string, Guid> etapasAtivasPorNome, CancellationToken ct)
    {
        var statusNovo = NotionEtapaLead.Normalizar(statusNotion);
        var statusMudou = statusNovo != NotionEtapaLead.Normalizar(lead.NotionStatus);
        var semEtapaComStatus = lead.EtapaId is null && statusNovo is not null;
        var statusGravado = statusNotion?.Trim();
        lead.NotionStatus = statusGravado is { Length: > 80 } ? statusGravado[..80] : statusGravado;

        if (!statusMudou && !semEtapaComStatus) return false;

        var ehIndicacao = NotionEtapaLead.EhIndicacao(lead.CriadoManualmente, lead.TipoIndicacao);
        var (reconhecido, etapaId, nomeEtapa) = NotionEtapaLead.Resolver(statusNotion, ehIndicacao, etapasAtivasPorNome);
        if (!reconhecido)
        {
            logger.LogWarning("Status '{Status}' do Notion não tem coluna correspondente no quadro de leads; lead {LeadId} mantido na coluna atual.", statusNotion, lead.Id);
            return false;
        }
        if (etapaId == lead.EtapaId) return false;

        lead.EtapaId = etapaId;
        if (nomeEtapa == NotionEtapaLead.Perdido)
        {
            var descricao = string.IsNullOrWhiteSpace(motivoPerdaNotion) ? NotionEtapaLead.MotivoPerdaPadrao(statusNotion) : motivoPerdaNotion.Trim();
            lead.MotivoPerdaId = (await ObterOuCriarMotivoPerdaAsync(descricao, ct)).Id;
            lead.MotivoPerdaObservacao = null;
            lead.VeiculoNaoAtendido = null;
        }
        else if (nomeEtapa == NotionEtapaLead.NaoFazemos)
        {
            var veiculo = string.IsNullOrWhiteSpace(veiculoNotion) ? "Não informado no Notion" : veiculoNotion.Trim();
            lead.VeiculoNaoAtendido = veiculo.Length > 200 ? veiculo[..200] : veiculo;
            lead.MotivoPerdaId = null;
            lead.MotivoPerdaObservacao = null;
        }
        else
        {
            lead.MotivoPerdaId = null;
            lead.MotivoPerdaObservacao = null;
            lead.VeiculoNaoAtendido = null;
        }

        return true;
    }

    private async Task<CrmLossReason> ObterOuCriarMotivoPerdaAsync(string descricao, CancellationToken ct)
    {
        var motivo = db.CrmLossReasons.Local.FirstOrDefault(m => string.Equals(m.Descricao, descricao, StringComparison.OrdinalIgnoreCase))
            ?? await db.CrmLossReasons.FirstOrDefaultAsync(m => m.Descricao.ToLower() == descricao.ToLower(), ct);
        if (motivo is not null) return motivo;

        // Motivos usados no Notion ("Não responde", "Financeiro"...) passam a existir também no CRM,
        // disponíveis no diálogo de perda do quadro.
        motivo = new CrmLossReason { Descricao = descricao.Length > 200 ? descricao[..200] : descricao };
        db.CrmLossReasons.Add(motivo);
        return motivo;
    }

    /// <summary>Todos os cards desde a data mínima, em fatias mensais (cada consulta do Notion tem teto de ~10 mil linhas).</summary>
    private static async IAsyncEnumerable<JsonElement> PaginasParaRealinhamentoAsync(
        NotionClient notion, string dataSourceId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var amanha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        for (var inicio = DataMinimaImportacao; inicio < amanha; inicio = inicio.AddMonths(1))
        {
            var fim = inicio.AddMonths(1) < amanha ? inicio.AddMonths(1) : amanha;
            await foreach (var page in notion.QueryCriadasEntreAsync(dataSourceId, inicio, fim, ct))
            {
                yield return page;
            }
        }
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
        oportunidade.MensalidadeComCupom = page.Number("Mensalidade (Cupom)") is { } cupom ? (decimal)cupom : oportunidade.MensalidadeComCupom;
        oportunidade.PagamentoAdesao = adesao ?? oportunidade.PagamentoAdesao;
        oportunidade.Porcentagem = porcentagem ?? oportunidade.Porcentagem;
        oportunidade.ValorIndicacao = page.Number("Indicação") is { } valorIndicacao ? (decimal)valorIndicacao : oportunidade.ValorIndicacao;
        oportunidade.TermoAdesaoAceito = page.HasFiles("Termo Adesão") || oportunidade.TermoAdesaoAceito;
        oportunidade.Migracao = page.Select("Migração", "Migração?") is not null || oportunidade.Migracao;

        var motivoPerdaNome = page.Select("Motivo da perda");
        if (motivoPerdaNome is not null && oportunidade.MotivoPerdaId is null)
        {
            var motivo = await db.CrmLossReasons.FirstOrDefaultAsync(m => m.Descricao == motivoPerdaNome, ct);
            if (motivo is not null) oportunidade.MotivoPerdaId = motivo.Id;
        }

        var veiculo = oportunidade.Veiculo ??= new CrmVeiculo { OpportunityId = oportunidade.Id };
        veiculo.Descricao = page.Text("Veiculo") ?? veiculo.Descricao;
        veiculo.Placa = page.Text("Placa") is { Length: <= 10 } placaValida ? placaValida : veiculo.Placa;
        veiculo.Fipe = page.Number("FIPE") is { } fipe ? (decimal)fipe : veiculo.Fipe;
        veiculo.Rastreador = page.Number("Rastreador") is { } rastreador ? (decimal)rastreador : veiculo.Rastreador;
        veiculo.ValorVistoria = page.Number("Vistoriador") is { } vistoriador ? (decimal)vistoriador : veiculo.ValorVistoria;
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

    /// <summary>
    /// Placa em maiúsculas e até 10 caracteres, como no cadastro de lead (LeadService). Também tira
    /// espaços e hífen, comuns na digitação do Notion ("abc-1d23"), para o cartão mostrar um formato só.
    /// </summary>
    public static string? NormalizarPlaca(string? placa)
    {
        if (string.IsNullOrWhiteSpace(placa)) return null;
        var normalizada = placa.Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
        return normalizada.Length is > 0 and <= 10 ? normalizada : null;
    }

    private static DateTimeOffset? ParseUtc(string? texto) =>
        DateTimeOffset.TryParse(texto, out var valor) ? valor.ToUniversalTime() : null;
}
