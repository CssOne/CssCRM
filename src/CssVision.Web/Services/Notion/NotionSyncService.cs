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
    /// <summary>Valor de CrmLead.NotionVendedorEmail para card sem vendedor.</summary>
    public const string SemVendedor = "(sem vendedor)";

    /// <summary>Ids dos usuários ativos no CRM (carregados junto com _emailsAtivos).</summary>
    private HashSet<Guid>? _idsAtivos;
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

    /// <summary>Início da reimportação dos cards dos consultores ativos (antes disso as bases não têm cards).</summary>
    private static readonly DateOnly InicioHistoricoNotion = new(2018, 1, 1);

    /// <summary>E-mails (minúsculos) dos usuários ativos no CRM — os cards deles entram sempre, sem data mínima.</summary>
    private HashSet<string>? _emailsAtivos;

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
        // Reimportação (uma vez por base, depois do realinhamento): relê a base inteira, de todas as
        // datas, e traz com todos os campos os cards cujo Vendedor é um usuário ativo no CRM.
        var reimportarAtivos = !realinhar && checkpoint!.ReimportacaoAtivosConcluidaEm is null;
        await CarregarUsuariosAtivosAsync(ct);
        var paginas = realinhar
            ? PaginasCriadasDesdeAsync(notion, dataSourceId, DataMinimaImportacao, ct)
            : reimportarAtivos
                ? PaginasCriadasDesdeAsync(notion, dataSourceId, InicioHistoricoNotion, ct)
                // Sem filtro de data no Notion: cards antigos de consultores ativos também entram (o
                // corte pela data mínima para os demais é feito abaixo, página a página).
                : notion.QueryEditadasDesdeAsync(dataSourceId, checkpoint!.UltimaSincronizacaoEm - JanelaSobreposicao, null, ct);

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

        int processados = 0, criados = 0, atualizados = 0, erros = 0, semNome = 0, ignorados = 0, devolvidos = 0;

        await foreach (var page in paginas)
        {
            var doConsultorAtivo = DeConsultorAtivo(page);

            // Reimportação, card de outro vendedor (inativo, fora do CRM ou sem vendedor): não traz
            // o card, só tira o lead de um consultor ativo que tenha ficado com ele por engano.
            if (reimportarAtivos && !doConsultorAtivo)
            {
                try
                {
                    if (await DevolverAoVendedorDoCardAsync(page, regional.Id, regionalNome, placeholderVendedorId, ct)) devolvidos++;
                    else ignorados++;
                }
                catch (Exception ex)
                {
                    erros++;
                    logger.LogWarning(ex, "Erro conferindo o vendedor da pagina {PageId} ({Regional})", page.PageId(), regionalNome);
                    db.ChangeTracker.Clear();
                }
                continue;
            }

            // Incremental: cards anteriores à data mínima só entram se forem de um consultor ativo.
            if (!realinhar && !reimportarAtivos && !doConsultorAtivo && CriadoAntesDaDataMinima(page))
            {
                ignorados++;
                continue;
            }

            processados++;
            // Card sem título e sem nenhum dado de contato (centenas em algumas bases) não tem como
            // virar lead — a não ser que seja de um consultor ativo: aí entra com um nome provisório.
            if (string.IsNullOrWhiteSpace(page.Text("Name")) && !doConsultorAtivo
                && string.IsNullOrWhiteSpace(PrimeiroTexto(page, "WhatsApp", "[META] Phone Number", "E-mail", "[META] Email")))
            {
                semNome++;
                continue;
            }

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

        // Recarrega: um erro em qualquer página chama ChangeTracker.Clear(), que descarta a instância
        // lida no início — sem isto a gravação abaixo não salvava nada e o checkpoint (e o fim do
        // realinhamento) nunca era registrado, fazendo a base inteira ser relida a cada ciclo.
        checkpoint = await db.CrmNotionSyncCheckpoints.FindAsync([dataSourceId], ct);
        if (checkpoint is null)
        {
            checkpoint = new CrmNotionSyncCheckpoint { DataSourceId = dataSourceId, RegionalNome = regionalNome };
            db.CrmNotionSyncCheckpoints.Add(checkpoint);
        }
        // A reimportação relê a base inteira e demora: não adianta o checkpoint do incremental (as
        // edições feitas durante ela são pegas na próxima execução, a partir do checkpoint anterior).
        if (!reimportarAtivos) checkpoint.UltimaSincronizacaoEm = inicioDaExecucao;
        if (realinhar) checkpoint.RealinhamentoConcluidoEm = inicioDaExecucao;
        if (reimportarAtivos) checkpoint.ReimportacaoAtivosConcluidaEm = inicioDaExecucao;
        await db.SaveChangesAsync(ct);

        if (_mudancasNoQuadro > 0)
        {
            eventos?.PublicarQuadroAtualizado("notion");
            _mudancasNoQuadro = 0;
        }

        var modo = realinhar ? " (realinhamento)" : reimportarAtivos ? " (reimportação dos consultores ativos)" : "";
        return $"{regionalNome}: {processados} processados, {criados} criados, {atualizados} atualizados, {devolvidos} devolvidos ao vendedor do card, {erros} erros, {semNome} sem nome, {ignorados} ignorados{modo}";
    }

    private async Task CarregarUsuariosAtivosAsync(CancellationToken ct)
    {
        if (_emailsAtivos is not null && _idsAtivos is not null) return;
        var ativos = await db.Users.AsNoTracking().Where(u => u.Ativo).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        _idsAtivos = ativos.Select(u => u.Id).ToHashSet();
        _emailsAtivos = ativos.Where(u => u.Email != null).Select(u => u.Email!.Trim().ToLowerInvariant()).ToHashSet();
    }

    /// <summary>Chave do vendedor do card (e-mail minúsculo) — ver CrmLead.NotionVendedorEmail.</summary>
    private static string ChaveVendedor(NotionPageExtensions.VendedorInfo? vendedor) =>
        string.IsNullOrWhiteSpace(vendedor?.Email) ? SemVendedor : vendedor.Email.Trim().ToLowerInvariant();

    /// <summary>
    /// Card de um vendedor que não é consultor ativo: se o lead dele (vindo do Notion) está com um
    /// consultor ativo sem ninguém ter atribuído à mão no CRM, devolve ao vendedor do card (ou ao
    /// "Vendedor não identificado" da base). Não cria lead nem mexe nos outros campos.
    /// </summary>
    /// <returns>true se o lead foi devolvido.</returns>
    internal async Task<bool> DevolverAoVendedorDoCardAsync(JsonElement page, Guid regionalId, string regionalNome, Guid placeholderVendedorId, CancellationToken ct)
    {
        await CarregarUsuariosAtivosAsync(ct);
        var nome = page.Text("Name") ?? PrimeiroTexto(page, "WhatsApp", "[META] Phone Number", "E-mail", "[META] Email") ?? "Sem nome (Notion)";
        var (documento, documentoSemZero) = NormalizarCpfNotion(page.Text("CPF") ?? page.Number("CPF")?.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
        var email = DocumentValidation.NormalizarEmail(page.Text("E-mail", "[META] Email"));
        var telefone = DocumentValidation.NormalizarTelefone(PrimeiroTexto(page, "WhatsApp", "[META] Phone Number"));
        var (lead, arquivado) = await EncontrarLeadAsync(
            new IdentificacaoNotion(page.PageId(), nome.Trim(), regionalNome, documento, documentoSemZero, email, telefone), ct);
        if (lead is null || arquivado) return false;

        var vendedorInfo = page.PrimeiroVendedor("Vendedor");
        var vendedorId = await ResolverVendedorAsync(vendedorInfo, regionalId, regionalNome, placeholderVendedorId, ct);
        var anterior = lead.ResponsavelId;
        await AjustarResponsavelAsync(lead, vendedorId, ChaveVendedor(vendedorInfo), ct);
        await db.SaveChangesAsync(ct);
        if (anterior == lead.ResponsavelId) return false;
        _mudancasNoQuadro++;
        return true;
    }

    /// <summary>
    /// Responsável de um lead que já existia, conforme o vendedor do card. Só age quando o vendedor
    /// do card mudou desde a última sincronização (ou nunca foi registrado):
    /// - vendedor é consultor ativo: o lead passa para ele;
    /// - vendedor inativo, fora do CRM ou card sem vendedor: se o lead é do Notion e está com um
    ///   consultor ativo, volta para o vendedor do card — cards de outros vendedores não ficam com
    ///   os consultores ativos.
    /// Lead que alguém atribuiu à mão no CRM (histórico de atribuição) não é mexido.
    /// </summary>
    private async Task AjustarResponsavelAsync(CrmLead lead, Guid vendedorId, string chaveVendedor, CancellationToken ct)
    {
        var mudouNoNotion = lead.NotionVendedorEmail != chaveVendedor;
        lead.NotionVendedorEmail = chaveVendedor;
        if (!mudouNoNotion || vendedorId == lead.ResponsavelId) return;
        if (await db.CrmLeadAssignmentHistories.AnyAsync(h => h.LeadId == lead.Id, ct)) return;

        if (_idsAtivos!.Contains(vendedorId))
        {
            lead.ResponsavelId = vendedorId;
            return;
        }

        var doNotion = lead.ConsentimentoOrigem is OrigemLead.MarcadorMigracaoNotion or OrigemLead.MarcadorSincronizacaoNotion;
        if (doNotion && (lead.ResponsavelId is null || _idsAtivos.Contains(lead.ResponsavelId.Value)))
        {
            lead.ResponsavelId = vendedorId;
        }
    }

    private bool DeConsultorAtivo(JsonElement page) =>
        page.PrimeiroVendedor("Vendedor")?.Email is { } email && _emailsAtivos?.Contains(email.Trim().ToLowerInvariant()) == true;

    private static bool CriadoAntesDaDataMinima(JsonElement page) =>
        ParseUtc(page.CreatedTime("Data de chegada")) is { } criadoEm
        && DateOnly.FromDateTime(criadoEm.UtcDateTime) < DataMinimaImportacao;

    /// <param name="somenteColuna">
    /// Realinhamento: só ajusta a coluna de leads que já existem no CRM — não cria leads (um lead
    /// arquivado/excluído no CRM voltaria como novo), não cria usuários de vendedor e não mexe em
    /// responsável nem nos demais campos. Isso continua a cargo da sincronização incremental.
    /// </param>
    /// <returns>true se criou um lead novo, false se atualizou um existente.</returns>
    internal async Task<bool> ProcessarPaginaAsync(
        JsonElement page, Guid regionalId, string regionalNome, Dictionary<string, Guid> etapasPorNome,
        Guid ganhoStageId, Guid placeholderVendedorId, bool somenteColuna, CancellationToken ct)
    {
        // Card sem título: entra com um nome provisório (telefone/e-mail) para não perder o card.
        var nome = page.Text("Name")
            ?? PrimeiroTexto(page, "WhatsApp", "[META] Phone Number", "E-mail", "[META] Email")
            ?? "Sem nome (Notion)";
        if (nome.Length > 200) nome = nome[..200];

        var cpfBruto = page.Text("CPF") ?? page.Number("CPF")?.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        var (documentoNormalizado, documentoSemZero) = NormalizarCpfNotion(cpfBruto);

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

        // O campo "WhatsApp" às vezes traz texto no lugar do número ("WhatsApp", "O< não", o e-mail
        // da pessoa...) enquanto "[META] Phone Number" tem o telefone certo — prefere o que é telefone.
        if (!NomeTelefoneHeuristica.PareceTelefone(telefone) && NomeTelefoneHeuristica.PareceTelefone(telefoneMeta))
        {
            telefone = telefoneMeta;
        }

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

        var pageId = page.PageId();
        var (lead, arquivado) = await EncontrarLeadAsync(
            new IdentificacaoNotion(pageId, nome.Trim(), regionalNome, documentoNormalizado, documentoSemZero, emailNormalizado, telefoneNormalizado), ct);

        // Card ligado a um lead que alguém arquivou/excluiu no CRM: respeita — não recria nem mexe.
        if (arquivado) return false;

        var placa = NormalizarPlaca(page.Text("Placa"));

        if (somenteColuna)
        {
            if (lead is null) return false;
            // Além da coluna, o realinhamento só COMPLETA campos vazios — nunca sobrescreve o CRM:
            // vínculo com o card, placa (mostrada no cartão) e telefone/e-mail (para o cartão não
            // ficar "sem contato" e para as próximas sincronizações acharem o lead).
            lead.NotionPageId ??= pageId;
            if (string.IsNullOrWhiteSpace(lead.Placa)) lead.Placa = placa;
            if (lead.TelefoneNormalizado is null && telefoneNormalizado is not null)
            {
                lead.Telefone = telefone;
                lead.TelefoneNormalizado = telefoneNormalizado;
            }
            if (lead.EmailNormalizado is null && emailNormalizado is not null
                && !await db.CrmLeads.AnyAsync(l => l.EmailNormalizado == emailNormalizado && !l.Arquivado, ct))
            {
                lead.Email = emailBruto;
                lead.EmailNormalizado = emailNormalizado;
            }
            if (await AplicarStatusDoNotionAsync(lead, status, page.Select("Motivo da perda"), page.Text("Veiculo"), etapasPorNome, ct))
            {
                _mudancasNoQuadro++;
            }
            await db.SaveChangesAsync(ct);
            return false;
        }

        await CarregarUsuariosAtivosAsync(ct);
        var vendedorInfo = page.PrimeiroVendedor("Vendedor");
        var chaveVendedor = ChaveVendedor(vendedorInfo);
        var vendedorId = await ResolverVendedorAsync(vendedorInfo, regionalId, regionalNome, placeholderVendedorId, ct);

        var criadoAgora = lead is null;
        if (lead is null)
        {
            lead = new CrmLead
            {
                TipoPessoa = documentoNormalizado?.Length == 14 ? TipoPessoa.Juridica : TipoPessoa.Fisica,
                Regional = regionalNome,
                // Origem = tag de campanha do card (Lookalike, UGC...); sem tag, a origem técnica.
                Origem = OrigemLead.TagDaCampanha(page.Select("Campanha", "CAMPANHA")) ?? OrigemLead.OrigemSincronizacaoNotion,
                // Sempre o vendedor do card (ou o "Vendedor não identificado" da base): card de um
                // vendedor inativo ou fora do CRM não vai para o rodízio dos consultores ativos.
                ResponsavelId = vendedorId,
                NotionVendedorEmail = chaveVendedor,
                ConsentimentoContato = true,
                ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion,
                Arquivado = false,
            };
            if (ParseUtc(page.CreatedTime("Data de chegada")) is { } criadoEm) lead.CriadoEm = criadoEm;
            db.CrmLeads.Add(lead);
        }

        lead.NotionPageId ??= pageId;
        lead.NomeOuRazaoSocial = nome.Trim();
        // Só preenche, e só se nenhum outro lead ativo já usa o documento (índice único) — senão o
        // card inteiro falhava ao salvar e o lead ficava sem nenhum dado do Notion.
        if (lead.DocumentoNormalizado is null && documentoNormalizado is not null
            && !await OutroLeadUsaAsync(lead, l => l.DocumentoNormalizado == documentoNormalizado && !l.Arquivado, ct))
        {
            lead.DocumentoNormalizado = documentoNormalizado;
        }
        lead.Telefone = Cortar(telefone, 40) ?? lead.Telefone;
        lead.TelefoneNormalizado = telefoneNormalizado ?? lead.TelefoneNormalizado;
        lead.Telefone2 = Cortar(telefoneSegundo, 40) ?? lead.Telefone2;
        lead.Telefone2Normalizado = telefone2Normalizado ?? lead.Telefone2Normalizado;
        lead.WhatsApp = NomeTelefoneHeuristica.PareceTelefone(whatsapp) ? Cortar(whatsapp, 40) : lead.WhatsApp;
        // Mesma coisa com o e-mail (índice único): se já é de outro lead, mantém o do lead.
        if (emailNormalizado is { Length: <= 256 } && emailNormalizado != lead.EmailNormalizado
            && !await OutroLeadUsaAsync(lead, l => l.EmailNormalizado == emailNormalizado && !l.Arquivado, ct))
        {
            lead.Email = emailBruto;
            lead.EmailNormalizado = emailNormalizado;
        }
        lead.Cidade = Cortar(cidade, 120) ?? lead.Cidade;
        lead.Estado = estado ?? lead.Estado;
        lead.Campanha = Cortar(page.Select("Campanha", "CAMPANHA"), 120) ?? lead.Campanha;
        // Leads do Notion (migrados ou sincronizados): a Origem acompanha a tag de campanha do card.
        if (lead.ConsentimentoOrigem is OrigemLead.MarcadorMigracaoNotion or OrigemLead.MarcadorSincronizacaoNotion
            && OrigemLead.TagDaCampanha(lead.Campanha) is { } tagCampanha)
        {
            lead.Origem = tagCampanha;
        }
        lead.ProdutoInteresse = Cortar(oQue, 120) ?? lead.ProdutoInteresse;
        lead.TipoIndicacao = tipoIndicacao;
        lead.CriadoManualmente = NotionLeadClassifier.CriadoManualmente(oQue);
        lead.Placa = placa ?? lead.Placa;
        lead.Gclid = Cortar(page.Text("GCLID"), 200) ?? lead.Gclid;
        lead.UtmSource = Cortar(page.Text("UTM SOURCE"), 120) ?? lead.UtmSource;
        lead.UtmMedium = Cortar(page.Text("UTM MEDIUM"), 120) ?? lead.UtmMedium;
        lead.UtmTerm = Cortar(page.Text("UTM TERM"), 120) ?? lead.UtmTerm;
        lead.MetaClickId = Cortar(page.Text("[META] Click ID"), 200) ?? lead.MetaClickId;
        lead.MetaFormId = Cortar(page.Text("[META] Form"), 120) ?? lead.MetaFormId;
        // ID do lead no Meta também é único (inclusive entre arquivados).
        if (page.Text("[META] Lead ID") is { Length: <= 120 } metaLeadId && lead.MetaLeadId is null
            && !await OutroLeadUsaAsync(lead, l => l.MetaLeadId == metaLeadId, ct))
        {
            lead.MetaLeadId = metaLeadId;
        }
        // Lead que já existia: o responsável acompanha o vendedor do card (ver AjustarResponsavelAsync).
        if (!criadoAgora) await AjustarResponsavelAsync(lead, vendedorId, chaveVendedor, ct);

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

    /// <summary>Todos os cards criados desde <paramref name="desde"/>, em fatias mensais (cada consulta do Notion tem teto de ~10 mil linhas).</summary>
    private static async IAsyncEnumerable<JsonElement> PaginasCriadasDesdeAsync(
        NotionClient notion, string dataSourceId, DateOnly desde, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var amanha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        for (var inicio = desde; inicio < amanha; inicio = inicio.AddMonths(1))
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

    /// <returns>
    /// O usuário do campo "Vendedor" do card (ativo ou não — o lead é dele); o placeholder da base
    /// quando o card não tem vendedor.
    /// </returns>
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
            // Vendedor que só existe no Notion entra inativo: não recebe leads do tráfego pago nem
            // aparece como consultor até um administrador ativá-lo.
            Ativo = false,
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

    public sealed record IdentificacaoNotion(
        string PageId, string Nome, string RegionalNome,
        string? Documento, string? DocumentoSemZero, string? Email, string? Telefone);

    /// <summary>
    /// Acha o lead de um card do Notion, da chave mais confiável para a menos confiável: o próprio
    /// card (NotionPageId), CPF/CNPJ válido, e-mail, telefone e, por último, o nome — só para leads
    /// da sincronização ainda sem vínculo, na mesma regional e quando houver exatamente um.
    /// </summary>
    /// <returns>O lead (ou null) e se o card está ligado a um lead arquivado.</returns>
    public async Task<(CrmLead? Lead, bool Arquivado)> EncontrarLeadAsync(IdentificacaoNotion id, CancellationToken ct)
    {
        var vinculado = await db.CrmLeads.FirstOrDefaultAsync(l => l.NotionPageId == id.PageId, ct);
        if (vinculado is not null) return (vinculado.Arquivado ? null : vinculado, vinculado.Arquivado);

        CrmLead? lead = null;
        if (id.Documento is not null)
        {
            var semZero = id.DocumentoSemZero ?? id.Documento;
            lead = await db.CrmLeads.FirstOrDefaultAsync(
                l => (l.DocumentoNormalizado == id.Documento || l.DocumentoNormalizado == semZero) && !l.Arquivado, ct);
        }
        lead ??= id.Email is not null
            ? await db.CrmLeads.FirstOrDefaultAsync(l => l.EmailNormalizado == id.Email && !l.Arquivado, ct)
            : null;
        lead ??= id.Telefone is not null
            ? await db.CrmLeads.FirstOrDefaultAsync(l => l.TelefoneNormalizado == id.Telefone && !l.Arquivado, ct)
            : null;
        if (lead is not null) return (lead, false);

        var mesmoNome = await db.CrmLeads
            .Where(l => l.NomeOuRazaoSocial == id.Nome && l.Regional == id.RegionalNome
                && l.ConsentimentoOrigem == OrigemLead.MarcadorSincronizacaoNotion && l.NotionPageId == null && !l.Arquivado)
            .Take(2)
            .ToListAsync(ct);
        return (mesmoNome.Count == 1 ? mesmoNome[0] : null, false);
    }

    /// <summary>
    /// CPF/CNPJ do card, só se for válido — um número qualquer ("100.309" vira "100") fazia o card
    /// casar com o lead de outro cliente. No Notion o CPF é número, então CPFs que começam com 0
    /// chegam com 10 (ou 9) dígitos: completa com zeros e devolve também a forma sem os zeros, que
    /// é como parte dos leads antigos foi gravada.
    /// </summary>
    public static (string? Documento, string? DocumentoSemZero) NormalizarCpfNotion(string? bruto)
    {
        var digitos = DocumentValidation.SomenteDigitos(bruto);
        if (digitos.Length is 9 or 10)
        {
            var completo = digitos.PadLeft(11, '0');
            return DocumentValidation.ValidarCpf(completo) ? (completo, digitos) : (null, null);
        }

        var normalizado = DocumentValidation.NormalizarDocumento(digitos, out var valido);
        return valido ? (normalizado, null) : (null, null);
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

    /// <summary>Outro lead (que não este) já tem o valor — considera também os ainda não salvos desta execução.</summary>
    private async Task<bool> OutroLeadUsaAsync(CrmLead lead, System.Linq.Expressions.Expression<Func<CrmLead, bool>> filtro, CancellationToken ct)
    {
        var compilado = filtro.Compile();
        if (db.CrmLeads.Local.Any(l => l.Id != lead.Id && compilado(l))) return true;
        return await db.CrmLeads.Where(filtro).AnyAsync(l => l.Id != lead.Id, ct);
    }

    /// <summary>O primeiro campo preenchido (cada nome consultado separadamente — Text() para no primeiro que existe, mesmo vazio).</summary>
    private static string? PrimeiroTexto(JsonElement page, params string[] nomes) =>
        nomes.Select(n => page.Text(n)).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

    private static string? Cortar(string? texto, int max)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        return t.Length > max ? t[..max] : t;
    }

    private static DateTimeOffset? ParseUtc(string? texto) =>
        DateTimeOffset.TryParse(texto, out var valor) ? valor.ToUniversalTime() : null;
}
