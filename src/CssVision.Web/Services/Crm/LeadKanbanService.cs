using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Quadro de leads. A base tem dezenas de milhares de leads, então nada é carregado inteiro: cada
/// coluna traz só os cartões mais recentes (<see cref="LeadKanbanFilterRequest.CartoesPorColuna"/>) e
/// o total dela, contado no banco; "Ver mais" busca a próxima página de uma coluna
/// (<see cref="ObterCartoesAsync"/>). Os cartões são projetados direto em SQL — só os campos exibidos.
/// </summary>
public sealed class LeadKanbanService(ApplicationDbContext db, IEquipeComercialService equipe) : ILeadKanbanService
{
    private const int MaxCartoesPorPagina = 200;

    public async Task<LeadKanbanBoardDto> ObterBoardAsync(LeadKanbanFilterRequest filtro, CancellationToken ct)
    {
        var etapas = await db.CrmLeadStages.AsNoTracking()
            .Where(s => s.Ativa)
            .OrderBy(s => s.Ordem)
            .ToListAsync(ct);

        var (query, podeVerOrigem) = await FiltrarAsync(filtro, ct);
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
            var total = Total(etapa.Id);
            var cartoes = total == 0 ? [] : await PaginaAsync(query, etapa.Id, 0, porPagina, podeVerOrigem, ct);
            colunas.Add(new LeadKanbanColumnDto(
                new LeadStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Cor, etapa.Fechada, etapa.Ativa), cartoes, total));
        }

        return new LeadKanbanBoardDto(colunas);
    }

    public async Task<IReadOnlyList<LeadKanbanCardDto>> ObterCartoesAsync(LeadKanbanColunaRequest request, CancellationToken ct)
    {
        var (query, podeVerOrigem) = await FiltrarAsync(request, ct);
        var quantidade = Math.Clamp(request.Quantidade, 1, MaxCartoesPorPagina);
        return await PaginaAsync(query, request.EtapaId, Math.Max(0, request.Pular), quantidade, podeVerOrigem, ct);
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
                Tags = l.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
                l.CriadoEm, l.UltimoContatoEm, l.Arquivado, l.RowVersion, l.ProdutoInteresse, l.ValorAdesao,
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
            l.Arquivado, l.RowVersion, l.ProdutoInteresse, l.ValorAdesao)).ToList();
    }

    private async Task<(IQueryable<CrmLead> Query, bool PodeVerOrigem)> FiltrarAsync(LeadKanbanFilterRequest filtro, CancellationToken ct)
    {
        var query = db.CrmLeads.AsNoTracking();

        if (!filtro.IncluirArquivados) query = query.Where(l => !l.Arquivado);
        if (filtro.CriadoManualmente.HasValue) query = query.Where(l => l.CriadoManualmente == filtro.CriadoManualmente.Value);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null)
        {
            query = query.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));
        }

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

        var responsaveis = Valores(filtro.ResponsavelId?.Select(id => (Guid?)id));
        if (responsaveis.Count > 0) query = query.Where(l => responsaveis.Contains(l.ResponsavelId));
        // Origem é informação só de administrador (visão total): para os demais, nem filtra nem aparece no cartão.
        var podeVerOrigem = visiveis is null;
        var origens = Valores(filtro.Origem);
        if (podeVerOrigem && origens.Count > 0) query = query.Where(l => origens.Contains(l.Origem));
        var regionais = Valores(filtro.Regional);
        if (regionais.Count > 0) query = query.Where(l => regionais.Contains(l.Regional));

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
        if (filtro.DataVendaInicio is { } vendaInicio)
        {
            var inicioUtc = vendaInicio.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(l => l.Oportunidades.Any(o => o.DataEfetivaFechamento >= inicioUtc));
        }
        if (filtro.DataVendaFim is { } vendaFim)
        {
            var fimUtc = vendaFim.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(l => l.Oportunidades.Any(o => o.DataEfetivaFechamento <= fimUtc));
        }

        return (query, podeVerOrigem);
    }

    /// <summary>Valores preenchidos de um filtro de múltipla escolha (ignora vazios e repetidos).</summary>
    private static List<T> Valores<T>(IEnumerable<T>? valores) =>
        (valores ?? []).Where(v => v is not null && (v is not string s || !string.IsNullOrWhiteSpace(s))).Distinct().ToList();
}
