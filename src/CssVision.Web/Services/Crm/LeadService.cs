using System.Linq.Expressions;
using ClosedXML.Excel;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Marketing;
using CssVision.Web.Services.Notion;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LeadService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEquipeComercialService equipe,
    ILeadAssignmentService assignment,
    IMetaConversionService conversion,
    IAuditSink audit,
    ICrmEventHub? eventos = null,
    Microsoft.Extensions.Options.IOptions<DistribuicaoOptions>? distribuicao = null) : ILeadService
{
    /// <summary>Nome da etapa terminal "perdida" do quadro de leads — ver CrmSeeder.cs. CrmLeadStage
    /// não tem um enum de tipo como CrmPipelineStage, então a identidade da etapa é pelo nome mesmo.</summary>
    private const string EtapaLeadPerdido = "Perdido";

    /// <summary>Nome da etapa "veículo fora do que a CSS Brasil atende" do quadro de leads — ver CrmSeeder.cs.</summary>
    private const string EtapaLeadNaoFazemos = "Não fazemos";

    /// <summary>
    /// Prefixo das etapas "Venda concluída (Leads)"/"Venda concluída (Indicação)" — é quando o
    /// evento de conversão pro Meta deve carregar o valor pago de verdade (pagamento de adesão);
    /// etapas antes disso (ex: Cotação) ainda não tiveram pagamento confirmado.
    /// </summary>
    private const string EtapaLeadVendaConcluida = "Venda concluída";

    /// <summary>Etapa "Cotação" do quadro de leads — só entra com o valor da adesão preenchido.</summary>
    private const string EtapaLeadCotacao = "Cotação";

    /// <summary>Coluna das vendas de indicação — ver <see cref="TipoIndicacaoLead.IndicacaoLead"/>.</summary>
    private const string EtapaLeadVendaConcluidaIndicacao = "Venda concluída (Indicação)";

    /// <summary>Máximo de cards criados de uma vez para outros veículos do mesmo cliente.</summary>
    private const int MaximoVeiculosAdicionais = 10;

    /// <summary>
    /// O campo Origem (de onde veio o lead) é informação só de administrador (Admin/GestorMaster):
    /// consultores e gestores comerciais não o recebem pela API, nem podem filtrar por ele.
    /// </summary>
    private bool PodeVerOrigem => currentUser.TemVisaoTotal;

    public async Task<PagedResult<LeadListItemDto>> ListarAsync(LeadFilterRequest filtro, CancellationToken ct)
    {
        if (!PodeVerOrigem) filtro = filtro with { Origem = null };
        var podeVerOrigem = PodeVerOrigem;
        var query = await QueryEscopadaAsync(filtro.IncluirArquivados, ct);
        query = AplicarFiltros(query, filtro);
        query = AplicarOrdenacao(query, filtro.OrdenarPor, filtro.OrdemDescendente);

        var total = await query.CountAsync(ct);

        var itens = await query
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .Select(l => new LeadListItemDto(
                l.Id,
                l.NomeOuRazaoSocial,
                l.TipoPessoa,
                l.DocumentoNormalizado,
                l.Telefone,
                l.Telefone2,
                l.Email,
                l.Cidade,
                l.Estado,
                l.Regional,
                podeVerOrigem ? l.Origem : null,
                l.Placa,
                l.TemSeguro,
                l.UtilidadeVeiculo,
                l.EtapaId,
                l.Etapa != null ? l.Etapa.Nome : null,
                l.Etapa != null ? l.Etapa.Cor : null,
                l.Oportunidades
                    .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Aberta)
                    .OrderByDescending(o => o.EtapaDesde)
                    .Select(o => o.Etapa.Nome)
                    .FirstOrDefault(),
                l.ResponsavelId,
                l.Responsavel != null ? l.Responsavel.NomeCompleto : null,
                l.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
                l.CriadoEm,
                l.UltimoContatoEm,
                l.ProximoContatoEm,
                string.IsNullOrWhiteSpace(l.Telefone) && string.IsNullOrWhiteSpace(l.Telefone2),
                l.Arquivado,
                null
            ))
            .ToListAsync(ct);

        // Colunas dos Relatórios do Notion (adesão, FIPE, mensalidade...): da venda do lead — a oportunidade ganha
        // mais recente, ou a mais recente que existir. Buscadas só para os leads desta página.
        var idsDaPagina = itens.Select(i => i.Id).ToList();
        var oportunidades = await db.CrmOpportunities.AsNoTracking()
            .Where(o => idsDaPagina.Contains(o.LeadId) && !o.Arquivado)
            .Select(o => new
            {
                o.LeadId,
                Ganha = o.Etapa.Tipo == TipoEtapaPipeline.Ganho,
                Data = o.DataEfetivaFechamento ?? o.CriadoEm,
                Venda = new LeadVendaResumoDto(
                    o.PagamentoAdesao, o.Veiculo != null ? o.Veiculo.Fipe : null, o.Mensalidade, o.MensalidadeComDesconto, o.Porcentagem,
                    o.Veiculo != null ? o.Veiculo.Rastreador : null, o.ValorIndicacao, o.Veiculo != null ? o.Veiculo.ValorVistoria : null,
                    o.Total ?? o.ValorFinal, o.DataEfetivaFechamento),
            })
            .ToListAsync(ct);
        var vendaPorLead = oportunidades
            .GroupBy(o => o.LeadId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.Ganha).ThenByDescending(o => o.Data).First().Venda);

        var mascarados = itens
            .Select(i => i with
            {
                DocumentoMascarado = DocumentValidation.MascararDocumento(i.DocumentoMascarado),
                Venda = vendaPorLead.GetValueOrDefault(i.Id),
            })
            .ToList();

        return new PagedResult<LeadListItemDto>
        {
            Itens = mascarados,
            Pagina = filtro.Pagina,
            TamanhoPagina = filtro.TamanhoPagina,
            TotalRegistros = total
        };
    }

    public async Task<LeadDetailDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(id, ct);
        return ParaDetailDto(lead, PodeVerOrigem);
    }

    public async Task<IReadOnlyList<LeadTimelineItemDto>> ObterTimelineAsync(Guid id, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(id, ct);
        var itens = new List<LeadTimelineItemDto>
        {
            new(lead.Id, TipoEventoTimeline.LeadCriado, "Lead cadastrado", lead.Origem is null || !PodeVerOrigem ? null : $"Origem: {lead.Origem}", null, lead.CriadoEm)
        };

        var auditorias = await db.CrmAuditLogs.AsNoTracking()
            .Where(a => a.EntidadeTipo == nameof(CrmLead) && a.EntidadeId == id && a.Acao == "LeadAtualizado")
            .Select(a => new { a.Id, a.OcorridoEm, Usuario = a.Usuario.NomeCompleto })
            .ToListAsync(ct);
        itens.AddRange(auditorias.Select(a =>
            new LeadTimelineItemDto(a.Id, TipoEventoTimeline.LeadAtualizado, "Cadastro atualizado", null, a.Usuario, a.OcorridoEm)));

        var atribuicoes = await db.CrmLeadAssignmentHistories.AsNoTracking()
            .Where(h => h.LeadId == id)
            .Select(h => new
            {
                h.Id,
                h.AlteradoEm,
                h.Motivo,
                ResponsavelNovo = h.ResponsavelNovo.NomeCompleto,
                AlteradoPor = h.AlteradoPor.NomeCompleto
            })
            .ToListAsync(ct);
        itens.AddRange(atribuicoes.Select(a =>
            new LeadTimelineItemDto(a.Id, TipoEventoTimeline.TrocaResponsavel, $"Atribuído para {a.ResponsavelNovo}", a.Motivo, a.AlteradoPor, a.AlteradoEm)));

        var oportunidadeIds = await db.CrmOpportunities.AsNoTracking()
            .Where(o => o.LeadId == id)
            .Select(o => o.Id)
            .ToListAsync(ct);

        if (oportunidadeIds.Count > 0)
        {
            var etapas = await db.CrmStageHistories.AsNoTracking()
                .Where(h => oportunidadeIds.Contains(h.OpportunityId))
                .Select(h => new
                {
                    h.Id,
                    h.AlteradoEm,
                    h.MotivoPerdaDescricao,
                    EtapaNova = h.EtapaNova.Nome,
                    EtapaTipo = h.EtapaNova.Tipo,
                    Usuario = h.Usuario.NomeCompleto
                })
                .ToListAsync(ct);
            itens.AddRange(etapas.Select(e => new LeadTimelineItemDto(
                e.Id,
                e.EtapaTipo switch
                {
                    TipoEtapaPipeline.Ganho => TipoEventoTimeline.OportunidadeGanha,
                    TipoEtapaPipeline.Perdido => TipoEventoTimeline.OportunidadePerdida,
                    _ => TipoEventoTimeline.MudancaEtapa
                },
                $"Movida para {e.EtapaNova}",
                e.MotivoPerdaDescricao,
                e.Usuario,
                e.AlteradoEm)));
        }

        var atividades = await db.CrmActivities.AsNoTracking()
            .Where(a => a.LeadId == id)
            .Select(a => new { a.Id, a.Tipo, a.Assunto, a.Resultado, a.DataHoraConclusao, a.DataHoraPrevista, Responsavel = a.Responsavel.NomeCompleto })
            .ToListAsync(ct);
        itens.AddRange(atividades.Select(a => new LeadTimelineItemDto(
            a.Id, TipoEventoTimeline.Atividade, a.Assunto, a.Resultado, a.Responsavel, a.DataHoraConclusao ?? a.DataHoraPrevista)));

        var notas = await db.CrmNotes.AsNoTracking()
            .Where(n => n.LeadId == id)
            .Select(n => new { n.Id, n.Texto, n.CriadoEm, Autor = n.Autor.NomeCompleto })
            .ToListAsync(ct);
        itens.AddRange(notas.Select(n => new LeadTimelineItemDto(n.Id, TipoEventoTimeline.Nota, "Anotação", n.Texto, n.Autor, n.CriadoEm)));

        var anexos = await db.CrmAttachments.AsNoTracking()
            .Where(a => a.LeadId == id)
            .Select(a => new { a.Id, a.NomeArquivo, a.CriadoEm, EnviadoPor = a.EnviadoPor.NomeCompleto })
            .ToListAsync(ct);
        itens.AddRange(anexos.Select(a => new LeadTimelineItemDto(a.Id, TipoEventoTimeline.Proposta, $"Arquivo enviado: {a.NomeArquivo}", null, a.EnviadoPor, a.CriadoEm)));

        return itens.OrderByDescending(i => i.OcorridoEm).ToList();
    }

    public async Task<CriarLeadResultado> CriarAsync(LeadCreateRequest request, CancellationToken ct)
    {
        var documentoNormalizado = DocumentValidation.NormalizarDocumento(request.Documento, out var documentoValido);
        if (!string.IsNullOrEmpty(documentoNormalizado) && !documentoValido)
        {
            throw new CrmBusinessException("CPF/CNPJ informado é inválido.", "documento_invalido");
        }

        var emailNormalizado = DocumentValidation.NormalizarEmail(request.Email);
        if (!string.IsNullOrWhiteSpace(request.Email) && emailNormalizado is null)
        {
            throw new CrmBusinessException("E-mail informado é inválido.", "email_invalido");
        }

        var telefoneNormalizado = DocumentValidation.NormalizarTelefone(request.Telefone);

        var duplicidade = await DetectarDuplicidadeAsync(documentoNormalizado, emailNormalizado, telefoneNormalizado, request.IgnorarDuplicidade, ct);
        if (duplicidade is not null) return new CriarLeadResultado(null, duplicidade);

        // Responsável explícito respeita o escopo de quem está criando. Sem escolha explícita:
        // uma vendedora cadastrando um contato dela mesma continua caindo pra ela (comportamento
        // intuitivo, "é meu"); só quando não há um dono natural (admin/gestor cadastrando sem
        // escolher alguém, ou nenhum usuário logado) a distribuição automática decide — round-robin
        // por quem tem menos leads no mês, respeitando o limite mensal de cada vendedor. Pode ficar
        // sem responsável se ninguém estiver elegível, do mesmo jeito que o lead pode ficar sem etapa.
        Guid? responsavelId;
        if (request.ResponsavelId.HasValue)
        {
            if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId.Value, ct))
            {
                throw new CrmForbiddenException("Você não pode atribuir leads para este vendedor.");
            }
            await ResponsavelAtivo.GarantirAsync(db, request.ResponsavelId.Value, ct);
            responsavelId = request.ResponsavelId.Value;
        }
        else if (currentUser.IsInRole(Roles.Comercial))
        {
            responsavelId = currentUser.UserId;
        }
        else
        {
            responsavelId = await assignment.ProximoResponsavelAsync(request.ProdutoInteresse, request.Regional, ct);
        }

        // Leads automáticos (Meta Ads, site) ficam de propósito sem etapa — "ninguém pegou
        // ainda" — mas um lead cadastrado manualmente já é trabalhado por quem o cadastrou, então
        // entra direto na primeira etapa ativa do funil em vez de cair na coluna "Sem etapa".
        var etapaId = request.EtapaId ?? await ObterEtapaInicialIdAsync(ct);

        var lead = new CrmLead
        {
            EtapaId = etapaId,
            NomeOuRazaoSocial = request.NomeOuRazaoSocial.Trim(),
            TipoPessoa = request.TipoPessoa,
            DocumentoNormalizado = documentoNormalizado,
            Telefone = request.Telefone,
            TelefoneNormalizado = telefoneNormalizado,
            WhatsApp = request.WhatsApp,
            Email = request.Email?.Trim(),
            EmailNormalizado = emailNormalizado,
            DataNascimento = request.DataNascimento,
            Cidade = request.Cidade,
            Estado = request.Estado?.ToUpperInvariant(),
            Regional = request.Regional,
            Origem = request.Origem,
            Campanha = request.Campanha,
            ProdutoInteresse = request.ProdutoInteresse,
            Placa = request.Placa?.Trim().ToUpperInvariant() is { Length: > 0 and <= 10 } placaValida ? placaValida : null,
            TemSeguro = request.TemSeguro,
            UtilidadeVeiculo = request.UtilidadeVeiculo,
            VeiculoNaoAtendido = LimparVeiculoNaoAtendido(request.VeiculoNaoAtendido),
            Gclid = request.Gclid,
            UtmMedium = request.UtmMedium,
            UtmSource = request.UtmSource,
            UtmCampaign = request.UtmCampaign,
            UtmTerm = request.UtmTerm,
            MetaClickId = request.MetaClickId,
            MetaFormId = request.MetaFormId,
            MetaLeadId = request.MetaLeadId,
            IndicadoPorLeadId = request.IndicadoPorLeadId,
            TipoIndicacao = request.TipoIndicacao,
            ResponsavelId = responsavelId,
            Observacoes = request.Observacoes,
            ConsentimentoContato = request.ConsentimentoContato,
            ConsentimentoDataEm = request.ConsentimentoContato ? DateTimeOffset.UtcNow : null,
            ConsentimentoOrigem = request.ConsentimentoOrigem
        };

        await AplicarTagsAsync(lead, request.Tags, ct);

        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync(ct);

        await audit.RegistrarAsync("LeadCriado", nameof(CrmLead), lead.Id, new { lead.NomeOuRazaoSocial }, ct);
        eventos?.PublicarQuadroAtualizado("crm");

        return new CriarLeadResultado(await ObterPorIdAsync(lead.Id, ct), null);
    }

    private async Task<Guid?> ObterEtapaInicialIdAsync(CancellationToken ct) =>
        await db.CrmLeadStages.AsNoTracking()
            .Where(s => s.Ativa)
            .OrderBy(s => s.Ordem)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<LeadDetailDto> AtualizarAsync(Guid id, LeadUpdateRequest request, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(id, ct);

        db.Entry(lead).Property(l => l.RowVersion).OriginalValue = request.RowVersion;

        var documentoNormalizado = DocumentValidation.NormalizarDocumento(request.Documento, out var documentoValido);
        if (!string.IsNullOrEmpty(documentoNormalizado) && !documentoValido)
        {
            throw new CrmBusinessException("CPF/CNPJ informado é inválido.", "documento_invalido");
        }

        var emailNormalizado = DocumentValidation.NormalizarEmail(request.Email);
        if (!string.IsNullOrWhiteSpace(request.Email) && emailNormalizado is null)
        {
            throw new CrmBusinessException("E-mail informado é inválido.", "email_invalido");
        }

        if (documentoNormalizado != lead.DocumentoNormalizado || emailNormalizado != lead.EmailNormalizado)
        {
            var duplicidade = await DetectarDuplicidadeAsync(
                documentoNormalizado == lead.DocumentoNormalizado ? null : documentoNormalizado,
                emailNormalizado == lead.EmailNormalizado ? null : emailNormalizado,
                null, true, ct, ignorarLeadId: lead.VeiculoAdicionalDeLeadId ?? lead.Id);
            if (duplicidade is not null)
            {
                throw new CrmBusinessException(
                    $"Já existe outro lead com o mesmo {duplicidade.CampoDuplicado} ({duplicidade.NomeExistente}).",
                    "duplicidade", duplicidade);
            }
        }

        lead.NomeOuRazaoSocial = request.NomeOuRazaoSocial.Trim();
        lead.TipoPessoa = request.TipoPessoa;
        lead.DocumentoNormalizado = documentoNormalizado;
        lead.Telefone = request.Telefone;
        lead.TelefoneNormalizado = DocumentValidation.NormalizarTelefone(request.Telefone);
        lead.WhatsApp = request.WhatsApp;
        lead.Email = request.Email?.Trim();
        lead.EmailNormalizado = emailNormalizado;
        lead.DataNascimento = request.DataNascimento;
        lead.Cidade = request.Cidade;
        lead.Estado = request.Estado?.ToUpperInvariant();
        lead.Regional = request.Regional;
        if (PodeVerOrigem) lead.Origem = request.Origem;
        lead.Campanha = request.Campanha;
        lead.ProdutoInteresse = request.ProdutoInteresse;
        lead.Placa = request.Placa?.Trim().ToUpperInvariant() is { Length: > 0 and <= 10 } placaValida ? placaValida : null;
        lead.TemSeguro = request.TemSeguro;
        lead.UtilidadeVeiculo = request.UtilidadeVeiculo;
        // Nulo = o chamador não manda o campo (não mexe); vazio = limpa.
        if (request.VeiculoNaoAtendido is not null) lead.VeiculoNaoAtendido = LimparVeiculoNaoAtendido(request.VeiculoNaoAtendido);
        lead.Gclid = request.Gclid;
        lead.UtmMedium = request.UtmMedium;
        lead.UtmSource = request.UtmSource;
        lead.UtmCampaign = request.UtmCampaign;
        lead.UtmTerm = request.UtmTerm;
        lead.MetaClickId = request.MetaClickId;
        lead.MetaFormId = request.MetaFormId;
        lead.MetaLeadId = request.MetaLeadId;
        lead.IndicadoPorLeadId = request.IndicadoPorLeadId;
        lead.TipoIndicacao = request.TipoIndicacao;
        lead.Observacoes = request.Observacoes;
        lead.ConsentimentoContato = request.ConsentimentoContato;
        lead.ConsentimentoOrigem = request.ConsentimentoOrigem;
        if (request.ConsentimentoContato && lead.ConsentimentoDataEm is null)
        {
            lead.ConsentimentoDataEm = DateTimeOffset.UtcNow;
        }

        await AplicarTagsAsync(lead, request.Tags, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await audit.RegistrarAsync("LeadAtualizado", nameof(CrmLead), lead.Id, null, ct);
        eventos?.PublicarQuadroAtualizado("crm");

        return await ObterPorIdAsync(lead.Id, ct);
    }

    public async Task<IReadOnlyList<Guid>> CriarVeiculosAdicionaisAsync(Guid id, LeadVeiculosAdicionaisRequest request, CancellationToken ct)
    {
        if (request.Quantidade is < 1 or > MaximoVeiculosAdicionais)
        {
            throw new CrmBusinessException($"Informe de 1 a {MaximoVeiculosAdicionais} veículos a mais.", "quantidade_invalida");
        }

        var original = await CarregarSemEscopoAsync(id, ct);
        // Cliente que já é atendido por OUTRO consultor (a duplicidade de CPF/e-mail vale para todo o sistema): quem fechou
        // o outro veículo registra a venda no próprio nome. O card novo é dele e só leva o nome (que o aviso de duplicidade já
        // mostra); CPF, telefone, e-mail e demais dados ficam no card do outro consultor — o CPF da venda é o que quem
        // registra digita no formulário.
        var deOutroConsultor = !await equipe.PodeAcessarVendedorAsync(original.ResponsavelId ?? Guid.Empty, ct);
        if (deOutroConsultor) await ResponsavelAtivo.GarantirAsync(db, currentUser.UserId, ct);
        if (request.EtapaId is { } etapaInformada && !await db.CrmLeadStages.AnyAsync(s => s.Id == etapaInformada, ct))
        {
            throw new CrmNotFoundException("Etapa de lead", etapaInformada);
        }
        // Para a coluna Leads/Indicação, o card novo é cadastro manual de quem registra (não herda a etiqueta do outro).
        var referenciaDeEtapa = deOutroConsultor ? new CrmLead { CriadoManualmente = true } : original;
        var etapaId = request.VendaConcluida
            ? await EtapaVendaConcluidaAsync(referenciaDeEtapa, ct)
            : request.EtapaId ?? (deOutroConsultor ? await ObterEtapaInicialIdAsync(ct) : await EtapaDoVeiculoAdicionalAsync(original, ct));
        var regionalDoCriador = deOutroConsultor
            ? await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.Regional != null ? u.Regional.Nome : null).FirstOrDefaultAsync(ct)
            : null;

        // Sempre aponta para o primeiro card do cliente, mesmo quando criado a partir de outro adicional.
        var principal = original.VeiculoAdicionalDeLead ?? original;
        var jaExistentes = await db.CrmLeads.CountAsync(l => l.VeiculoAdicionalDeLeadId == principal.Id && !l.Arquivado, ct);

        var novos = new List<CrmLead>();
        for (var i = 0; i < request.Quantidade; i++)
        {
            // Dados do cliente (inclusive CPF/CNPJ e e-mail — um cliente pode ter vários veículos),
            // sem os de rastreio do anúncio (não é um lead novo do tráfego: não conta nos limites nem
            // nos números de tráfego pago) e sem a placa, que é do outro veículo.
            var novo = deOutroConsultor ? new CrmLead
            {
                EtapaId = etapaId,
                NomeOuRazaoSocial = principal.NomeOuRazaoSocial,
                TipoPessoa = principal.TipoPessoa,
                Regional = regionalDoCriador,
                CriadoManualmente = true,
                ResponsavelId = currentUser.UserId,
                ResponsavelAtribuidoEm = DateTimeOffset.UtcNow,
                VeiculoAdicionalDeLeadId = principal.Id,
                Observacoes = $"Outro veículo de cliente já atendido por outro consultor (card original criado em {principal.CriadoEm.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy}).",
            } : new CrmLead
            {
                EtapaId = etapaId,
                NomeOuRazaoSocial = principal.NomeOuRazaoSocial,
                TipoPessoa = principal.TipoPessoa,
                DocumentoNormalizado = principal.DocumentoNormalizado,
                Email = principal.Email,
                EmailNormalizado = principal.EmailNormalizado,
                Telefone = principal.Telefone,
                TelefoneNormalizado = principal.TelefoneNormalizado,
                Telefone2 = principal.Telefone2,
                Telefone2Normalizado = principal.Telefone2Normalizado,
                WhatsApp = principal.WhatsApp,
                DataNascimento = principal.DataNascimento,
                Cidade = principal.Cidade,
                Estado = principal.Estado,
                Regional = principal.Regional,
                Origem = principal.Origem,
                ProdutoInteresse = original.ProdutoInteresse,
                TipoIndicacao = original.TipoIndicacao,
                IndicadoPorLeadId = principal.IndicadoPorLeadId,
                CriadoManualmente = original.CriadoManualmente,
                ResponsavelId = original.ResponsavelId,
                ResponsavelAtribuidoEm = DateTimeOffset.UtcNow,
                ConsentimentoContato = principal.ConsentimentoContato,
                ConsentimentoDataEm = principal.ConsentimentoDataEm,
                VeiculoAdicionalDeLeadId = principal.Id,
                Observacoes = $"Veículo adicional nº {jaExistentes + i + 2} do cliente (card original criado em {principal.CriadoEm.ToOffset(TimeSpan.FromHours(-3)):dd/MM/yyyy}).",
            };
            if (!deOutroConsultor) foreach (var lt in original.LeadTags) novo.LeadTags.Add(new CrmLeadTag { TagId = lt.TagId });
            novos.Add(novo);
        }

        db.CrmLeads.AddRange(novos);
        await db.SaveChangesAsync(ct);

        foreach (var novo in novos)
        {
            await audit.RegistrarAsync("LeadVeiculoAdicionalCriado", nameof(CrmLead), novo.Id, new { LeadOriginalId = principal.Id, DeOutroConsultor = deOutroConsultor, ResponsavelOriginalId = original.ResponsavelId }, ct);
        }
        eventos?.PublicarQuadroAtualizado("crm");

        return novos.Select(n => n.Id).ToList();
    }

    /// <summary>
    /// Coluna do card do veículo adicional quando o chamador não informa: a mesma do card original,
    /// ou — se o original já fechou (Venda concluída, Perdido...) — "Em atendimento" da mesma
    /// etiqueta (Leads/Indicação), porque a venda deste veículo ainda precisa ser preenchida.
    /// </summary>
    private async Task<Guid?> EtapaDoVeiculoAdicionalAsync(CrmLead original, CancellationToken ct)
    {
        if (original.Etapa is { Fechada: false } etapaAberta) return etapaAberta.Id;
        return await ColunaDaEtiquetaAsync(original, "EM ATENDIMENTO", ct) ?? await ObterEtapaInicialIdAsync(ct);
    }

    /// <summary>"Venda concluída (Leads)" ou "(Indicação)", pela etiqueta do cliente.</summary>
    private async Task<Guid?> EtapaVendaConcluidaAsync(CrmLead original, CancellationToken ct) =>
        await ColunaDaEtiquetaAsync(original, "VENDA CONCLUIDA", ct)
        ?? throw new CrmBusinessException("A coluna \"Venda concluída\" não está ativa no quadro de leads.", "etapa_venda_inexistente");

    /// <summary>Coluna Leads/Indicação do status (mesma regra do quadro e do Notion) para a etiqueta do card.</summary>
    private async Task<Guid?> ColunaDaEtiquetaAsync(CrmLead lead, string status, CancellationToken ct)
    {
        var etapasAtivas = await db.CrmLeadStages.AsNoTracking().Where(e => e.Ativa)
            .Select(e => new { e.Id, e.Nome }).ToListAsync(ct);
        var porNome = etapasAtivas.GroupBy(e => e.Nome).ToDictionary(g => g.Key, g => g.First().Id);
        var ehIndicacao = NotionEtapaLead.EhIndicacao(lead.CriadoManualmente, lead.TipoIndicacao);
        return NotionEtapaLead.Resolver(status, ehIndicacao, porNome).EtapaId;
    }

    public async Task AtribuirAsync(Guid id, LeadAssignRequest request, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem redistribuir leads.");
        }

        var lead = await CarregarComEscopoAsync(id, ct);

        if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId, ct))
        {
            throw new CrmForbiddenException("Você não pode atribuir leads para este vendedor.");
        }
        await ResponsavelAtivo.GarantirAsync(db, request.ResponsavelId, ct);

        var anteriorId = lead.ResponsavelId;
        lead.ResponsavelId = request.ResponsavelId;

        db.CrmLeadAssignmentHistories.Add(new CrmLeadAssignmentHistory
        {
            LeadId = lead.Id,
            ResponsavelAnteriorId = anteriorId,
            ResponsavelNovoId = request.ResponsavelId,
            AlteradoPorId = currentUser.UserId,
            Motivo = request.Motivo
        });

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("LeadAtribuido", nameof(CrmLead), lead.Id, new { request.ResponsavelId }, ct);
        eventos?.PublicarQuadroAtualizado("crm");
    }

    public async Task<LeadDetailDto> MudarEtapaAsync(Guid id, ChangeLeadStageRequest request, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(id, ct);
        db.Entry(lead).Property(l => l.RowVersion).OriginalValue = request.RowVersion;

        CrmLeadStage? novaEtapa = null;
        if (request.NovaEtapaId.HasValue)
        {
            novaEtapa = await db.CrmLeadStages.FirstOrDefaultAsync(s => s.Id == request.NovaEtapaId, ct)
                ?? throw new CrmNotFoundException("Etapa de lead", request.NovaEtapaId.Value);
            lead.EtapaId = novaEtapa.Id;

            // Lead (tráfego) que fechou como indicação: vai pra coluna das indicações com a
            // etiqueta própria "Indicação Lead", sem perder que começou como lead.
            if (novaEtapa.Nome == EtapaLeadVendaConcluidaIndicacao && TipoIndicacaoLead.EhLead(lead.TipoIndicacao))
            {
                lead.TipoIndicacao = TipoIndicacaoLead.IndicacaoLead;
            }

            if (novaEtapa.Nome == EtapaLeadCotacao)
            {
                var valorAdesao = request.ValorAdesao ?? lead.ValorAdesao;
                if (valorAdesao is not > 0)
                {
                    throw new CrmBusinessException("Informe o valor da adesão para mover o lead para 'Cotação'.", "valor_adesao_obrigatorio");
                }
                lead.ValorAdesao = valorAdesao;
            }

            if (novaEtapa.Nome == EtapaLeadPerdido)
            {
                if (request.MotivoPerdaId is null)
                {
                    throw new CrmBusinessException("Informe o motivo da perda ao mover para 'Perdido'.", "motivo_perda_obrigatorio");
                }

                var motivo = await db.CrmLossReasons.FirstOrDefaultAsync(m => m.Id == request.MotivoPerdaId, ct)
                    ?? throw new CrmNotFoundException("Motivo de perda", request.MotivoPerdaId.Value);
                lead.MotivoPerdaId = motivo.Id;
                lead.MotivoPerdaObservacao = string.IsNullOrWhiteSpace(request.MotivoPerdaObservacao) ? null : request.MotivoPerdaObservacao.Trim();
                lead.VeiculoNaoAtendido = null;
            }
            else if (novaEtapa.Nome == EtapaLeadNaoFazemos)
            {
                if (string.IsNullOrWhiteSpace(request.VeiculoNaoAtendido))
                {
                    throw new CrmBusinessException("Informe o modelo do veículo ao mover para 'Não fazemos'.", "veiculo_nao_atendido_obrigatorio");
                }

                lead.VeiculoNaoAtendido = request.VeiculoNaoAtendido.Trim();
                lead.MotivoPerdaId = null;
                lead.MotivoPerdaObservacao = null;
            }
            else
            {
                lead.MotivoPerdaId = null;
                lead.MotivoPerdaObservacao = null;
                lead.VeiculoNaoAtendido = null;
            }
        }
        else
        {
            lead.EtapaId = null;
            lead.MotivoPerdaId = null;
            lead.MotivoPerdaObservacao = null;
            lead.VeiculoNaoAtendido = null;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await audit.RegistrarAsync("LeadMudouEtapa", nameof(CrmLead), lead.Id, new { EtapaNova = request.NovaEtapaId }, ct);
        eventos?.PublicarQuadroAtualizado("crm");

        // Retorno de conversão offline (CAPI): manda um evento pra toda mudança de etapa (nomeado
        // com a própria etapa) — qual delas vira otimização de campanha é escolhido no
        // Gerenciador de Anúncios, não aqui. Nunca deve bloquear a resposta desse endpoint.
        if (novaEtapa is not null)
        {
            var valorConversao = novaEtapa.Nome.StartsWith(EtapaLeadVendaConcluida, StringComparison.OrdinalIgnoreCase)
                ? EscolherValorConversao(await ObterPagamentoAdesaoAsync(lead.Id, ct), lead.ValorAdesao)
                : null;
            await conversion.EnviarEventoEtapaAsync(lead, novaEtapa.Id, novaEtapa.Nome, ct, valorConversao);
        }

        return await ObterPorIdAsync(lead.Id, ct);
    }

    /// <summary>
    /// Pagamento de adesão da oportunidade mais recente do lead (não arquivada) — o valor real que
    /// já entrou, usado como valor do evento de conversão "venda concluída" pro Meta. Um lead pode
    /// ter mais de uma oportunidade (raro); pega a mais recente por ser a mais provável de ser a
    /// que motivou essa mudança de etapa.
    /// </summary>
    /// <summary>
    /// Valor que vai pro pixel na venda concluída: o pagamento de adesão da oportunidade, quando existe;
    /// senão o valor da adesão do próprio lead (obrigatório na etapa Cotação). Antes só olhava a oportunidade,
    /// e quem fecha direto pelo quadro de leads não tem oportunidade — o evento saía sem valor.
    /// </summary>
    internal static decimal? EscolherValorConversao(decimal? pagamentoOportunidade, decimal? valorAdesaoLead)
    {
        if (pagamentoOportunidade is > 0) return pagamentoOportunidade;
        return valorAdesaoLead is > 0 ? valorAdesaoLead : null;
    }

    private async Task<decimal?> ObterPagamentoAdesaoAsync(Guid leadId, CancellationToken ct)
    {
        return await db.CrmOpportunities.AsNoTracking()
            .Where(o => o.LeadId == leadId && !o.Arquivado)
            .OrderByDescending(o => o.CriadoEm)
            .Select(o => o.PagamentoAdesao)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> AtribuirEmLoteAsync(LeadBulkAssignRequest request, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem redistribuir leads.");
        }

        if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId, ct))
        {
            throw new CrmForbiddenException("Você não pode atribuir leads para este vendedor.");
        }
        await ResponsavelAtivo.GarantirAsync(db, request.ResponsavelId, ct);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var query = db.CrmLeads.Where(l => request.LeadIds.Contains(l.Id) && !l.Arquivado);
        if (visiveis is not null)
        {
            query = query.Where(l => visiveis.Contains(l.ResponsavelId ?? Guid.Empty));
        }

        var leads = await query.ToListAsync(ct);
        var agora = DateTimeOffset.UtcNow;

        foreach (var lead in leads)
        {
            var anteriorId = lead.ResponsavelId;
            lead.ResponsavelId = request.ResponsavelId;
            db.CrmLeadAssignmentHistories.Add(new CrmLeadAssignmentHistory
            {
                LeadId = lead.Id,
                ResponsavelAnteriorId = anteriorId,
                ResponsavelNovoId = request.ResponsavelId,
                AlteradoPorId = currentUser.UserId,
                AlteradoEm = agora,
                Motivo = request.Motivo
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("LeadRedistribuicaoLote", nameof(CrmLead), null, new { Quantidade = leads.Count, request.ResponsavelId }, ct);
        eventos?.PublicarQuadroAtualizado("crm");

        return leads.Count;
    }

    public async Task<Guid> AdicionarNotaAsync(Guid leadId, string texto, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(leadId, ct);
        var nota = new CrmNote { LeadId = lead.Id, AutorId = currentUser.UserId, Texto = texto.Trim() };
        db.CrmNotes.Add(nota);
        await db.SaveChangesAsync(ct);
        return nota.Id;
    }

    /// <summary>
    /// Exclusão lógica (arquivamento) — nunca DELETE físico, ver CrmEntityBase. Só permitida pra
    /// leads com a etiqueta "Indicação" do quadro de leads (cadastro manual ou TipoIndicacao
    /// "Indicação" vindo do Notion) — mesma regra usada no rodapé do cartão no front-end.
    /// </summary>
    /// <summary>
    /// Exclusão de lead — só Admin/GestorMaster, qualquer lead. É um arquivamento (o histórico fica
    /// para auditoria): o lead some do quadro e da lista, e as oportunidades dele saem do Pipeline.
    /// Arquivar (e não apagar a linha) também é o que impede a sincronização com o Notion de trazer o
    /// lead de volta — card ligado a lead arquivado é ignorado (ver NotionSyncService).
    /// </summary>
    public async Task ExcluirAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.TemVisaoTotal)
        {
            throw new CrmForbiddenException("Apenas administradores podem excluir leads.");
        }

        var lead = await CarregarComEscopoAsync(id, ct);
        var agora = DateTimeOffset.UtcNow;

        lead.Arquivado = true;
        lead.ArquivadoEm = agora;
        lead.ArquivadoPorId = currentUser.UserId;

        var oportunidades = await db.CrmOpportunities.Where(o => o.LeadId == lead.Id && !o.Arquivado).ToListAsync(ct);
        foreach (var oportunidade in oportunidades)
        {
            oportunidade.Arquivado = true;
            oportunidade.ArquivadoEm = agora;
            oportunidade.ArquivadoPorId = currentUser.UserId;
        }

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("LeadExcluido", nameof(CrmLead), lead.Id,
            new { lead.NomeOuRazaoSocial, OportunidadesArquivadas = oportunidades.Count }, ct);
        eventos?.PublicarQuadroAtualizado("crm");
    }

    public async Task<LeadImportResultDto> ImportarAsync(Stream planilha, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem importar leads em lote.");
        }

        using var workbook = new XLWorkbook(planilha);
        var sheet = workbook.Worksheets.First();
        var linhas = sheet.RowsUsed().Skip(1).ToList();

        var erros = new List<string>();
        var importados = 0;
        var duplicados = 0;

        // Só consultores ativos: e-mail de consultor inativo na planilha cai na distribuição automática.
        var usuariosPorEmail = await db.Users.AsNoTracking()
            .Where(u => u.Ativo && u.Email != null)
            .ToDictionaryAsync(u => u.Email!.ToLowerInvariant(), u => u.Id, ct);
        var etapaInicialId = await ObterEtapaInicialIdAsync(ct);

        foreach (var linha in linhas)
        {
            var numeroLinha = linha.RowNumber();
            try
            {
                var nome = linha.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(nome))
                {
                    erros.Add($"Linha {numeroLinha}: nome é obrigatório.");
                    continue;
                }

                var tipoPessoaTexto = linha.Cell(2).GetString().Trim();
                var tipoPessoa = tipoPessoaTexto.Equals("juridica", StringComparison.OrdinalIgnoreCase) ||
                                  tipoPessoaTexto.Equals("jurídica", StringComparison.OrdinalIgnoreCase)
                    ? TipoPessoa.Juridica
                    : TipoPessoa.Fisica;

                var documento = linha.Cell(3).GetString().Trim();
                var telefone = linha.Cell(4).GetString().Trim();
                var email = linha.Cell(5).GetString().Trim();
                var cidade = linha.Cell(6).GetString().Trim();
                var estado = linha.Cell(7).GetString().Trim();
                var regional = linha.Cell(8).GetString().Trim();
                var origem = linha.Cell(9).GetString().Trim();
                var responsavelEmail = linha.Cell(10).GetString().Trim().ToLowerInvariant();

                var documentoNormalizado = DocumentValidation.NormalizarDocumento(documento, out var documentoValido);
                if (!string.IsNullOrEmpty(documentoNormalizado) && !documentoValido)
                {
                    erros.Add($"Linha {numeroLinha}: CPF/CNPJ inválido.");
                    continue;
                }

                var emailNormalizado = DocumentValidation.NormalizarEmail(email);
                var duplicidade = await DetectarDuplicidadeAsync(documentoNormalizado, emailNormalizado, null, false, ct);
                if (duplicidade is not null)
                {
                    duplicados++;
                    continue;
                }

                Guid? responsavelId = null;
                if (!string.IsNullOrWhiteSpace(responsavelEmail) && usuariosPorEmail.TryGetValue(responsavelEmail, out var uid))
                {
                    // Respeita o limite mensal do consultor da planilha; se já bateu, vai pro rodízio.
                    if (await equipe.PodeAcessarVendedorAsync(uid, ct) && await assignment.PodeReceberAsync(uid, ct)) responsavelId = uid;
                }
                responsavelId ??= await assignment.ProximoResponsavelAsync(null, ct);

                db.CrmLeads.Add(new CrmLead
                {
                    EtapaId = etapaInicialId,
                    NomeOuRazaoSocial = nome,
                    TipoPessoa = tipoPessoa,
                    DocumentoNormalizado = documentoNormalizado,
                    Telefone = telefone,
                    TelefoneNormalizado = DocumentValidation.NormalizarTelefone(telefone),
                    Email = email,
                    EmailNormalizado = emailNormalizado,
                    Cidade = cidade,
                    Estado = string.IsNullOrWhiteSpace(estado) ? null : estado.ToUpperInvariant(),
                    Regional = regional,
                    Origem = string.IsNullOrWhiteSpace(origem) ? "Importação" : origem,
                    ResponsavelId = responsavelId
                });

                importados++;
            }
            catch (Exception ex)
            {
                erros.Add($"Linha {numeroLinha}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("LeadsImportados", nameof(CrmLead), null, new { importados, duplicados, erros = erros.Count }, ct);
        eventos?.PublicarQuadroAtualizado("crm");

        return new LeadImportResultDto(linhas.Count, importados, duplicados, erros.Count, erros);
    }

    private static string? LimparVeiculoNaoAtendido(string? veiculo) =>
        string.IsNullOrWhiteSpace(veiculo) ? null : veiculo.Trim() is { Length: > 200 } longo ? longo[..200] : veiculo.Trim();

    public async Task<LeadTotaisDto> ObterTotaisAsync(LeadFilterRequest filtro, CancellationToken ct)
    {
        if (!PodeVerOrigem) filtro = filtro with { Origem = null, Origens = null };
        var query = await QueryEscopadaAsync(filtro.IncluirArquivados, ct);
        query = AplicarFiltros(query, filtro);

        var contagem = await query.CountAsync(ct);

        // Somas sobre as vendas (oportunidades não arquivadas) dos leads filtrados: cada venda é um card do Notion,
        // como na barra de somas da tela de relatórios de lá.
        var somas = await query
            .SelectMany(l => l.Oportunidades.Where(o => !o.Arquivado))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Adesao = g.Sum(o => o.PagamentoAdesao) ?? 0m,
                Fipe = g.Sum(o => o.Veiculo != null ? o.Veiculo.Fipe : null) ?? 0m,
                Mensalidade = g.Sum(o => o.Mensalidade) ?? 0m,
                MensalidadeComDesconto = g.Sum(o => o.MensalidadeComDesconto) ?? 0m,
                Rastreador = g.Sum(o => o.Veiculo != null ? o.Veiculo.Rastreador : null) ?? 0m,
                Indicacao = g.Sum(o => o.ValorIndicacao) ?? 0m,
                Vistoria = g.Sum(o => o.Veiculo != null ? o.Veiculo.ValorVistoria : null) ?? 0m,
                Total = g.Sum(o => o.Total ?? o.ValorFinal) ?? 0m,
                MediaPorcentagem = g.Average(o => o.Porcentagem) ?? 0m,
            })
            .FirstOrDefaultAsync(ct);

        return somas is null
            ? new LeadTotaisDto(contagem, 0, 0, 0, 0, 0, 0, 0, 0)
            : new LeadTotaisDto(contagem, somas.Adesao, somas.Fipe, somas.Mensalidade, somas.MensalidadeComDesconto,
                somas.Rastreador, somas.Indicacao, somas.Vistoria, somas.Total, Math.Round(somas.MediaPorcentagem, 2));
    }

    public async Task<byte[]> ExportarAsync(LeadFilterRequest filtro, CancellationToken ct)
    {
        if (!PodeVerOrigem) filtro = filtro with { Origem = null };
        var podeVerOrigem = PodeVerOrigem;
        var query = await QueryEscopadaAsync(filtro.IncluirArquivados, ct);
        query = AplicarFiltros(query, filtro);

        var leads = await query
            .Select(l => new
            {
                l.NomeOuRazaoSocial,
                l.TipoPessoa,
                l.DocumentoNormalizado,
                l.Telefone,
                l.Telefone2,
                l.Email,
                l.Cidade,
                l.Estado,
                l.Regional,
                Origem = podeVerOrigem ? l.Origem : null,
                EtapaNome = l.Etapa != null ? l.Etapa.Nome : "Sem etapa",
                Responsavel = l.Responsavel != null ? l.Responsavel.NomeCompleto : null,
                l.CriadoEm
            })
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Leads");
        string[] cabecalho = ["Nome/Razão Social", "Tipo", "CPF/CNPJ", "Telefone", "E-mail", "Cidade", "UF", "Regional", "Origem", "Etapa", "Responsável", "Criado em"];
        for (var i = 0; i < cabecalho.Length; i++) sheet.Cell(1, i + 1).Value = cabecalho[i];

        var linha = 2;
        foreach (var l in leads)
        {
            sheet.Cell(linha, 1).Value = l.NomeOuRazaoSocial;
            sheet.Cell(linha, 2).Value = l.TipoPessoa == TipoPessoa.Fisica ? "Física" : "Jurídica";
            sheet.Cell(linha, 3).Value = DocumentValidation.FormatarDocumento(l.DocumentoNormalizado);
            sheet.Cell(linha, 4).Value = l.Telefone;
            sheet.Cell(linha, 5).Value = l.Email;
            sheet.Cell(linha, 6).Value = l.Cidade;
            sheet.Cell(linha, 7).Value = l.Estado;
            sheet.Cell(linha, 8).Value = l.Regional;
            sheet.Cell(linha, 9).Value = l.Origem;
            sheet.Cell(linha, 10).Value = l.EtapaNome;
            sheet.Cell(linha, 11).Value = l.Responsavel;
            sheet.Cell(linha, 12).Value = l.CriadoEm.ToString("dd/MM/yyyy HH:mm");
            linha++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // --- Métodos auxiliares privados ---

    private async Task<IQueryable<CrmLead>> QueryEscopadaAsync(bool incluirArquivados, CancellationToken ct)
    {
        var query = db.CrmLeads.AsNoTracking().AsQueryable();
        if (!incluirArquivados) query = query.Where(l => !l.Arquivado);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null)
        {
            query = query.Where(l => visiveis.Contains(l.ResponsavelId ?? Guid.Empty));
        }

        return query;
    }

    /// <summary>Lead sem checar de quem é a carteira (quem chama decide o que fazer com o dado — ver CriarVeiculosAdicionaisAsync).</summary>
    private async Task<CrmLead> CarregarSemEscopoAsync(Guid id, CancellationToken ct) =>
        await db.CrmLeads
            .Include(l => l.LeadTags).ThenInclude(lt => lt.Tag)
            .Include(l => l.VeiculoAdicionalDeLead)
            .Include(l => l.Etapa)
            .FirstOrDefaultAsync(l => l.Id == id && !l.Arquivado, ct)
            ?? throw new CrmNotFoundException("Lead", id);

    private async Task<CrmLead> CarregarComEscopoAsync(Guid id, CancellationToken ct)
    {
        var lead = await db.CrmLeads
            .Include(l => l.LeadTags).ThenInclude(lt => lt.Tag)
            .Include(l => l.Responsavel)
            .Include(l => l.IndicadoPorLead)
            .Include(l => l.VeiculoAdicionalDeLead)
            .Include(l => l.Etapa)
            .Include(l => l.MotivoPerda)
            .Include(l => l.Oportunidades).ThenInclude(o => o.Etapa)
            .Include(l => l.Oportunidades).ThenInclude(o => o.Veiculo)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new CrmNotFoundException("Lead", id);

        if (!await equipe.PodeAcessarVendedorAsync(lead.ResponsavelId ?? Guid.Empty, ct))
        {
            throw new CrmForbiddenException();
        }

        return lead;
    }

    private IQueryable<CrmLead> AplicarFiltros(IQueryable<CrmLead> query, LeadFilterRequest filtro)
    {
        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();
            var buscaDigitos = DocumentValidation.SomenteDigitos(busca);
            query = query.Where(l =>
                EF.Functions.ILike(l.NomeOuRazaoSocial, $"%{busca}%") ||
                (l.Email != null && EF.Functions.ILike(l.Email, $"%{busca}%")) ||
                (buscaDigitos != "" && l.DocumentoNormalizado != null && l.DocumentoNormalizado.Contains(buscaDigitos)) ||
                (buscaDigitos != "" && l.TelefoneNormalizado != null && l.TelefoneNormalizado.Contains(buscaDigitos)));
        }

        if (filtro.ResponsavelId.HasValue) query = query.Where(l => l.ResponsavelId == filtro.ResponsavelId);
        if (!string.IsNullOrWhiteSpace(filtro.Regional)) query = FiltroDeRegional.Aplicar(query, db.CrmRegionais.AsNoTracking(), db.CrmGrupos.AsNoTracking(), [filtro.Regional], distribuicao?.Value.RegionaisExclusivas);
        if (!string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(l => l.Origem == filtro.Origem);
        if (filtro.LeadEtapaId.HasValue) query = query.Where(l => l.EtapaId == filtro.LeadEtapaId);

        if (filtro.ResponsavelIds is { Length: > 0 })
        {
            var ids = filtro.ResponsavelIds.Select(id => (Guid?)id).ToList();
            query = query.Where(l => ids.Contains(l.ResponsavelId));
        }
        if (filtro.LeadEtapaIds is { Length: > 0 })
        {
            var semEtapa = filtro.LeadEtapaIds.Contains(Guid.Empty);
            var etapas = filtro.LeadEtapaIds.Where(id => id != Guid.Empty).Select(id => (Guid?)id).ToList();
            query = query.Where(l => (l.EtapaId != null && etapas.Contains(l.EtapaId)) || (semEtapa && l.EtapaId == null));
        }
        if (filtro.Origens is { Length: > 0 })
        {
            var origens = filtro.Origens.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();
            query = query.Where(l => origens.Contains(l.Origem!));
        }
        if (filtro.Regionais is { Length: > 0 })
        {
            query = FiltroDeRegional.Aplicar(query, db.CrmRegionais.AsNoTracking(), db.CrmGrupos.AsNoTracking(), filtro.Regionais.Where(r => !string.IsNullOrWhiteSpace(r)), distribuicao?.Value.RegionaisExclusivas);
        }
        if (filtro.GrupoIds is { Length: > 0 })
        {
            var grupos = filtro.GrupoIds.Select(id => (Guid?)id).ToList();
            query = query.Where(l => l.Responsavel != null && grupos.Contains(l.Responsavel.GrupoId));
        }
        if (filtro.DataVendaInicio.HasValue || filtro.DataVendaFim.HasValue)
        {
            // Pela data de ativação da venda (sem ela, a data da venda) e só venda ganha (a perda também grava data de fechamento).
            var per = PeriodoDeAtivacao.De(filtro.DataVendaInicio, filtro.DataVendaFim);
            query = query.Where(l => l.Oportunidades.Any(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho
                && ((o.AtivoEm != null && o.AtivoEm >= per.AtivacaoDe && o.AtivoEm < per.AtivacaoAte)
                    || (o.AtivoEm == null && o.DataEfetivaFechamento >= per.VendaDe && o.DataEfetivaFechamento <= per.VendaAte))));
        }
        if (filtro.EtapaId.HasValue) query = query.Where(l => l.Oportunidades.Any(o => o.EtapaId == filtro.EtapaId && !o.Arquivado));
        if (filtro.DataInicio.HasValue)
        {
            var inicio = filtro.DataInicio.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(l => l.CriadoEm >= inicio);
        }
        if (filtro.DataFim.HasValue)
        {
            var fim = filtro.DataFim.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(l => l.CriadoEm <= fim);
        }
        if (filtro.Tags is { Count: > 0 })
        {
            query = query.Where(l => l.LeadTags.Any(lt => filtro.Tags.Contains(lt.Tag.Nome)));
        }

        return query;
    }

    private static IQueryable<CrmLead> AplicarOrdenacao(IQueryable<CrmLead> query, string? ordenarPor, bool desc)
    {
        Expression<Func<CrmLead, object?>> chave = ordenarPor?.ToLowerInvariant() switch
        {
            "nome" => l => l.NomeOuRazaoSocial,
            "etapa" => l => l.Etapa != null ? l.Etapa.Nome : null,
            "ultimocontato" => l => l.UltimoContatoEm,
            "proximocontato" => l => l.ProximoContatoEm,
            _ => l => l.CriadoEm
        };

        return desc ? query.OrderByDescending(chave) : query.OrderBy(chave);
    }

    /// <param name="ignorarLeadId">
    /// Card principal do cliente que está sendo editado: ele e os veículos adicionais dele não contam
    /// como duplicata (todos têm o mesmo CPF/e-mail).
    /// </param>
    private async Task<LeadDuplicateWarningDto?> DetectarDuplicidadeAsync(
        string? documentoNormalizado, string? emailNormalizado, string? telefoneNormalizado, bool ignorarDuplicidade,
        CancellationToken ct, Guid? ignorarLeadId = null)
    {
        var outros = db.CrmLeads.AsNoTracking()
            .Where(l => !l.Arquivado && l.Id != ignorarLeadId && (ignorarLeadId == null || l.VeiculoAdicionalDeLeadId != ignorarLeadId))
            // O aviso aponta para o card principal do cliente (é nele que se lança "Outro veículo").
            .OrderBy(l => l.VeiculoAdicionalDeLeadId != null);

        if (!string.IsNullOrEmpty(documentoNormalizado))
        {
            var existente = await outros
                .Where(l => l.DocumentoNormalizado == documentoNormalizado)
                .Select(l => new { Id = l.VeiculoAdicionalDeLeadId ?? l.Id, l.NomeOuRazaoSocial })
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                return new LeadDuplicateWarningDto(existente.Id, existente.NomeOuRazaoSocial, "CPF/CNPJ");
            }
        }

        if (!string.IsNullOrEmpty(emailNormalizado))
        {
            var existente = await outros
                .Where(l => l.EmailNormalizado == emailNormalizado)
                .Select(l => new { Id = l.VeiculoAdicionalDeLeadId ?? l.Id, l.NomeOuRazaoSocial })
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                return new LeadDuplicateWarningDto(existente.Id, existente.NomeOuRazaoSocial, "e-mail");
            }
        }

        if (!ignorarDuplicidade && !string.IsNullOrEmpty(telefoneNormalizado))
        {
            var existente = await outros
                .Where(l => l.TelefoneNormalizado == telefoneNormalizado)
                .Select(l => new { Id = l.VeiculoAdicionalDeLeadId ?? l.Id, l.NomeOuRazaoSocial })
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                return new LeadDuplicateWarningDto(existente.Id, existente.NomeOuRazaoSocial, "telefone");
            }
        }

        return null;
    }

    private async Task AplicarTagsAsync(CrmLead lead, List<string>? tags, CancellationToken ct)
    {
        if (tags is null) return;

        var nomes = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existentes = await db.CrmTags.Where(t => nomes.Contains(t.Nome)).ToListAsync(ct);
        var faltantes = nomes.Where(n => !existentes.Any(e => e.Nome.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
        var novas = faltantes.Select(n => new CrmTag { Nome = n }).ToList();
        if (novas.Count > 0)
        {
            db.CrmTags.AddRange(novas);
            existentes.AddRange(novas);
        }

        lead.LeadTags.Clear();
        foreach (var tag in existentes)
        {
            lead.LeadTags.Add(new CrmLeadTag { LeadId = lead.Id, TagId = tag.Id });
        }
    }

    private static LeadDetailDto ParaDetailDto(CrmLead lead, bool podeVerOrigem) => new(
        lead.Id,
        lead.NomeOuRazaoSocial,
        lead.TipoPessoa,
        DocumentValidation.FormatarDocumento(lead.DocumentoNormalizado),
        lead.Telefone,
        lead.Telefone2,
        lead.WhatsApp,
        lead.Email,
        lead.DataNascimento,
        lead.Cidade,
        lead.Estado,
        lead.Regional,
        podeVerOrigem ? lead.Origem : null,
        lead.Campanha,
        lead.ProdutoInteresse,
        lead.Placa,
        lead.TemSeguro,
        lead.UtilidadeVeiculo,
        lead.Gclid,
        lead.UtmMedium,
        lead.UtmSource,
        lead.UtmTerm,
        lead.MetaClickId,
        lead.MetaFormId,
        lead.MetaLeadId,
        lead.IndicadoPorLeadId,
        lead.IndicadoPorLead?.NomeOuRazaoSocial,
        lead.TipoIndicacao,
        lead.CriadoManualmente,
        lead.EtapaId,
        lead.Etapa?.Nome,
        lead.Etapa?.Cor,
        lead.MotivoPerdaId,
        lead.MotivoPerda?.Descricao,
        lead.MotivoPerdaObservacao,
        lead.VeiculoNaoAtendido,
        lead.ResponsavelId,
        lead.Responsavel?.NomeCompleto,
        lead.Observacoes,
        lead.ConsentimentoContato,
        lead.ConsentimentoDataEm,
        lead.ConsentimentoOrigem,
        lead.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
        lead.Oportunidades
            .Where(o => !o.Arquivado)
            .OrderByDescending(o => o.CriadoEm)
            .Select(o => new LeadOpportunitySummaryDto(
                o.Id, o.Titulo, o.Etapa.Nome, o.Etapa.Tipo, o.ValorEstimado, o.DataPrevistaFechamento, o.Etapa.Tipo == TipoEtapaPipeline.Aberta, o.Migracao, o.Indicacao,
                o.Cpf, o.Estado, o.AtivoEm, o.Porcentagem, o.Mensalidade, o.MensalidadeComDesconto, o.MensalidadeComCupom, o.PagamentoAdesao, o.Total,
                o.TipoIndicacao, o.ValorIndicacao,
                o.Veiculo == null ? null : new LeadOpportunityVeiculoSummaryDto(o.Veiculo.Descricao, o.Veiculo.Placa, o.Veiculo.Fipe, o.Veiculo.Rastreador, o.Veiculo.ValorVistoria, o.Veiculo.DataChegada, o.Veiculo.Chassi),
                o.TermoAdesaoArquivoUrl, o.PagamentoAdesaoArquivoUrl, o.ComprovanteIndicacaoArquivoUrl, o.ComprovanteVistoriaArquivoUrl,
                o.DataPagamentoAdesaoPrevista, o.ResponsavelId))
            .ToList(),
        lead.CriadoEm,
        lead.AtualizadoEm,
        lead.RowVersion,
        lead.Arquivado,
        lead.ValorAdesao,
        lead.UtmCampaign,
        lead.VeiculoAdicionalDeLeadId,
        lead.VeiculoAdicionalDeLead?.NomeOuRazaoSocial,
        DocumentValidation.FormatarDocumento(lead.VeiculoAdicionalDeLead?.DocumentoNormalizado));
}
