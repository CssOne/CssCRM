using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Marketing;
using CssVision.Web.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class OpportunityService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEquipeComercialService equipe,
    IMetaConversionService conversion,
    IAuditSink audit,
    IFileStorageService armazenamento,
    ICrmEventHub? eventos = null,
    IVendaPublicador? publicador = null) : IOpportunityService
{
    private static readonly string[] ExtensoesAnexoPermitidas = [".pdf", ".jpg", ".jpeg", ".png", ".webp"];
    private static readonly HashSet<string> TiposAnexoValidos = new(StringComparer.OrdinalIgnoreCase)
        { "termo-adesao", "pagamento-adesao", "comprovante-indicacao", "comprovante-vistoria" };

    public async Task<PagedResult<OpportunityDto>> ListarAsync(OpportunityFilterRequest filtro, CancellationToken ct)
    {
        var query = await QueryEscopadaAsync(ct);

        if (filtro.ResponsavelId.HasValue) query = query.Where(o => o.ResponsavelId == filtro.ResponsavelId);
        if (filtro.EtapaId.HasValue) query = query.Where(o => o.EtapaId == filtro.EtapaId);
        if (!string.IsNullOrWhiteSpace(filtro.ProdutoOuServico)) query = query.Where(o => o.ProdutoOuServico == filtro.ProdutoOuServico);
        // Origem é informação só de administrador (visão total): o consultor não filtra por ela (daria para deduzi-la).
        if (currentUser.TemVisaoTotal && !string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(o => o.Lead.Origem == filtro.Origem);
        if (!string.IsNullOrWhiteSpace(filtro.Regional)) query = query.Where(o => o.Lead.Regional == filtro.Regional);
        if (filtro.StatusEtapa.HasValue) query = query.Where(o => o.Etapa.Tipo == filtro.StatusEtapa);
        if (filtro.DataInicio.HasValue)
        {
            var inicio = filtro.DataInicio.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(o => o.CriadoEm >= inicio);
        }
        if (filtro.DataFim.HasValue)
        {
            var fim = filtro.DataFim.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(o => o.CriadoEm <= fim);
        }

        var total = await query.CountAsync(ct);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        var pagina = await query
            .Include(o => o.Lead)
            .Include(o => o.Etapa)
            .Include(o => o.Responsavel)
            .Include(o => o.MotivoPerda)
            .Include(o => o.Veiculo).ThenInclude(v => v!.Vistoriador)
            .OrderByDescending(o => o.CriadoEm)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .ToListAsync(ct);

        var itens = pagina.Select(o => ParaDto(o, hoje)).ToList();

        return new PagedResult<OpportunityDto>
        {
            Itens = itens,
            Pagina = filtro.Pagina,
            TamanhoPagina = filtro.TamanhoPagina,
            TotalRegistros = total
        };
    }

    public async Task<OpportunityDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var o = await CarregarComEscopoAsync(id, ct);
        return ParaDto(o, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private const decimal LimiteDinheiro = 999_999_999_999.99m; // numeric(14,2)

    /// <summary>
    /// Confere, antes de gravar, o que as colunas do banco não aguentam: texto maior que a coluna (placa 10, chassi 30, descrição 200, CPF 14, UF 2, canal 80)
    /// e número fora do limite (valores numeric(14,2), porcentagem de 0 a 100). Sem isto o banco recusava ("value too long"/"numeric overflow") e o consultor
    /// via só "Erro inesperado (500)" ao concluir a venda, mesmo com tudo preenchido (ex.: placa digitada com o modelo junto).
    /// </summary>
    internal static void ExigirCamposDaVendaDentroDoLimite(
        string? cpf, string? estado, string? tipoIndicacao, VeiculoUpsertRequest? veiculo, decimal? porcentagem,
        params (string Nome, decimal? Valor)[] valores)
    {
        static void Texto(string nome, string? texto, int maximo, string dica)
        {
            if (texto is { } t && t.Trim().Length > maximo)
            {
                throw new CrmBusinessException($"{nome} deve ter no máximo {maximo} caracteres (foram digitados {t.Trim().Length}). {dica}", "campo_muito_longo");
            }
        }

        Texto("A placa", veiculo?.Placa, 10, "Digite só a placa do veículo, sem o modelo ou outras informações.");
        Texto("O chassi", veiculo?.Chassi, 30, "Confira o número do chassi.");
        Texto("A descrição do veículo", veiculo?.Descricao, 200, "Use só marca e modelo.");
        Texto("O CPF/CNPJ", cpf, 14, "Digite só os números.");
        Texto("O estado", estado, 2, "Use a sigla do estado (UF).");
        Texto("O canal de aquisição", tipoIndicacao, 80, "Escolha uma das opções da lista.");

        if (porcentagem is { } p && (p < 0 || p > 100))
        {
            throw new CrmBusinessException("O cupom de desconto (porcentagem) deve ficar entre 0 e 100.", "porcentagem_invalida");
        }

        foreach (var (nome, valor) in valores)
        {
            if (valor is { } v && Math.Abs(v) > LimiteDinheiro)
            {
                throw new CrmBusinessException($"O valor de \"{nome}\" é grande demais. Confira se foi digitado corretamente.", "valor_invalido");
            }
        }
    }

    public async Task<OpportunityDto> CriarAsync(OpportunityCreateRequest request, CancellationToken ct)
    {
        ExigirCamposDaVendaDentroDoLimite(request.Cpf, request.Estado, request.TipoIndicacao, request.Veiculo, request.Porcentagem,
            ("Valor estimado", request.ValorEstimado), ("Mensalidade", request.Mensalidade), ("Mensalidade com desconto", request.MensalidadeComDesconto),
            ("Mensalidade com cupom", request.MensalidadeComCupom), ("Pagamento de adesão", request.PagamentoAdesao),
            ("Valor da indicação", request.ValorIndicacao), ("Total", request.Total));

        var lead = await db.CrmLeads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct)
            ?? throw new CrmNotFoundException("Lead", request.LeadId);

        if (!await equipe.PodeAcessarVendedorAsync(lead.ResponsavelId ?? Guid.Empty, ct))
        {
            throw new CrmForbiddenException();
        }

        if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId, ct))
        {
            throw new CrmForbiddenException("Você não pode criar oportunidades para este vendedor.");
        }
        await ResponsavelAtivo.GarantirAsync(db, request.ResponsavelId, ct);

        if (request.ValorEstimado < 0)
        {
            throw new CrmBusinessException("O valor estimado não pode ser negativo.", "valor_invalido");
        }

        var etapaInicial = request.EtapaId.HasValue
            ? await db.CrmPipelineStages.FirstOrDefaultAsync(s => s.Id == request.EtapaId, ct)
              ?? throw new CrmNotFoundException("Etapa", request.EtapaId.Value)
            : await db.CrmPipelineStages.Where(s => s.Ativa).OrderBy(s => s.Ordem).FirstAsync(ct);
        if (etapaInicial.Tipo == TipoEtapaPipeline.Ganho)
        {
            await GarantirUmaVendaConcluidaPorLeadAsync(lead.Id, null, ct);
            // Venda já criada como ganha: o card do lead vai junto para "Venda concluída", como em MudarEtapaAsync.
            TravarLeadDuranteAVenda(lead);
            await MoverLeadParaVendaConcluidaAsync(lead, null, ct);
        }

        var opportunity = new CrmOpportunity
        {
            LeadId = lead.Id,
            Titulo = request.Titulo.Trim(),
            ResponsavelId = request.ResponsavelId,
            EtapaId = etapaInicial.Id,
            EtapaDesde = DateTimeOffset.UtcNow,
            ProdutoOuServico = request.ProdutoOuServico,
            ValorEstimado = request.ValorEstimado,
            ProbabilidadeFechamento = request.ProbabilidadeFechamento,
            DataPrevistaFechamento = request.DataPrevistaFechamento,
            Concorrente = request.Concorrente,
            Observacoes = request.Observacoes,
            DataAdesao = request.DataAdesao,
            Mensalidade = request.Mensalidade,
            MensalidadeComDesconto = request.MensalidadeComDesconto,
            MensalidadeComCupom = request.MensalidadeComCupom,
            PagamentoAdesao = request.PagamentoAdesao,
            Porcentagem = request.Porcentagem,
            TermoAdesaoAceito = request.TermoAdesaoAceito,
            Migracao = request.Migracao,
            Cpf = string.IsNullOrWhiteSpace(request.Cpf) ? null : request.Cpf.Trim(),
            Estado = string.IsNullOrWhiteSpace(request.Estado) ? null : request.Estado.Trim().ToUpperInvariant(),
            Indicacao = request.Indicacao,
            // Venda do outro veículo de um cliente do tráfego pago: o canal da venda é o do card ("Indicação Lead").
            TipoIndicacao = Services.Crm.TipoIndicacaoLead.EhIndicacaoLead(lead.TipoIndicacao) && lead.VeiculoAdicionalDeLeadId is not null
                ? Services.Crm.TipoIndicacaoLead.IndicacaoLead
                : string.IsNullOrWhiteSpace(request.TipoIndicacao) ? null : request.TipoIndicacao.Trim(),
            ValorIndicacao = request.ValorIndicacao,
            Total = request.Total
        };

        if (request.Veiculo is not null)
        {
            opportunity.Veiculo = CriarOuAtualizarVeiculo(null, request.Veiculo);
        }

        db.CrmOpportunities.Add(opportunity);

        db.CrmStageHistories.Add(new CrmStageHistory
        {
            OpportunityId = opportunity.Id,
            EtapaAnteriorId = null,
            EtapaNovaId = etapaInicial.Id,
            UsuarioId = currentUser.UserId
        });

        await CompletarCadastroDoLeadAsync(lead, request.Cpf, request.Estado, request.TipoIndicacao, request.Veiculo?.Placa, ct);
        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("OportunidadeCriada", nameof(CrmOpportunity), opportunity.Id, new { opportunity.Titulo }, ct);
        eventos?.PublicarQuadroAtualizado("crm");

        return await ObterPorIdAsync(opportunity.Id, ct);
    }

    public async Task<OpportunityDto> AtualizarAsync(Guid id, OpportunityUpdateRequest request, CancellationToken ct)
    {
        ExigirCamposDaVendaDentroDoLimite(request.Cpf, request.Estado, request.TipoIndicacao, request.Veiculo, request.Porcentagem,
            ("Valor estimado", request.ValorEstimado), ("Mensalidade", request.Mensalidade), ("Mensalidade com desconto", request.MensalidadeComDesconto),
            ("Mensalidade com cupom", request.MensalidadeComCupom), ("Pagamento de adesão", request.PagamentoAdesao),
            ("Valor da indicação", request.ValorIndicacao), ("Total", request.Total));

        var opportunity = await CarregarComEscopoAsync(id, ct);
        db.Entry(opportunity).Property(o => o.RowVersion).OriginalValue = request.RowVersion;

        if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId, ct))
        {
            throw new CrmForbiddenException("Você não pode transferir esta oportunidade para este vendedor.");
        }
        // Só ao trocar de responsável: um card que já é de alguém que ficou inativo continua editável.
        if (request.ResponsavelId != opportunity.ResponsavelId)
        {
            await ResponsavelAtivo.GarantirAsync(db, request.ResponsavelId, ct);
        }

        if (request.ValorEstimado < 0)
        {
            throw new CrmBusinessException("O valor estimado não pode ser negativo.", "valor_invalido");
        }

        if (request.DataEfetivaFechamento is { } dataDaVenda)
        {
            ExigirDataDeVendaNaoFutura(dataDaVenda);
        }

        ExigirValorDaAdesaoComComprovante(opportunity, request.PagamentoAdesao);

        opportunity.Titulo = request.Titulo.Trim();
        opportunity.ResponsavelId = request.ResponsavelId;
        opportunity.ProdutoOuServico = request.ProdutoOuServico;
        opportunity.ValorEstimado = request.ValorEstimado;
        opportunity.ProbabilidadeFechamento = request.ProbabilidadeFechamento;
        opportunity.DataPrevistaFechamento = request.DataPrevistaFechamento;
        opportunity.Concorrente = request.Concorrente;
        opportunity.Observacoes = request.Observacoes;
        opportunity.DataAdesao = request.DataAdesao;
        opportunity.AtivoEm = NormalizarParaUtc(request.AtivoEm);
        opportunity.Mensalidade = request.Mensalidade;
        opportunity.MensalidadeComDesconto = request.MensalidadeComDesconto;
        opportunity.MensalidadeComCupom = request.MensalidadeComCupom;
        opportunity.PagamentoAdesao = request.PagamentoAdesao;
        opportunity.Porcentagem = request.Porcentagem;
        opportunity.TermoAdesaoAceito = request.TermoAdesaoAceito;
        opportunity.Migracao = request.Migracao;
        opportunity.Cpf = string.IsNullOrWhiteSpace(request.Cpf) ? null : request.Cpf.Trim();
        opportunity.Estado = string.IsNullOrWhiteSpace(request.Estado) ? null : request.Estado.Trim().ToUpperInvariant();
        opportunity.Indicacao = request.Indicacao;
        opportunity.TipoIndicacao = string.IsNullOrWhiteSpace(request.TipoIndicacao) ? null : request.TipoIndicacao.Trim();
        opportunity.ValorIndicacao = request.ValorIndicacao;
        opportunity.Total = request.Total;
        if (request.DataEfetivaFechamento is not null)
        {
            opportunity.DataEfetivaFechamento = DataDaVendaAoMeioDia(request.DataEfetivaFechamento.Value);
        }
        // Só quando vier: as outras telas de edição não mandam o campo e não podem apagar a data.
        if (request.DataPagamentoAdesaoPrevista is not null)
        {
            opportunity.DataPagamentoAdesaoPrevista = request.DataPagamentoAdesaoPrevista;
        }

        if (request.Veiculo is not null)
        {
            opportunity.Veiculo = CriarOuAtualizarVeiculo(opportunity.Veiculo, request.Veiculo);
        }

        await CompletarCadastroDoLeadAsync(opportunity.Lead, request.Cpf, request.Estado, request.TipoIndicacao, request.Veiculo?.Placa, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await audit.RegistrarAsync("OportunidadeAtualizada", nameof(CrmOpportunity), opportunity.Id, null, ct);
        eventos?.PublicarQuadroAtualizado("crm");

        return await ObterPorIdAsync(opportunity.Id, ct);
    }

    public async Task<OpportunityDto> MudarEtapaAsync(Guid id, ChangeStageRequest request, CancellationToken ct)
    {
        var opportunity = await CarregarComEscopoAsync(id, ct);
        db.Entry(opportunity).Property(o => o.RowVersion).OriginalValue = request.RowVersion;

        var novaEtapa = await db.CrmPipelineStages.FirstOrDefaultAsync(s => s.Id == request.NovaEtapaId, ct)
            ?? throw new CrmNotFoundException("Etapa", request.NovaEtapaId);

        if (opportunity.Etapa.Tipo != TipoEtapaPipeline.Aberta)
        {
            throw new CrmBusinessException("Não é possível mover uma oportunidade que já está Ganha ou Perdida.", "etapa_terminal");
        }

        string? motivoDescricao = null;
        CrmLeadStage? etapaDoLeadNaVenda = null;
        if (novaEtapa.Tipo == TipoEtapaPipeline.Perdido)
        {
            if (request.MotivoPerdaId is null)
            {
                throw new CrmBusinessException("Informe o motivo da perda ao mover para 'Perdido'.", "motivo_perda_obrigatorio");
            }

            var motivo = await db.CrmLossReasons.FirstOrDefaultAsync(m => m.Id == request.MotivoPerdaId, ct)
                ?? throw new CrmNotFoundException("Motivo de perda", request.MotivoPerdaId.Value);
            opportunity.MotivoPerdaId = motivo.Id;
            opportunity.MotivoPerdaObservacao = string.IsNullOrWhiteSpace(request.MotivoPerdaObservacao) ? null : request.MotivoPerdaObservacao.Trim();
            motivoDescricao = motivo.Descricao;
            opportunity.DataEfetivaFechamento = DateTimeOffset.UtcNow;
        }
        else if (novaEtapa.Tipo == TipoEtapaPipeline.Ganho)
        {
            ExigirCamposDaVendaDentroDoLimite(request.Cpf, request.Estado, request.TipoIndicacao, request.Veiculo, request.Porcentagem,
                ("Valor final", request.ValorFinal), ("Mensalidade", request.Mensalidade), ("Mensalidade com desconto", request.MensalidadeComDesconto),
                ("Mensalidade com cupom", request.MensalidadeComCupom), ("Pagamento de adesão", request.PagamentoAdesao),
                ("Valor da indicação", request.ValorIndicacao), ("Total", request.Total));
            if (request.ValorFinal is null || request.DataEfetivaFechamento is null)
            {
                throw new CrmBusinessException("Informe o valor final e a data de fechamento ao marcar como 'Ganho'.", "fechamento_incompleto");
            }

            ExigirDataDeVendaNaoFutura(request.DataEfetivaFechamento.Value);
            ExigirValorDaAdesaoComComprovante(opportunity, request.PagamentoAdesao);
            await GarantirUmaVendaConcluidaPorLeadAsync(opportunity.LeadId, opportunity.Id, ct);
            TravarLeadDuranteAVenda(opportunity.Lead);
            etapaDoLeadNaVenda = await MoverLeadParaVendaConcluidaAsync(opportunity.Lead, request.Indicacao, ct);

            opportunity.ValorFinal = request.ValorFinal;
            opportunity.DataEfetivaFechamento = DataDaVendaAoMeioDia(request.DataEfetivaFechamento.Value);
            opportunity.Cpf = string.IsNullOrWhiteSpace(request.Cpf) ? null : request.Cpf.Trim();
            opportunity.Estado = string.IsNullOrWhiteSpace(request.Estado) ? null : request.Estado.Trim().ToUpperInvariant();
            opportunity.Indicacao = request.Indicacao;
            opportunity.TipoIndicacao = string.IsNullOrWhiteSpace(request.TipoIndicacao) ? null : request.TipoIndicacao.Trim();
            // Venda do outro veículo de um cliente do tráfego pago: o canal da venda é o do card ("Indicação Lead").
            if (TipoIndicacaoLead.EhIndicacaoLead(opportunity.Lead.TipoIndicacao) && opportunity.Lead.VeiculoAdicionalDeLeadId is not null) opportunity.TipoIndicacao = TipoIndicacaoLead.IndicacaoLead;
            opportunity.ValorIndicacao = request.ValorIndicacao;
            opportunity.Total = request.Total;
            opportunity.AtivoEm = NormalizarParaUtc(request.AtivoEm);
            opportunity.Mensalidade = request.Mensalidade;
            opportunity.MensalidadeComDesconto = request.MensalidadeComDesconto;
            opportunity.MensalidadeComCupom = request.MensalidadeComCupom;
            opportunity.PagamentoAdesao = request.PagamentoAdesao;
            opportunity.Porcentagem = request.Porcentagem;
            opportunity.Migracao = request.Migracao;
            opportunity.DataPagamentoAdesaoPrevista = request.DataPagamentoAdesaoPrevista;

            await CompletarCadastroDoLeadAsync(opportunity.Lead, request.Cpf, request.Estado, request.TipoIndicacao, request.Veiculo?.Placa, ct);

            if (request.Veiculo is not null)
            {
                // Não usa CriarOuAtualizarVeiculo aqui: esse formulário de conclusão de venda não tem
                // campo de vistoriador, e sobrescrever VistoriadorId com null apagaria uma atribuição
                // já feita antes por outra tela.
                var veiculoExistente = opportunity.Veiculo;
                var veiculo = veiculoExistente ?? new CrmVeiculo();
                veiculo.Descricao = request.Veiculo.Descricao;
                veiculo.Placa = request.Veiculo.Placa?.Trim().ToUpperInvariant();
                veiculo.Chassi = NormalizarChassi(request.Veiculo.Chassi);
                veiculo.Fipe = request.Veiculo.Fipe;
                veiculo.Rastreador = request.Veiculo.Rastreador;
                veiculo.ValorVistoria = request.Veiculo.ValorVistoria;
                veiculo.DataChegada = request.Veiculo.DataChegada;
                opportunity.Veiculo = veiculo;
                // Ver comentário em CriarOuAtualizarVeiculo: sem isto, o EF trata o veículo novo
                // (Id já preenchido pelo construtor de CrmEntityBase) como existente e tenta um UPDATE
                // que não afeta nenhuma linha, disparando DbUpdateConcurrencyException.
                if (veiculoExistente is null) db.CrmVeiculos.Add(veiculo);
            }
        }

        var etapaAnteriorId = opportunity.EtapaId;
        opportunity.EtapaId = novaEtapa.Id;
        opportunity.EtapaDesde = DateTimeOffset.UtcNow;

        db.CrmStageHistories.Add(new CrmStageHistory
        {
            OpportunityId = opportunity.Id,
            EtapaAnteriorId = etapaAnteriorId,
            EtapaNovaId = novaEtapa.Id,
            UsuarioId = currentUser.UserId,
            MotivoPerdaDescricao = motivoDescricao
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await audit.RegistrarAsync("OportunidadeMudouEtapa", nameof(CrmOpportunity), opportunity.Id,
            new { EtapaAnterior = etapaAnteriorId, EtapaNova = novaEtapa.Id }, ct);
        eventos?.PublicarQuadroAtualizado("crm");

        // Retorno de conversão offline (CAPI): só depois que a venda ganha já está persistida —
        // uma falha aqui nunca deve desfazer nem bloquear o registro da venda no CRM.
        if (novaEtapa.Tipo == TipoEtapaPipeline.Ganho)
        {
            var enviado = false;
            try
            {
                enviado = await conversion.EnviarConversaoVendaAsync(opportunity.Lead, opportunity, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A venda já está salva: montar/enviar o evento para o Facebook nunca pode devolver erro ao consultor (o envio em si já trata as próprias falhas).
            }
            if (enviado)
            {
                opportunity.ConversaoOfflineEnviadaEm = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            // Mesmo evento que o quadro de leads mandava ao mover o card para "Venda concluída" (agora o servidor move junto com a venda).
            if (etapaDoLeadNaVenda is not null)
            {
                try
                {
                    var valor = opportunity.PagamentoAdesao is > 0 ? opportunity.PagamentoAdesao : opportunity.Lead.ValorAdesao is > 0 ? opportunity.Lead.ValorAdesao : null;
                    await conversion.EnviarEventoEtapaAsync(opportunity.Lead, etapaDoLeadNaVenda.Id, etapaDoLeadNaVenda.Nome, ct, valor);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // o evento é um extra: a venda já está salva
                }
            }

            // Aviso no canal da regional (ligado pelo administrador). A venda já está salva: nada daqui pode desfazê-la nem fazê-la falhar.
            if (publicador is not null)
            {
                try
                {
                    await publicador.PublicarVendaAsync(opportunity.Id, ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // aviso é um extra
                }
            }
        }

        return await ObterPorIdAsync(opportunity.Id, ct);
    }

    // --- auxiliares ---

    private const string ColunaVendaConcluidaLeads = "Venda concluída (Leads)";
    private const string ColunaVendaConcluidaIndicacao = "Venda concluída (Indicação)";

    /// <summary>
    /// Concluir a venda leva o card do lead para "Venda concluída (Leads)" ou "(Indicação)" na mesma operação, pela etiqueta do cliente — antes a tela
    /// movia o card num segundo passo, e quando esse passo falhava (conflito de versão, queda de rede, outra aba) a venda ficava ganha com o card
    /// parado em "Em atendimento"/"Cotação". Lead (tráfego) cuja venda teve indicação vai para a coluna das indicações com a etiqueta "Indicação Lead".
    /// Devolve a coluna para onde o card foi (nulo se não mudou ou se o quadro não tem a coluna ativa).
    /// </summary>
    private async Task<CrmLeadStage?> MoverLeadParaVendaConcluidaAsync(CrmLead lead, bool? vendaComIndicacao, CancellationToken ct)
    {
        // Outro veículo de um cliente do tráfego pago: a segunda venda é uma venda de indicação ("Indicação Lead"), sem a etiqueta do produto nem a do anúncio.
        if (EhOutroVeiculoDeLead(lead)) ComoIndicacaoLeadDeOutroVeiculo(lead);
        var vaiComoIndicacao = Services.Notion.NotionEtapaLead.EhIndicacao(lead.CriadoManualmente, lead.TipoIndicacao)
            || (vendaComIndicacao == true && Services.Crm.TipoIndicacaoLead.EhLead(lead.TipoIndicacao));
        var nomeDestino = vaiComoIndicacao ? ColunaVendaConcluidaIndicacao : ColunaVendaConcluidaLeads;
        var destino = await db.CrmLeadStages.FirstOrDefaultAsync(s => s.Ativa && s.Nome == nomeDestino, ct);
        if (destino is null) return null;

        if (vendaComIndicacao == true && Services.Crm.TipoIndicacaoLead.EhLead(lead.TipoIndicacao)) lead.TipoIndicacao = Services.Crm.TipoIndicacaoLead.IndicacaoLead;

        if (lead.EtapaId == destino.Id) return null;
        lead.EtapaId = destino.Id;
        lead.MotivoPerdaId = null;
        lead.MotivoPerdaObservacao = null;
        lead.VeiculoNaoAtendido = null;
        return destino;
    }

    /// <summary>Card de "Outro veículo" (veículo adicional) de um cliente que veio como Lead (tráfego pago): ainda não é indicação.</summary>
    internal static bool EhOutroVeiculoDeLead(CrmLead lead) =>
        lead.VeiculoAdicionalDeLeadId is not null && !Services.Notion.NotionEtapaLead.EhIndicacao(lead.CriadoManualmente, lead.TipoIndicacao);

    /// <summary>
    /// A venda do outro veículo de um cliente do tráfego pago conta como indicação: canal "Indicação Lead", e o card perde a etiqueta do produto (AGV...) e a do anúncio
    /// (origem/campanha — a venda não veio de um anúncio). A marca de edição no CRM impede a sincronização do Notion de desfazer.
    /// </summary>
    internal static void ComoIndicacaoLeadDeOutroVeiculo(CrmLead lead)
    {
        lead.TipoIndicacao = Services.Crm.TipoIndicacaoLead.IndicacaoLead;
        lead.TipoIndicacaoEditadoNoCrm = true;
        lead.ProdutoInteresse = null;
        lead.Origem = null;
        lead.Campanha = null;
    }

    /// <summary>
    /// Um cliente (lead) tem no máximo UMA venda concluída: outro veículo dele é outro card ("Outro veículo"). Sem isto, uma venda
    /// repetida (reenvio do formulário, nova venda sobre um card já concluído) duplicava vendas, comissões e números. As vendas já
    /// importadas do Notion com mais de um veículo no mesmo card não são afetadas — só o que se conclui daqui para frente.
    /// </summary>
    private async Task GarantirUmaVendaConcluidaPorLeadAsync(Guid leadId, Guid? ignorarOportunidadeId, CancellationToken ct)
    {
        var jaTem = await db.CrmOpportunities.AnyAsync(
            o => o.LeadId == leadId && o.Id != ignorarOportunidadeId && !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho, ct);
        if (jaTem)
        {
            throw new CrmBusinessException(
                "Este cliente já tem uma venda concluída. Para outro veículo dele, use \"Outro veículo\" no card do cliente.", "venda_ja_concluida");
        }
    }

    /// <summary>
    /// Duas vendas do mesmo cliente concluídas ao mesmo tempo passariam as duas pela conferência acima. Gravar o lead na mesma operação
    /// faz a segunda falhar pelo token de concorrência do banco (xmin), e o servidor responde com conflito em vez de duplicar a venda.
    /// </summary>
    private void TravarLeadDuranteAVenda(CrmLead lead) =>
        db.Entry(lead).Property(l => l.AtualizadoEm).IsModified = true;

    /// <summary>
    /// Os dados da venda (CPF, estado, canal de aquisição, placa) são digitados na oportunidade, mas o card do cliente lê o próprio cadastro do lead:
    /// sem esta cópia o card ficava com esses campos em branco. Só preenche o que o cadastro ainda não tem (nunca sobrescreve) e nunca troca a etiqueta
    /// do card (só completa o canal de quem já é Indicação, e nunca com "Lead") — isso moveria o card de coluna; o CPF/CNPJ só entra se nenhum outro cliente ativo já o usa.
    /// </summary>
    private async Task CompletarCadastroDoLeadAsync(CrmLead lead, string? cpf, string? estado, string? tipoIndicacao, string? placa, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(lead.Estado) && !string.IsNullOrWhiteSpace(estado) && estado.Trim().Length <= 2)
            lead.Estado = estado.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(lead.Placa) && !string.IsNullOrWhiteSpace(placa) && placa.Trim().Length <= 10)
            lead.Placa = placa.Trim().ToUpperInvariant();
        if (lead.CriadoManualmente && string.IsNullOrWhiteSpace(lead.TipoIndicacao) && !string.IsNullOrWhiteSpace(tipoIndicacao) && tipoIndicacao.Trim().Length <= 80
            && !TipoIndicacaoLead.EhLead(tipoIndicacao))
            lead.TipoIndicacao = tipoIndicacao.Trim();

        if (string.IsNullOrEmpty(lead.DocumentoNormalizado) && !string.IsNullOrWhiteSpace(cpf))
        {
            var documento = DocumentValidation.NormalizarDocumento(cpf, out var valido);
            if (valido && documento is not null
                && !await db.CrmLeads.AnyAsync(l => l.Id != lead.Id && l.DocumentoNormalizado == documento && !l.Arquivado && l.VeiculoAdicionalDeLeadId == null, ct))
            {
                lead.DocumentoNormalizado = documento;
            }
        }
    }

    private async Task<IQueryable<CrmOpportunity>> QueryEscopadaAsync(CancellationToken ct)
    {
        var query = db.CrmOpportunities.AsNoTracking().AsQueryable();
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null) query = query.Where(o => visiveis.Contains(o.ResponsavelId));
        return query;
    }

    /// <summary>
    /// Exclusão (arquivamento) de uma oportunidade. Admin/GestorMaster e gestores comerciais excluem
    /// as que enxergam (a carga já respeita a equipe); consultores, só as próprias. O lead continua
    /// no quadro de leads.
    /// </summary>
    public async Task ExcluirAsync(Guid id, CancellationToken ct)
    {
        var opportunity = await CarregarComEscopoAsync(id, ct);
        if (!currentUser.TemVisaoTotal && !currentUser.PodeGerirComercial && opportunity.ResponsavelId != currentUser.UserId)
        {
            throw new CrmForbiddenException("Você só pode excluir as suas próprias oportunidades.");
        }

        opportunity.Arquivado = true;
        opportunity.ArquivadoEm = DateTimeOffset.UtcNow;
        opportunity.ArquivadoPorId = currentUser.UserId;

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("OportunidadeExcluida", nameof(CrmOpportunity), opportunity.Id,
            new { opportunity.Titulo, Lead = opportunity.Lead.NomeOuRazaoSocial }, ct);
        // Tira o card da tela de quem estiver com o Pipeline aberto (ex.: o consultor responsável).
        eventos?.PublicarQuadroAtualizado("crm");
    }

    public async Task<IReadOnlyList<LembreteAdesaoDto>> ListarLembretesAdesaoAsync(CancellationToken ct)
    {
        // "Hoje" no horário de Brasília (UTC-3, sem horário de verão desde 2019).
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var usuarioId = currentUser.UserId;

        return await db.CrmOpportunities.AsNoTracking()
            .Where(o => o.ResponsavelId == usuarioId
                && !o.Arquivado
                && o.DataPagamentoAdesaoPrevista != null
                && o.DataPagamentoAdesaoPrevista <= hoje
                && o.PagamentoAdesaoArquivoUrl == null)
            .OrderBy(o => o.DataPagamentoAdesaoPrevista)
            .Select(o => new LembreteAdesaoDto(o.Id, o.LeadId, o.Lead.NomeOuRazaoSocial, o.DataPagamentoAdesaoPrevista!.Value, o.PagamentoAdesao))
            .ToListAsync(ct);
    }

    /// <summary>
    /// "Data da venda" do formulário vem só com o dia. À meia-noite UTC seria 21h do dia anterior em Brasília — a venda de
    /// hoje contava como de ontem (e a do dia 1º, como do mês passado). Ao meio-dia UTC (9h em Brasília) cai sempre no dia certo.
    /// </summary>
    internal static DateTimeOffset DataDaVendaAoMeioDia(DateOnly dia) => new(dia.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    /// <summary>
    /// Venda não acontece no futuro: a data da venda é o dia em que ela foi fechada. Uma data futura (digitada errada, ou a de
    /// ativação/pagamento no lugar da data da venda) entrava nos totais, rankings e metas do mês antes de acontecer. "Hoje" é o dia
    /// de Brasília, como no resto do CRM — comparar com a data em UTC bloquearia vendas legítimas feitas depois das 21h.
    /// </summary>
    internal static void ExigirDataDeVendaNaoFutura(DateOnly dia)
    {
        if (dia > HorarioBrasilia.Hoje)
        {
            throw new CrmBusinessException(
                "A data da venda não pode ser uma data futura. Informe o dia em que a venda foi fechada (a data de ativação ou do pagamento da adesão é outro campo).",
                "data_venda_futura");
        }
    }

    private async Task<CrmOpportunity> CarregarComEscopoAsync(Guid id, CancellationToken ct)
    {
        var opportunity = await db.CrmOpportunities
            .Include(o => o.Lead)
            .Include(o => o.Etapa)
            .Include(o => o.Responsavel)
            .Include(o => o.MotivoPerda)
            .Include(o => o.Veiculo).ThenInclude(v => v!.Vistoriador)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new CrmNotFoundException("Oportunidade", id);

        if (!await equipe.PodeAcessarVendedorAsync(opportunity.ResponsavelId, ct))
        {
            throw new CrmForbiddenException();
        }

        return opportunity;
    }

    private CrmVeiculo CriarOuAtualizarVeiculo(CrmVeiculo? existente, VeiculoUpsertRequest request)
    {
        var veiculo = existente ?? new CrmVeiculo();
        veiculo.Descricao = request.Descricao;
        veiculo.Placa = request.Placa?.Trim().ToUpperInvariant();
        veiculo.Chassi = NormalizarChassi(request.Chassi);
        veiculo.Fipe = request.Fipe;
        veiculo.Rastreador = request.Rastreador;
        veiculo.ValorVistoria = request.ValorVistoria;
        veiculo.VistoriadorId = request.VistoriadorId;
        veiculo.DataChegada = request.DataChegada;
        // Como CrmEntityBase já preenche Id com um Guid não-vazio no construtor, o EF não consegue
        // inferir sozinho que este é um registro novo ao ser alcançado via fixup de navegação a partir
        // de uma oportunidade já rastreada — sem este Add() explícito, ele tenta um UPDATE (0 linhas
        // afetadas) em vez de um INSERT, disparando DbUpdateConcurrencyException.
        if (existente is null) db.CrmVeiculos.Add(veiculo);
        return veiculo;
    }

    // O Postgres só aceita 'timestamp with time zone' em UTC (offset zero). O front-end envia esse
    // campo como uma data pura (ex.: "2026-09-17"), que o model binding do ASP.NET interpreta como
    // meia-noite no fuso local do servidor — daí o offset não-zero que o Npgsql rejeita. Aqui
    // preservamos a data (dia/mês/ano) como o usuário a escolheu e a re-ancoramos em UTC.
    private static DateTimeOffset? NormalizarParaUtc(DateTimeOffset? valor) =>
        valor is null ? null : new DateTimeOffset(valor.Value.Date, TimeSpan.Zero);

    /// <summary>Chassi em maiúsculas e sem espaços (ex.: "9bw zzz377vt004251" → "9BWZZZ377VT004251").</summary>
    private static string? NormalizarChassi(string? chassi)
    {
        if (string.IsNullOrWhiteSpace(chassi)) return null;
        var normalizado = string.Concat(chassi.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return normalizado.Length <= 30 ? normalizado : normalizado[..30];
    }

    private static VeiculoDto? ParaVeiculoDto(CrmVeiculo? v) => v is null
        ? null
        : new VeiculoDto(v.Id, v.Descricao, v.Placa, v.Fipe, v.Rastreador, v.ValorVistoria, v.VistoriadorId, v.Vistoriador?.NomeCompleto, v.DataChegada, v.Chassi);

    private static OpportunityDto ParaDto(CrmOpportunity o, DateOnly hoje) => new(
        o.Id,
        o.LeadId,
        o.Lead.NomeOuRazaoSocial,
        o.Titulo,
        o.ResponsavelId,
        o.Responsavel.NomeCompleto,
        o.EtapaId,
        o.Etapa.Nome,
        o.Etapa.Tipo,
        o.EtapaDesde,
        o.ProdutoOuServico,
        o.ValorEstimado,
        o.ProbabilidadeFechamento,
        o.DataPrevistaFechamento,
        o.ValorFinal,
        o.DataEfetivaFechamento,
        o.MotivoPerda != null ? o.MotivoPerda.Descricao : null,
        o.MotivoPerdaObservacao,
        o.Concorrente,
        o.Observacoes,
        o.DataAdesao,
        o.AtivoEm,
        o.Mensalidade,
        o.MensalidadeComDesconto,
        o.MensalidadeComCupom,
        o.PagamentoAdesao,
        o.Porcentagem,
        o.TermoAdesaoAceito,
        o.Migracao,
        ParaVeiculoDto(o.Veiculo),
        o.CriadoEm,
        o.AtualizadoEm,
        o.RowVersion,
        o.Etapa.Tipo == TipoEtapaPipeline.Aberta && o.DataPrevistaFechamento.HasValue && o.DataPrevistaFechamento.Value < hoje,
        o.Cpf,
        o.Estado,
        o.Indicacao,
        o.TipoIndicacao,
        o.ValorIndicacao,
        o.Total,
        o.TermoAdesaoArquivoUrl,
        o.PagamentoAdesaoArquivoUrl,
        o.ComprovanteIndicacaoArquivoUrl,
        o.ComprovanteVistoriaArquivoUrl,
        o.ConversaoOfflineEnviadaEm,
        o.DataPagamentoAdesaoPrevista);

    /// <summary>
    /// Comprovante de pagamento da adesão anexado exige o valor pago: sem ele a venda fica de fora da meta e do card de adesão
    /// (foi o que deixou vendas pagas com adesão em branco). O comprovante é enviado antes do salvamento da venda, por isso a trava
    /// fica aqui, quando o valor chega — e não no envio do arquivo. Adesão ainda a pagar não tem comprovante: usa a data prevista.
    /// </summary>
    private static void ExigirValorDaAdesaoComComprovante(CrmOpportunity opportunity, decimal? pagamentoAdesao)
    {
        if (opportunity.PagamentoAdesaoArquivoUrl is not null && pagamentoAdesao is not > 0)
        {
            throw new CrmBusinessException(
                "Informe o valor da adesão paga: há um comprovante de pagamento anexado.",
                "adesao_valor_obrigatorio");
        }
    }

    public async Task<OpportunityDto> AnexarArquivoAsync(Guid id, string tipo, IFormFile arquivo, CancellationToken ct)
    {
        if (!TiposAnexoValidos.Contains(tipo))
        {
            throw new CrmBusinessException("Tipo de anexo inválido.", "anexo_tipo_invalido");
        }

        var opportunity = await CarregarComEscopoAsync(id, ct);

        if (arquivo is null || arquivo.Length == 0)
        {
            throw new CrmBusinessException("Selecione um arquivo.", "anexo_invalido");
        }
        if (arquivo.Length > 10 * 1024 * 1024)
        {
            throw new CrmBusinessException("O arquivo deve ter no máximo 10 MB.", "anexo_invalido");
        }

        var extensao = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
        if (!ExtensoesAnexoPermitidas.Contains(extensao))
        {
            throw new CrmBusinessException("Formato inválido. Envie um PDF, JPG, PNG ou WEBP.", "anexo_invalido");
        }

        var tipoNormalizado = tipo.ToLowerInvariant();
        var urlAtual = tipoNormalizado switch
        {
            "termo-adesao" => opportunity.TermoAdesaoArquivoUrl,
            "pagamento-adesao" => opportunity.PagamentoAdesaoArquivoUrl,
            "comprovante-indicacao" => opportunity.ComprovanteIndicacaoArquivoUrl,
            _ => opportunity.ComprovanteVistoriaArquivoUrl,
        };
        // O conteúdo precisa ser do formato da extensão (um arquivo renomeado não passa), e só depois o anterior é apagado.
        await using var stream = arquivo.OpenReadStream();
        if (!await AssinaturaDeArquivo.ConfereAsync(stream, extensao, ct))
        {
            throw new CrmBusinessException("O conteúdo do arquivo não corresponde ao formato. Envie um PDF, JPG, PNG ou WEBP válido.", "anexo_invalido");
        }

        await armazenamento.ExcluirSeExistirAsync(urlAtual, ct);

        var nomeArquivo = $"{tipoNormalizado}{extensao}";
        var novaUrl = await armazenamento.SalvarAsync($"opportunities/{opportunity.Id}", nomeArquivo, stream, arquivo.ContentType, ct);

        switch (tipoNormalizado)
        {
            case "termo-adesao":
                opportunity.TermoAdesaoArquivoUrl = novaUrl;
                opportunity.TermoAdesaoAceito = true;
                break;
            case "pagamento-adesao":
                opportunity.PagamentoAdesaoArquivoUrl = novaUrl;
                break;
            case "comprovante-indicacao":
                opportunity.ComprovanteIndicacaoArquivoUrl = novaUrl;
                break;
            default:
                opportunity.ComprovanteVistoriaArquivoUrl = novaUrl;
                break;
        }

        await db.SaveChangesAsync(ct);

        return await ObterPorIdAsync(opportunity.Id, ct);
    }

}
