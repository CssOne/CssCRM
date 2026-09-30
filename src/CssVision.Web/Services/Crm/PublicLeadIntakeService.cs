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
        // Texto de formulário externo nunca pode derrubar o lead por estourar o tamanho da coluna
        // (CrmLeadConfiguration): o estado vira a sigla da UF e o resto é cortado no limite.
        request = request with
        {
            Nome = Cortar(request.Nome, 200) ?? "",
            Estado = UnidadeFederativa.Sigla(request.Estado),
            Fonte = Cortar(request.Fonte, 80),
            Campanha = Cortar(request.Campanha, 120),
            Oque = Cortar(request.Oque, 120),
            UtilidadeVeiculo = Cortar(request.UtilidadeVeiculo, 80),
            Gclid = Cortar(request.Gclid, 200),
            ClickId = Cortar(request.ClickId, 200),
            Projeto = Cortar(request.Projeto, 120),
            UtmSource = Cortar(request.UtmSource, 120),
            UtmMedium = Cortar(request.UtmMedium, 120),
            UtmTerm = Cortar(request.UtmTerm, 120),
            UtmCampaign = Cortar(request.UtmCampaign, 200),
        };

        // Mesmo Meta Lead ID = mesmo lead, mesmo se já tiver sido excluído (arquivado): o reenvio da
        // reconciliação não pode criar outro — o índice único do MetaLeadId recusaria e a resposta
        // seria 500, repetida a cada reenvio.
        if (!string.IsNullOrWhiteSpace(request.MetaLeadId))
        {
            var mesmoMetaLeadId = await db.CrmLeads.AsNoTracking()
                .Where(l => l.MetaLeadId == request.MetaLeadId)
                .Select(l => new { l.Id, l.ResponsavelId })
                .FirstOrDefaultAsync(ct);
            if (mesmoMetaLeadId is not null)
            {
                logger.LogInformation("Lead do site com Meta Lead ID {MetaLeadId} já recebido antes ({LeadId}) — nada a criar", request.MetaLeadId, mesmoMetaLeadId.Id);
                return await MontarResultadoAsync(mesmoMetaLeadId.Id, mesmoMetaLeadId.ResponsavelId, ct);
            }
        }

        var existente = await EncontrarLeadExistenteAsync(emailNormalizado, telefoneNormalizado, ct);
        if (existente is not null)
        {
            await AtualizarContatoExistenteAsync(existente, request, telefoneNormalizado, ct);
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
            UtmCampaign = request.UtmCampaign,
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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Corrida de concorrência: o webhook em tempo real e a reconciliação (Worker) podem
            // tentar criar o MESMO lead novo quase ao mesmo tempo — ambos acham "não existe ainda"
            // (EncontrarLeadExistenteAsync não vê a escrita um do outro até algum commitar) e
            // tentam inserir uma linha nova cada um, com o mesmo Meta Lead ID. A segunda a
            // commitar bate no índice único e cai aqui. Não é a mesma duplicata antiga de e-mail —
            // essa é criada NA HORA, pela própria corrida. Em vez de derrubar a submissão, assume
            // que a outra tentativa venceu, busca o registro que ela acabou de criar e faz o
            // mesmo tratamento de atualização usado pra contato já existente.
            logger.LogWarning(ex, "Corrida detectada ao criar lead (projeto {Projeto}, Meta Lead ID {MetaLeadId}) — buscando o registro que a tentativa concorrente criou", request.Projeto, request.MetaLeadId);

            try
            {
                var criadoPelaOutraTentativa = await EncontrarLeadExistenteAsync(emailNormalizado, telefoneNormalizado, ct);
                if (criadoPelaOutraTentativa is null)
                {
                    throw ex;
                }

                await AtualizarContatoExistenteAsync(criadoPelaOutraTentativa, request, telefoneNormalizado, ct);
                return await MontarResultadoAsync(criadoPelaOutraTentativa.Id, criadoPelaOutraTentativa.ResponsavelId, ct);
            }
            catch (Exception recuperacaoEx)
            {
                // Se até essa recuperação falhar (ex: o DbContext não sobrevive a uma query depois
                // do SaveChangesAsync que já deu erro), não insiste mais — loga com o máximo de
                // contexto possível e deixa subir. Preferível a mascarar um 2º erro dentro do 1º.
                logger.LogError(recuperacaoEx, "Falha ao recuperar da corrida de criação (projeto {Projeto}, Meta Lead ID {MetaLeadId})", request.Projeto, request.MetaLeadId);
                throw;
            }
        }

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
    /// Atualiza um contato já existente com o que essa nova submissão trouxe de novidade — nunca
    /// sobrescreve o que já está preenchido (o consultor pode ter corrigido/completado algo na
    /// tela), só preenche o que estava vazio:
    /// (1) contato: sem isso, um lead que dedupa pra um registro sem telefone/WhatsApp (ex: card
    ///     criado por engano sem esse dado) ficava pra sempre sem jeito de contato nenhum — o
    ///     consultor via o nome mas não conseguia ligar/chamar no WhatsApp;
    /// (2) classificação de tráfego pago: um contato antigo (ex: migrado do Notion) pode mandar um
    ///     lead novo de verdade hoje — sem isso, o card ficava preso à classificação antiga e sumia
    ///     do filtro "Tráfego pago" do quadro;
    /// (3) Meta Lead ID: sem vincular, a reconciliação (Worker) nunca confirmava por
    ///     GET /by-meta-lead-id que aquele lead específico já tinha chegado, e ficava reenviando ele
    ///     pra sempre;
    /// (4) responsável: um registro duplicado pode ter ficado sem ninguém atribuído (ex: criado por
    ///     um caminho que não passa pelo rodízio) — sem isso, o lead ficava pra sempre sem dono
    ///     nenhum, mesmo recebendo submissões novas.
    /// </summary>
    private async Task AtualizarContatoExistenteAsync(CrmLead existente, PublicLeadCreateRequest request, string? telefoneNormalizado, CancellationToken ct)
    {
        var mudou = false;

        if (existente.ResponsavelId is null)
        {
            existente.ResponsavelId = await ResolverResponsavelAsync(request.Projeto, request.Oque, ct);
            mudou = true;
        }

        if (string.IsNullOrWhiteSpace(existente.WhatsApp) && !string.IsNullOrWhiteSpace(request.WhatsApp))
        {
            existente.Telefone = request.WhatsApp;
            existente.TelefoneNormalizado = telefoneNormalizado;
            existente.WhatsApp = request.WhatsApp;
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.Telefone2) && !string.IsNullOrWhiteSpace(request.Telefone2))
        {
            existente.Telefone2 = request.Telefone2;
            existente.Telefone2Normalizado = DocumentValidation.NormalizarTelefone(request.Telefone2);
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.Estado) && !string.IsNullOrWhiteSpace(request.Estado))
        {
            existente.Estado = request.Estado.ToUpperInvariant();
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.Placa) && request.Placa?.Trim().ToUpperInvariant() is { Length: > 0 and <= 10 } placaValida)
        {
            existente.Placa = placaValida;
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.UtilidadeVeiculo) && !string.IsNullOrWhiteSpace(request.UtilidadeVeiculo))
        {
            existente.UtilidadeVeiculo = request.UtilidadeVeiculo;
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.UtmSource) && !string.IsNullOrWhiteSpace(request.UtmSource))
        {
            existente.UtmSource = request.UtmSource;
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.UtmMedium) && !string.IsNullOrWhiteSpace(request.UtmMedium))
        {
            existente.UtmMedium = request.UtmMedium;
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.UtmTerm) && !string.IsNullOrWhiteSpace(request.UtmTerm))
        {
            existente.UtmTerm = request.UtmTerm;
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.UtmCampaign) && !string.IsNullOrWhiteSpace(request.UtmCampaign))
        {
            existente.UtmCampaign = request.UtmCampaign;
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.Gclid) && !string.IsNullOrWhiteSpace(request.Gclid))
        {
            existente.Gclid = request.Gclid;
            mudou = true;
        }
        if (string.IsNullOrWhiteSpace(existente.MetaClickId) && !string.IsNullOrWhiteSpace(request.ClickId))
        {
            existente.MetaClickId = request.ClickId;
            mudou = true;
        }

        var jaClassificado = OrigemLead.VeioDoTrafegoPago.Compile()(existente);
        var precisaVincularMetaLeadId = existente.MetaLeadId is null && request.MetaLeadId is not null;
        if (!jaClassificado)
        {
            existente.ConsentimentoOrigem = OrigemLead.MarcadorFormularioSite;
            mudou = true;
        }
        if (precisaVincularMetaLeadId)
        {
            existente.MetaLeadId = request.MetaLeadId;
            mudou = true;
        }

        if (!mudou) return;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Existem contatos duplicados na base (mesmo e-mail em duas linhas, herança da migração
            // do Notion) — se ESTE registro não for o que já guarda o Meta Lead ID, vincular aqui
            // bate no índice único e não pode derrubar a criação/atualização do lead por causa
            // disso. NÃO dá pra rodar mais nada nesse DbContext depois daqui (a transação do
            // Postgres já foi abortada pelo erro — até um Reload() simples lançaria de novo); só
            // loga e sai. As outras atualizações desse lote (contato/classificação) também são
            // perdidas nesse caso raro — mas o lead em si não pode falhar por causa disso.
            logger.LogWarning(ex, "Não foi possível atualizar o lead {LeadId} (provável duplicata de e-mail/telefone com outro registro que já tem o Meta Lead ID {MetaLeadId})", existente.Id, request.MetaLeadId);
            return;
        }

        eventos?.PublicarQuadroAtualizado("site");
        logger.LogInformation("Lead {LeadId} atualizado pelo intake público (contato preenchido e/ou classificação/vínculo do Meta Lead ID)", existente.Id);
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

    private static string? Cortar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpo = texto.Trim();
        return limpo.Length <= maximo ? limpo : limpo[..maximo];
    }

    private async Task<CrmLead?> EncontrarLeadExistenteAsync(string? emailNormalizado, string? telefoneNormalizado, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(emailNormalizado))
        {
            // Card principal primeiro (os veículos adicionais do cliente repetem o e-mail).
            var porEmail = await db.CrmLeads
                .Where(l => l.EmailNormalizado == emailNormalizado && !l.Arquivado)
                .OrderBy(l => l.VeiculoAdicionalDeLeadId != null)
                .FirstOrDefaultAsync(ct);
            if (porEmail is not null) return porEmail;
        }

        if (!string.IsNullOrEmpty(telefoneNormalizado))
        {
            return await db.CrmLeads
                .Where(l => l.TelefoneNormalizado == telefoneNormalizado && !l.Arquivado)
                .OrderBy(l => l.VeiculoAdicionalDeLeadId != null)
                .FirstOrDefaultAsync(ct);
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
