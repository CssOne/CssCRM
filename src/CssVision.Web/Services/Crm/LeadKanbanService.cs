using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Quadro de leads. A base tem dezenas de milhares de leads, então nada é carregado inteiro: cada
/// coluna traz só os cartões mais recentes (<see cref="LeadKanbanFilterRequest.CartoesPorColuna"/>) e
/// o total dela, contado no banco; "Ver mais" busca a próxima página de uma coluna
/// (<see cref="ObterCartoesAsync"/>). Os cartões são projetados direto em SQL — só os campos exibidos.
/// </summary>
public sealed class LeadKanbanService(
    ApplicationDbContext db, IEquipeComercialService equipe, IMemoryCache? cache = null, ICrmEventHub? eventos = null,
    Microsoft.Extensions.Options.IOptions<DistribuicaoOptions>? distribuicao = null) : ILeadKanbanService
{
    private const int MaxCartoesPorPagina = 200;

    /// <summary>Coluna do quadro que tem o filtro por motivo — ver CrmSeeder.cs.</summary>
    private const string EtapaPerdido = "Perdido";

    public async Task<LeadKanbanBoardDto> ObterBoardAsync(LeadKanbanFilterRequest filtro, CancellationToken ct)
    {
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        return await RespostaEmCache.ObterAsync(cache, eventos, "quadro", new { escopo = RespostaEmCache.Escopo(visiveis), filtro },
            () => CalcularBoardAsync(filtro, visiveis, ct));
    }

    private async Task<LeadKanbanBoardDto> CalcularBoardAsync(LeadKanbanFilterRequest filtro, List<Guid>? visiveis, CancellationToken ct)
    {
        var etapas = await db.CrmLeadStages.AsNoTracking()
            .Where(s => s.Ativa)
            .OrderBy(s => s.Ordem)
            .ToListAsync(ct);

        var (query, podeVerOrigem) = Filtrar(filtro, visiveis);
        var porPagina = Math.Clamp(filtro.CartoesPorColuna, 1, MaxCartoesPorPagina);

        var totais = await query
            .GroupBy(l => l.EtapaId)
            .Select(g => new { EtapaId = g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);
        int Total(Guid? etapaId) => totais.FirstOrDefault(t => t.EtapaId == etapaId)?.Quantidade ?? 0;

        // Coluna virtual (sem linha em CrmLeadStage): leads que ainda não foram trabalhados por
        // ninguém. Fica sempre em primeiro, pra vendedora enxergar de cara quem ainda não pegou.
        var colunas = new List<LeadKanbanColumnDto>
        {
            new(new LeadStageDto(null, "Sem etapa", -1, "#94a3b8", false, true),
                Total(null) == 0 ? [] : await PaginaAsync(query, null, 0, porPagina, podeVerOrigem, ct),
                Total(null)),
        };

        foreach (var etapa in etapas)
        {
            var dto = new LeadStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Cor, etapa.Fechada, etapa.Ativa);
            if (etapa.Nome == EtapaPerdido)
            {
                colunas.Add(await ColunaPerdidoAsync(dto, query, filtro.MotivoPerdaId, porPagina, podeVerOrigem, ct));
                continue;
            }

            var total = Total(etapa.Id);
            var cartoes = total == 0 ? [] : await PaginaAsync(query, etapa.Id, 0, porPagina, podeVerOrigem, ct);
            colunas.Add(new LeadKanbanColumnDto(dto, cartoes, total));
        }

        return new LeadKanbanBoardDto(colunas);
    }

    /// <summary>
    /// Coluna "Perdido": os motivos dos cartões dela (com os filtros de cima) para o filtro da coluna,
    /// e os cartões/total já filtrados pelos motivos escolhidos.
    /// </summary>
    private async Task<LeadKanbanColumnDto> ColunaPerdidoAsync(
        LeadStageDto etapa, IQueryable<CrmLead> query, Guid[]? motivos, int porPagina, bool podeVerOrigem, CancellationToken ct)
    {
        var porMotivo = await query
            .Where(l => l.EtapaId == etapa.Id)
            .GroupBy(l => new { l.MotivoPerdaId, Descricao = l.MotivoPerda != null ? l.MotivoPerda.Descricao : null })
            .Select(g => new { g.Key.MotivoPerdaId, g.Key.Descricao, Quantidade = g.Count() })
            .ToListAsync(ct);
        var opcoes = porMotivo
            .Select(m => new LeadKanbanMotivoPerdaDto(m.MotivoPerdaId ?? Guid.Empty, m.Descricao ?? "Sem motivo informado", m.Quantidade))
            .OrderByDescending(m => m.Quantidade)
            .ToList();

        var filtrada = FiltrarMotivo(query, motivos);
        var total = motivos is { Length: > 0 }
            ? opcoes.Where(o => motivos.Contains(o.Id)).Sum(o => o.Quantidade)
            : opcoes.Sum(o => o.Quantidade);
        var cartoes = total == 0 ? [] : await PaginaAsync(filtrada, etapa.Id, 0, porPagina, podeVerOrigem, ct);
        return new LeadKanbanColumnDto(etapa, cartoes, total, opcoes);
    }

    private static IQueryable<CrmLead> FiltrarMotivo(IQueryable<CrmLead> query, Guid[]? motivos)
    {
        if (motivos is not { Length: > 0 }) return query;
        var ids = motivos.Where(m => m != Guid.Empty).Select(m => (Guid?)m).ToList();
        var semMotivo = motivos.Contains(Guid.Empty);
        return query.Where(l => (l.MotivoPerdaId != null && ids.Contains(l.MotivoPerdaId)) || (semMotivo && l.MotivoPerdaId == null));
    }

    public async Task<IReadOnlyList<LeadKanbanCardDto>> ObterCartoesAsync(LeadKanbanColunaRequest request, CancellationToken ct)
    {
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        return await RespostaEmCache.ObterAsync(cache, eventos, "quadro-coluna", new { escopo = RespostaEmCache.Escopo(visiveis), request },
            async () =>
            {
                var (query, podeVerOrigem) = Filtrar(request, visiveis);
                // "Ver mais" da coluna Perdido respeita o filtro por motivo dela.
                if (request.MotivoPerdaId is { Length: > 0 } && request.EtapaId is { } etapaId
                    && await db.CrmLeadStages.AnyAsync(s => s.Id == etapaId && s.Nome == EtapaPerdido, ct))
                {
                    query = FiltrarMotivo(query, request.MotivoPerdaId);
                }
                var quantidade = Math.Clamp(request.Quantidade, 1, MaxCartoesPorPagina);
                return await PaginaAsync(query, request.EtapaId, Math.Max(0, request.Pular), quantidade, podeVerOrigem, ct);
            });
    }

    /// <summary>Uma página de cartões de uma coluna (EtapaId nulo = "Sem etapa"), dos mais recentes para os mais antigos.</summary>
    private static async Task<IReadOnlyList<LeadKanbanCardDto>> PaginaAsync(
        IQueryable<CrmLead> query, Guid? etapaId, int pular, int quantidade, bool podeVerOrigem, CancellationToken ct)
    {
        var linhas = await query
            .Where(l => l.EtapaId == etapaId)
            .OrderByDescending(l => l.CriadoEm)
            .ThenBy(l => l.Id)
            .Skip(pular)
            .Take(quantidade)
            .Select(l => new
            {
                l.Id, l.NomeOuRazaoSocial, l.Telefone, l.Telefone2, l.Email, l.Estado, l.Origem, l.Campanha,
                l.Placa, l.TemSeguro, l.UtilidadeVeiculo, l.TipoIndicacao, l.CriadoManualmente,
                l.ResponsavelId,
                ResponsavelNome = l.Responsavel != null ? l.Responsavel.NomeCompleto : null,
                ResponsavelFotoUrl = l.Responsavel != null ? l.Responsavel.FotoUrl : null,
                Tags = l.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
                l.CriadoEm, l.UltimoContatoEm, l.Arquivado, l.RowVersion, l.ProdutoInteresse, l.ValorAdesao,
                VeiculoAdicional = l.VeiculoAdicionalDeLeadId != null,
                MotivoPerda = l.MotivoPerda != null ? l.MotivoPerda.Descricao : null,
                l.MotivoPerdaObservacao,
                l.VeiculoNaoAtendido,
                // A oportunidade mais recente é a fonte dos selos Migração/Indicação — normalmente é a
                // que fechou a venda (o lead só chega na coluna "Venda concluída" depois disso).
                Oportunidade = l.Oportunidades
                    .OrderByDescending(o => o.CriadoEm)
                    .Select(o => new { o.Migracao, o.Indicacao, Placa = o.Veiculo != null ? o.Veiculo.Placa : null })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return linhas.Select(l => new LeadKanbanCardDto(
            l.Id, l.NomeOuRazaoSocial, l.Telefone, l.Telefone2, l.Email, l.Estado, podeVerOrigem ? l.Origem : null, l.Campanha,
            // Placa do lead; se ainda não tiver, a do veículo da oportunidade mais recente.
            l.Placa ?? l.Oportunidade?.Placa, l.TemSeguro, l.UtilidadeVeiculo, l.TipoIndicacao,
            l.Oportunidade?.Migracao ?? false, l.Oportunidade?.Indicacao, l.CriadoManualmente,
            l.ResponsavelId, l.ResponsavelNome, l.Tags,
            // "Sem contato" reflete se o lead tem ALGUM telefone cadastrado — some sozinho assim que
            // um telefone é preenchido.
            l.CriadoEm, l.UltimoContatoEm, string.IsNullOrWhiteSpace(l.Telefone) && string.IsNullOrWhiteSpace(l.Telefone2),
            l.Arquivado, l.RowVersion, l.ProdutoInteresse, l.ValorAdesao, l.ResponsavelFotoUrl, l.VeiculoAdicional,
            l.MotivoPerda, l.MotivoPerdaObservacao, l.VeiculoNaoAtendido)).ToList();
    }

    private (IQueryable<CrmLead> Query, bool PodeVerOrigem) Filtrar(LeadKanbanFilterRequest filtro, List<Guid>? visiveis)
    {
        var query = db.CrmLeads.AsNoTracking();

        if (!filtro.IncluirArquivados) query = query.Where(l => !l.Arquivado);
        if (filtro.CriadoManualmente.HasValue) query = query.Where(l => l.CriadoManualmente == filtro.CriadoManualmente.Value);

        if (visiveis is not null)
        {
            query = query.Where(l => visiveis.Contains(l.ResponsavelId ?? Guid.Empty));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();
            // Documento/telefone só entram quando a busca é um número (sem letras): a placa "EXN3C02" não pode virar "302" e casar com
            // qualquer telefone que tenha esses dígitos.
            var buscaDigitos = busca.Any(char.IsLetter) ? "" : DocumentValidation.SomenteDigitos(busca);
            // Placa: do cadastro do lead ou do veículo da venda, com ou sem hífen/espaço ("ABC-1D23" acha "ABC1D23").
            var buscaPlaca = new string(busca.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var buscarPlaca = buscaPlaca.Length >= 3;
            query = query.Where(l =>
                EF.Functions.ILike(l.NomeOuRazaoSocial, $"%{busca}%") ||
                (buscarPlaca && l.Placa != null && EF.Functions.ILike(l.Placa.Replace("-", "").Replace(" ", ""), $"%{buscaPlaca}%")) ||
                (buscarPlaca && l.Oportunidades.Any(o => o.Veiculo != null && o.Veiculo.Placa != null
                    && EF.Functions.ILike(o.Veiculo.Placa.Replace("-", "").Replace(" ", ""), $"%{buscaPlaca}%"))) ||
                (l.Email != null && EF.Functions.ILike(l.Email, $"%{busca}%")) ||
                (buscaDigitos != "" && l.DocumentoNormalizado != null && l.DocumentoNormalizado.Contains(buscaDigitos)) ||
                (buscaDigitos != "" && l.TelefoneNormalizado != null && l.TelefoneNormalizado.Contains(buscaDigitos)));
        }

        var responsaveis = Valores(filtro.ResponsavelId?.Select(id => (Guid?)id));
        if (responsaveis.Count > 0) query = query.Where(l => responsaveis.Contains(l.ResponsavelId));
        // Origem é informação só de administrador (visão total): para os demais, nem filtra nem aparece no cartão.
        var podeVerOrigem = visiveis is null;
        var origens = Valores(filtro.Origem);
        if (podeVerOrigem && origens.Count > 0) query = query.Where(l => origens.Contains(l.Origem));
        // Regional do lead, a do consultor responsável ou, sem nenhuma das duas, o rodízio geral (ver FiltroDeRegional).
        var regionais = Valores(filtro.Regional);
        if (regionais.Count > 0) query = FiltroDeRegional.Aplicar(query, db.CrmRegionais.AsNoTracking(), db.CrmGrupos.AsNoTracking(), regionais, distribuicao?.Value.RegionaisExclusivas);
        var grupos = Valores(filtro.GrupoId?.Select(id => (Guid?)id));
        if (grupos.Count > 0) query = query.Where(l => l.Responsavel != null && grupos.Contains(l.Responsavel.GrupoId));

        // Dados migrados em épocas diferentes gravaram TipoIndicacao com capitalização distinta
        // (ex.: "LEAD" vs "Lead") — as comparações abaixo ignoram maiúsculas/minúsculas.
        var categorias = Valores(filtro.Categoria);
        if (categorias.Count > 0)
        {
            var migracao = categorias.Contains("Migração");
            var indicacao = categorias.Contains("Indicação");
            var lead = categorias.Contains("Lead");
            query = query.Where(l =>
                // Pelo marcador da migração, não pelo texto da Origem (que vira a tag de campanha).
                (migracao && l.ConsentimentoOrigem == OrigemLead.MarcadorMigracaoNotion)
                // Indicação = qualquer tipo que não seja "Lead" (Indicação, Pessoal, Contemplando Sonhos...).
                || (indicacao && l.TipoIndicacao != null && l.TipoIndicacao.ToLower() != "lead")
                || (lead && l.TipoIndicacao != null && l.TipoIndicacao.ToLower() == "lead"));
        }

        var tipos = Valores(filtro.TipoIndicacao).Select(t => t!.ToLower()).ToList();
        if (tipos.Count > 0) query = query.Where(l => l.TipoIndicacao != null && tipos.Contains(l.TipoIndicacao.ToLower()));

        var fontes = Valores(filtro.Fonte);
        if (fontes.Count > 0)
        {
            var notion = fontes.Contains("Notion");
            var trafego = fontes.Contains("TrafegoPago");
            query = query.Where(l =>
                (notion && (l.ConsentimentoOrigem == OrigemLead.MarcadorMigracaoNotion
                    || l.ConsentimentoOrigem == OrigemLead.MarcadorSincronizacaoNotion))
                // Direto dos anúncios (Meta Lead Ads ou formulário do site), sem os do Notion — cards
                // migrados também podem trazer o ID do lead no Meta. Mesma regra de OrigemLead.VeioDoTrafegoPago.
                || (trafego && l.ConsentimentoOrigem != OrigemLead.MarcadorMigracaoNotion
                    && l.ConsentimentoOrigem != OrigemLead.MarcadorSincronizacaoNotion
                    && (l.MetaLeadId != null || l.ConsentimentoOrigem == OrigemLead.MarcadorFormularioSite)));
        }

        if (filtro.DataChegadaInicio is { } chegadaInicio)
        {
            var inicioUtc = chegadaInicio.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(l => l.CriadoEm >= inicioUtc);
        }
        if (filtro.DataChegadaFim is { } chegadaFim)
        {
            var fimUtc = chegadaFim.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(l => l.CriadoEm <= fimUtc);
        }
        if (filtro.DataVendaInicio is not null || filtro.DataVendaFim is not null)
        {
            // Pela data de ATIVAÇÃO da venda (sem ela, a data da venda em Brasília), na MESMA venda (ganha, não excluída): antes início e
            // fim eram checados em vendas diferentes e uma perda (que também grava data de fechamento) aparecia como venda.
            var per = PeriodoDeAtivacao.De(filtro.DataVendaInicio, filtro.DataVendaFim);
            query = query.Where(l => l.Oportunidades.Any(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho
                && ((o.AtivoEm != null && o.AtivoEm >= per.AtivacaoDe && o.AtivoEm < per.AtivacaoAte)
                    || (o.AtivoEm == null && o.DataEfetivaFechamento >= per.VendaDe && o.DataEfetivaFechamento <= per.VendaAte))));
        }

        return (query, podeVerOrigem);
    }

    /// <summary>Valores preenchidos de um filtro de múltipla escolha (ignora vazios e repetidos).</summary>
    private static List<T> Valores<T>(IEnumerable<T>? valores) =>
        (valores ?? []).Where(v => v is not null && (v is not string s || !string.IsNullOrWhiteSpace(s))).Distinct().ToList();
}
