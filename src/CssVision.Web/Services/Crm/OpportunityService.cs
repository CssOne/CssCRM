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
    IFileStorageService armazenamento) : IOpportunityService
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
        if (!string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(o => o.Lead.Origem == filtro.Origem);
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

    public async Task<OpportunityDto> CriarAsync(OpportunityCreateRequest request, CancellationToken ct)
    {
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

        if (request.ValorEstimado < 0)
        {
            throw new CrmBusinessException("O valor estimado não pode ser negativo.", "valor_invalido");
        }

        var etapaInicial = request.EtapaId.HasValue
            ? await db.CrmPipelineStages.FirstOrDefaultAsync(s => s.Id == request.EtapaId, ct)
              ?? throw new CrmNotFoundException("Etapa", request.EtapaId.Value)
            : await db.CrmPipelineStages.Where(s => s.Ativa).OrderBy(s => s.Ordem).FirstAsync(ct);

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
            Migracao = request.Migracao
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

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("OportunidadeCriada", nameof(CrmOpportunity), opportunity.Id, new { opportunity.Titulo }, ct);

        return await ObterPorIdAsync(opportunity.Id, ct);
    }

    public async Task<OpportunityDto> AtualizarAsync(Guid id, OpportunityUpdateRequest request, CancellationToken ct)
    {
        var opportunity = await CarregarComEscopoAsync(id, ct);
        db.Entry(opportunity).Property(o => o.RowVersion).OriginalValue = request.RowVersion;

        if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId, ct))
        {
            throw new CrmForbiddenException("Você não pode transferir esta oportunidade para este vendedor.");
        }

        if (request.ValorEstimado < 0)
        {
            throw new CrmBusinessException("O valor estimado não pode ser negativo.", "valor_invalido");
        }

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
            opportunity.DataEfetivaFechamento = request.DataEfetivaFechamento.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        if (request.Veiculo is not null)
        {
            opportunity.Veiculo = CriarOuAtualizarVeiculo(opportunity.Veiculo, request.Veiculo);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await audit.RegistrarAsync("OportunidadeAtualizada", nameof(CrmOpportunity), opportunity.Id, null, ct);

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
            if (request.ValorFinal is null || request.DataEfetivaFechamento is null)
            {
                throw new CrmBusinessException("Informe o valor final e a data de fechamento ao marcar como 'Ganho'.", "fechamento_incompleto");
            }

            opportunity.ValorFinal = request.ValorFinal;
            opportunity.DataEfetivaFechamento = request.DataEfetivaFechamento.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            opportunity.Cpf = string.IsNullOrWhiteSpace(request.Cpf) ? null : request.Cpf.Trim();
            opportunity.Estado = string.IsNullOrWhiteSpace(request.Estado) ? null : request.Estado.Trim().ToUpperInvariant();
            opportunity.Indicacao = request.Indicacao;
            opportunity.TipoIndicacao = string.IsNullOrWhiteSpace(request.TipoIndicacao) ? null : request.TipoIndicacao.Trim();
            opportunity.ValorIndicacao = request.ValorIndicacao;
            opportunity.Total = request.Total;
            opportunity.AtivoEm = NormalizarParaUtc(request.AtivoEm);
            opportunity.Mensalidade = request.Mensalidade;
            opportunity.MensalidadeComDesconto = request.MensalidadeComDesconto;
            opportunity.MensalidadeComCupom = request.MensalidadeComCupom;
            opportunity.PagamentoAdesao = request.PagamentoAdesao;
            opportunity.Porcentagem = request.Porcentagem;
            opportunity.Migracao = request.Migracao;

            if (request.Veiculo is not null)
            {
                // Não usa CriarOuAtualizarVeiculo aqui: esse formulário de conclusão de venda não tem
                // campo de vistoriador, e sobrescrever VistoriadorId com null apagaria uma atribuição
                // já feita antes por outra tela.
                var veiculoExistente = opportunity.Veiculo;
                var veiculo = veiculoExistente ?? new CrmVeiculo();
                veiculo.Descricao = request.Veiculo.Descricao;
                veiculo.Placa = request.Veiculo.Placa?.Trim().ToUpperInvariant();
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

        // Retorno de conversão offline (CAPI): só depois que a venda ganha já está persistida —
        // uma falha aqui nunca deve desfazer nem bloquear o registro da venda no CRM.
        if (novaEtapa.Tipo == TipoEtapaPipeline.Ganho)
        {
            var enviado = await conversion.EnviarConversaoVendaAsync(opportunity.Lead, opportunity, ct);
            if (enviado)
            {
                opportunity.ConversaoOfflineEnviadaEm = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        return await ObterPorIdAsync(opportunity.Id, ct);
    }

    // --- auxiliares ---

    private async Task<IQueryable<CrmOpportunity>> QueryEscopadaAsync(CancellationToken ct)
    {
        var query = db.CrmOpportunities.AsNoTracking().AsQueryable();
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null) query = query.Where(o => visiveis.Contains(o.ResponsavelId));
        return query;
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

    private static VeiculoDto? ParaVeiculoDto(CrmVeiculo? v) => v is null
        ? null
        : new VeiculoDto(v.Id, v.Descricao, v.Placa, v.Fipe, v.Rastreador, v.ValorVistoria, v.VistoriadorId, v.Vistoriador?.NomeCompleto, v.DataChegada);

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
        o.ComprovanteVistoriaArquivoUrl);

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
        await armazenamento.ExcluirSeExistirAsync(urlAtual, ct);

        var nomeArquivo = $"{tipoNormalizado}{extensao}";
        await using var stream = arquivo.OpenReadStream();
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
