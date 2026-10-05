using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CssVision.Web.Services.Crm;

public interface ITvComercialService
{
    Task<TvComercialDto> ObterAsync(int? mes, int? ano, CancellationToken ct);
}

/// <summary>
/// Painel comercial da TV: vendas (oportunidades em etapa Ganho, pela data da venda), adesão paga, conversão
/// (vendas ÷ leads que chegaram no mês), regionais, evolução diária e últimas vendas — tudo direto do CRM,
/// no escopo de quem está logado (Gestor regional vê só a própria regional).
/// </summary>
public sealed class TvComercialService(
    ApplicationDbContext db,
    IEquipeComercialService equipe,
    IMemoryCache? cache = null,
    ICrmEventHub? eventos = null) : ITvComercialService
{
    private const string SemRegional = "Sem regional";

    public async Task<TvComercialDto> ObterAsync(int? mes, int? ano, CancellationToken ct)
    {
        var hoje = HorarioBrasilia.Hoje;
        var primeiro = new DateOnly(ano ?? hoje.Year, mes ?? hoje.Month, 1);
        if (primeiro > HorarioBrasilia.PrimeiroDiaDoMes(hoje)) primeiro = HorarioBrasilia.PrimeiroDiaDoMes(hoje);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        return await RespostaEmCache.ObterAsync(cache, eventos, "tv-comercial",
            new { escopo = RespostaEmCache.Escopo(visiveis), primeiro, hoje }, () => CalcularAsync(primeiro, hoje, visiveis, ct));
    }

    private async Task<TvComercialDto> CalcularAsync(DateOnly primeiro, DateOnly hoje, List<Guid>? visiveis, CancellationToken ct)
    {
        var inicioMes = HorarioBrasilia.Inicio(primeiro);
        var fimMes = HorarioBrasilia.Inicio(primeiro.AddMonths(1));

        var vendasQuery = db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho
                && o.DataEfetivaFechamento >= inicioMes && o.DataEfetivaFechamento < fimMes);
        var leadsQuery = db.CrmLeads.AsNoTracking().Where(l => !l.Arquivado && l.CriadoEm >= inicioMes && l.CriadoEm < fimMes);
        if (visiveis is not null)
        {
            vendasQuery = vendasQuery.Where(o => visiveis.Contains(o.ResponsavelId));
            leadsQuery = leadsQuery.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));
        }

        var vendas = await vendasQuery
            .Select(o => new
            {
                o.Id, o.ResponsavelId, Data = o.DataEfetivaFechamento!.Value, Adesao = o.PagamentoAdesao ?? 0m,
                Atualizada = o.AtualizadoEm ?? o.CriadoEm, Cliente = o.Lead.NomeOuRazaoSocial,
                Placa = o.Veiculo != null ? o.Veiculo.Placa : null, Origem = o.TipoIndicacao ?? o.Lead.TipoIndicacao,
            })
            .ToListAsync(ct);

        // Leads do mês por consultor: recebidos e perdidos (conversão = vendas ÷ leads do mês, como na Visão geral).
        var leadsPorConsultor = await ContagensPorVendedor.ContarLeadsAsync(leadsQuery, ct);
        var perdidosPorConsultor = await ContagensPorVendedor.ContarLeadsAsync(
            leadsQuery.Where(l => l.Etapa != null && l.Etapa.Nome == NotionEtapaLead.Perdido), ct);

        var ids = vendas.Select(v => v.ResponsavelId).Concat(leadsPorConsultor.Keys).Distinct().ToList();
        var pessoas = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && !u.NomeCompleto.StartsWith(NotionPageExtensions.PrefixoNomeProvisorio))
            .Select(u => new { u.Id, u.NomeCompleto, u.FotoUrl, u.RegionalId, Regional = u.Regional != null ? u.Regional.Nome : null })
            .ToDictionaryAsync(u => u.Id, ct);
        vendas = vendas.Where(v => pessoas.ContainsKey(v.ResponsavelId)).ToList();

        var metasIndividuais = await db.CrmSalesGoals.AsNoTracking()
            .Where(g => g.MesReferencia == primeiro && ids.Contains(g.VendedorId))
            .ToDictionaryAsync(g => g.VendedorId, g => g.MetaQuantidadeVendas, ct);
        var metasRegionaisQuery = db.CrmRegionalGoals.AsNoTracking().Where(g => g.MesReferencia == primeiro);
        var metasRegionais = await metasRegionaisQuery.ToDictionaryAsync(g => g.RegionalId, g => g.MetaQuantidadeVendas, ct);

        // ----- consultores (vendas e adesão) -----
        var porConsultor = vendas.GroupBy(v => v.ResponsavelId)
            .Select(g => new { Id = g.Key, Qtd = g.Count(), Adesao = g.Sum(v => v.Adesao) })
            .ToList();
        TvRankingDto Linha(int posicao, Guid id, int qtd, decimal adesao)
        {
            var p = pessoas[id];
            int? meta = metasIndividuais.TryGetValue(id, out var m) && m > 0 ? m : null;
            return new TvRankingDto(posicao, id, p.NomeCompleto, p.FotoUrl, p.Regional ?? SemRegional, qtd, adesao, meta,
                meta is null ? null : Math.Round(100m * qtd / meta.Value, 1));
        }
        var rankingVendas = porConsultor.OrderByDescending(c => c.Qtd).ThenByDescending(c => c.Adesao)
            .Select((c, i) => Linha(i + 1, c.Id, c.Qtd, c.Adesao)).ToList();
        var rankingAdesao = porConsultor.OrderByDescending(c => c.Adesao).ThenByDescending(c => c.Qtd)
            .Select((c, i) => Linha(i + 1, c.Id, c.Qtd, c.Adesao)).ToList();

        // ----- conversão -----
        var vendasPorConsultor = porConsultor.ToDictionary(c => c.Id, c => c.Qtd);
        var rankingConversao = leadsPorConsultor.Where(l => l.Value > 0 && pessoas.ContainsKey(l.Key))
            .Select(l => new
            {
                Id = l.Key, Leads = l.Value, Vendas = vendasPorConsultor.GetValueOrDefault(l.Key),
                Perdidos = perdidosPorConsultor.GetValueOrDefault(l.Key),
            })
            .OrderByDescending(c => ContagensPorVendedor.TaxaConversaoLeads(c.Vendas, c.Leads)).ThenByDescending(c => c.Vendas).ThenByDescending(c => c.Leads)
            .Select((c, i) =>
            {
                var p = pessoas[c.Id];
                return new TvConversaoDto(i + 1, c.Id, p.NomeCompleto, p.FotoUrl, p.Regional ?? SemRegional, c.Leads, c.Vendas, c.Perdidos,
                    ContagensPorVendedor.TaxaConversaoLeads(c.Vendas, c.Leads));
            }).ToList();

        // ----- regionais -----
        var totalAdesao = vendas.Sum(v => v.Adesao);
        var regionais = await db.CrmRegionais.AsNoTracking().Where(r => r.Ativa).Select(r => new { r.Id, r.Nome }).ToListAsync(ct);
        var porRegional = vendas.GroupBy(v => pessoas[v.ResponsavelId].RegionalId)
            .ToDictionary(g => g.Key ?? Guid.Empty, g => new { Qtd = g.Count(), Valor = g.Sum(v => v.Adesao) });
        var rankingRegionais = regionais
            .Select(r =>
            {
                porRegional.TryGetValue(r.Id, out var s);
                int? meta = metasRegionais.TryGetValue(r.Id, out var m) && m > 0 ? m : null;
                return new { r.Id, r.Nome, Qtd = s?.Qtd ?? 0, Valor = s?.Valor ?? 0m, Meta = meta };
            })
            // Regional sem venda, sem meta e fora do escopo do usuário não entra.
            .Where(r => r.Qtd > 0 || r.Meta is not null)
            .OrderByDescending(r => r.Qtd).ThenByDescending(r => r.Valor)
            .Select((r, i) => new TvRegionalDto(i + 1, r.Id, r.Nome, r.Qtd, r.Valor,
                totalAdesao == 0 ? 0 : Math.Round(100m * r.Valor / totalAdesao, 1), r.Meta,
                r.Meta is null ? null : Math.Round(100m * r.Qtd / r.Meta.Value, 1)))
            .ToList();

        // ----- evolução diária -----
        var ultimoDia = primeiro.Year == hoje.Year && primeiro.Month == hoje.Month ? hoje : primeiro.AddMonths(1).AddDays(-1);
        var porDia = vendas.GroupBy(v => HorarioBrasilia.Dia(v.Data)).ToDictionary(g => g.Key, g => new { Qtd = g.Count(), Valor = g.Sum(v => v.Adesao) });
        var evolucao = new List<TvEvolucaoDto>();
        var acumuladoQtd = 0;
        var acumuladoValor = 0m;
        for (var dia = primeiro; dia <= ultimoDia; dia = dia.AddDays(1))
        {
            porDia.TryGetValue(dia, out var d);
            acumuladoQtd += d?.Qtd ?? 0;
            acumuladoValor += d?.Valor ?? 0m;
            evolucao.Add(new TvEvolucaoDto(dia, d?.Qtd ?? 0, d?.Valor ?? 0m, acumuladoQtd, acumuladoValor));
        }

        var vendasHoje = vendas.Where(v => HorarioBrasilia.Dia(v.Data) == hoje).ToList();
        var metaTotal = metasIndividuais.Values.Sum() + metasRegionais.Where(m => visiveis is null || regionais.Any(r => r.Id == m.Key)).Sum(m => m.Value);
        var resumo = new TvResumoDto(vendasHoje.Count, vendas.Count, vendasHoje.Sum(v => v.Adesao), totalAdesao,
            metaTotal > 0 ? Math.Round(100m * vendas.Count / metaTotal, 1) : null);

        var ultimas = vendas.OrderByDescending(v => v.Atualizada).Take(10)
            .Select(v =>
            {
                var p = pessoas[v.ResponsavelId];
                return new TvVendaDto(v.Id, p.NomeCompleto, p.FotoUrl, p.Regional ?? SemRegional, v.Cliente, v.Placa, v.Origem, v.Adesao, v.Data, v.Atualizada);
            }).ToList();

        return new TvComercialDto(new TvPeriodoDto(primeiro.Month, primeiro.Year), resumo, rankingVendas, rankingAdesao, rankingConversao,
            rankingRegionais, evolucao, ultimas, DateTimeOffset.UtcNow);
    }
}
