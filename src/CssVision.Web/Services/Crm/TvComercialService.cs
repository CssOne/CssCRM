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

    /// <summary>
    /// O mesmo ranking do painel da TV (todas as regionais, qualquer que seja o escopo de quem pede) — usado no Portal do consultor, que
    /// espelha a TV.
    /// </summary>
    Task<TvRankingGeralDto> ObterRankingGeralAsync(int? mes, int? ano, CancellationToken ct);
}

/// <summary>
/// Painel comercial da TV: vendas (oportunidades em etapa Ganho, pela data da venda), adesão paga, conversão
/// (vendas ÷ leads que chegaram no mês), regionais, evolução diária e últimas vendas — tudo direto do CRM,
/// no escopo de quem está logado (Gestor regional vê só a própria regional).
/// </summary>
public sealed class TvComercialService(
    ApplicationDbContext db,
    IEquipeComercialService equipe,
    ICurrentUserService? currentUser = null,
    ITvNotionFonte? notion = null,
    IMemoryCache? cache = null,
    ICrmEventHub? eventos = null) : ITvComercialService
{
    private const string SemRegional = "Sem regional";

    /// <summary>Venda do painel, venha do CRM ou só do Notion.</summary>
    private sealed record VendaTv(Guid Id, Guid ResponsavelId, DateTimeOffset Data, decimal Adesao, DateTimeOffset Atualizada, string? Cliente, string? Placa, string? Origem, bool SoNoNotion = false);

    private sealed record PessoaTv(Guid Id, string NomeCompleto, string? FotoUrl, Guid? RegionalId, string? Regional);

    public Task<TvComercialDto> ObterAsync(int? mes, int? ano, CancellationToken ct) => ObterInternoAsync(mes, ano, escopoCompleto: false, ct);

    public async Task<TvRankingGeralDto> ObterRankingGeralAsync(int? mes, int? ano, CancellationToken ct)
    {
        var tv = await ObterInternoAsync(mes, ano, escopoCompleto: true, ct);
        return new TvRankingGeralDto(tv.Periodo, tv.RankingConsultores);
    }

    private async Task<TvComercialDto> ObterInternoAsync(int? mes, int? ano, bool escopoCompleto, CancellationToken ct)
    {
        var hoje = HorarioBrasilia.Hoje;
        var primeiro = new DateOnly(ano ?? hoje.Year, mes ?? hoje.Month, 1);
        if (primeiro > HorarioBrasilia.PrimeiroDiaDoMes(hoje)) primeiro = HorarioBrasilia.PrimeiroDiaDoMes(hoje);

        // O painel da TV mostra todas as regionais, mesmo para o administrador que oculta alguma nas demais telas.
        var visiveis = escopoCompleto ? null : await equipe.ObterVendedoresVisiveisAsync(ct, ignorarRegionaisOcultas: true);
        // O Notion tem cache próprio; a versão dele entra na chave para o painel refletir cada atualização.
        IReadOnlyList<TvNotionVenda> vendasNotion = notion is null ? [] : await notion.VendasAsync(primeiro, ct);
        var administrativo = notion is null ? null : await notion.AdministrativoAsync(primeiro, ct);
        return await RespostaEmCache.ObterAsync(cache, eventos, "tv-comercial",
            new { escopo = RespostaEmCache.Escopo(visiveis), primeiro, hoje, notion = notion?.Versao ?? 0 },
            () => CalcularAsync(primeiro, hoje, visiveis, vendasNotion, administrativo, ct));
    }

    private async Task<TvComercialDto> CalcularAsync(
        DateOnly primeiro, DateOnly hoje, List<Guid>? visiveis, IReadOnlyList<TvNotionVenda> vendasNotion, TvAdministrativoDto? administrativo, CancellationToken ct)
    {
        var inicioMes = HorarioBrasilia.Inicio(primeiro);
        // Só vendas do mês exibido e que já aconteceram: no mês corrente o limite é o fim de hoje. Venda com data futura (data digitada
        // errada) não conta nos totais, nos rankings nem na meta — antes entrava só porque a data ainda cai dentro do mês.
        var fimMes = HorarioBrasilia.Inicio(primeiro.AddMonths(1));
        var fimDeHoje = HorarioBrasilia.Inicio(hoje.AddDays(1));
        if (fimDeHoje < fimMes) fimMes = fimDeHoje;

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
            .Select(o => new VendaTv(
                o.Id, o.ResponsavelId, o.DataEfetivaFechamento!.Value, o.PagamentoAdesao ?? 0m,
                o.AtualizadoEm ?? o.CriadoEm, o.Lead.NomeOuRazaoSocial,
                o.Veiculo != null ? o.Veiculo.Placa : null, o.TipoIndicacao ?? o.Lead.TipoIndicacao, false))
            .ToListAsync(ct);

        // ----- vendas que existem só no Notion (base MG134), sem repetir as que o CRM já tem -----
        var regionais = await db.CrmRegionais.AsNoTracking().Where(r => r.Ativa).Select(r => new { r.Id, r.Nome }).ToListAsync(ct);
        // O Notion também é conferido aqui (a consulta dele filtra por data, mas o painel não confia só nisso).
        vendasNotion = vendasNotion.Where(v => v.DataVenda >= inicioMes && v.DataVenda < fimMes).ToList();
        var (vendasSoNoNotion, pessoasDoNotion) = await VendasSoNoNotionAsync(vendasNotion, vendas, visiveis, regionais.FirstOrDefault(r => r.Nome == "MG134")?.Id, ct);
        vendas = vendas.Concat(vendasSoNoNotion).ToList();

        // Leads do mês por consultor: recebidos e perdidos (conversão = vendas ÷ leads do mês, como na Visão geral).
        var leadsPorConsultor = await ContagensPorVendedor.ContarLeadsAsync(leadsQuery, ct);
        var perdidosPorConsultor = await ContagensPorVendedor.ContarLeadsAsync(
            leadsQuery.Where(l => l.Etapa != null && l.Etapa.Nome == NotionEtapaLead.Perdido), ct);

        var ids = vendas.Select(v => v.ResponsavelId).Concat(leadsPorConsultor.Keys).Distinct().ToList();
        var pessoas = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.AtuaNasVendas && !u.NomeCompleto.StartsWith(NotionPageExtensions.PrefixoNomeProvisorio))
            .Select(u => new PessoaTv(u.Id, u.NomeCompleto, u.FotoUrl, u.RegionalId, u.Regional != null ? u.Regional.Nome : null))
            .ToDictionaryAsync(u => u.Id, ct);
        foreach (var (id, pessoa) in pessoasDoNotion) pessoas.TryAdd(id, pessoa);
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

        var vendasDoMes = vendas.OrderByDescending(v => v.Data)
            .Select(v =>
            {
                var p = pessoas[v.ResponsavelId];
                return new TvVendaMesDto(v.Id, v.ResponsavelId, p.NomeCompleto, p.FotoUrl, p.Regional ?? SemRegional, v.Cliente, v.Placa, v.Origem, v.Adesao, v.Data);
            }).ToList();

        return new TvComercialDto(new TvPeriodoDto(primeiro.Month, primeiro.Year), resumo, rankingVendas, rankingAdesao, rankingConversao,
            rankingRegionais, evolucao, ultimas, DateTimeOffset.UtcNow, administrativo, vendas.Count(v => v.SoNoNotion), vendasDoMes);
    }

    /// <summary>
    /// Vendas do Notion (MG134) que o CRM não tem. Uma venda nunca conta duas vezes: sai a que já está no CRM pelo mesmo card do Notion
    /// (<c>NotionPageId</c>) ou pela mesma placa no mês, e a que se repete entre as bases do Notion. Quem vê só uma regional só recebe as do
    /// MG134 se for dessa regional.
    /// </summary>
    private async Task<(List<VendaTv> Vendas, Dictionary<Guid, PessoaTv> Pessoas)> VendasSoNoNotionAsync(
        IReadOnlyList<TvNotionVenda> doNotion, List<VendaTv> doCrm, List<Guid>? visiveis, Guid? regionalMg134Id, CancellationToken ct)
    {
        var vazio = (new List<VendaTv>(), new Dictionary<Guid, PessoaTv>());
        if (doNotion.Count == 0) return vazio;

        if (visiveis is not null)
        {
            // Escopo regional: só quem é do MG134 vê as vendas do MG134.
            var regionalDoUsuario = currentUser is null ? null
                : await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.RegionalId).FirstOrDefaultAsync(ct);
            if (regionalMg134Id is null || regionalDoUsuario != regionalMg134Id) return vazio;
        }

        // 1) mesmo card do Notion já importado e ativo no CRM
        var paginas = doNotion.Select(v => v.PageId).ToList();
        var jaNoCrm = (await db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.NotionPageId != null && paginas.Contains(o.NotionPageId))
            .Select(o => o.NotionPageId!).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 2) mesma placa de uma venda do CRM no mês (a consultora pode ter registrado nos dois lugares)
        var placasDoCrm = doCrm.Select(v => TvNotionFonte.Normalizar(v.Placa)).Where(p => p is not null).ToHashSet();

        // Consultor do Notion → usuário do CRM (id do Notion ou e-mail); sem correspondência, uma pessoa só do painel.
        var notionIds = doNotion.Select(v => v.ConsultorNotionId).Where(i => i is not null).Distinct().ToList();
        var emails = doNotion.Select(v => v.ConsultorEmail?.Trim().ToLowerInvariant()).Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
        var usuarios = await db.Users.AsNoTracking()
            .Where(u => (u.NotionUserId != null && notionIds.Contains(u.NotionUserId)) || (u.Email != null && emails.Contains(u.Email.ToLower())))
            .Select(u => new { u.Id, u.NotionUserId, u.Email })
            .ToListAsync(ct);

        var vendas = new List<VendaTv>();
        var pessoas = new Dictionary<Guid, PessoaTv>();
        var placasVistas = new HashSet<string>();
        foreach (var n in doNotion.OrderBy(v => v.DataVenda))
        {
            if (jaNoCrm.Contains(n.PageId)) continue;
            if (n.Placa is not null && (placasDoCrm.Contains(n.Placa) || !placasVistas.Add(n.Placa))) continue;

            var usuario = usuarios.FirstOrDefault(u => n.ConsultorNotionId is not null && u.NotionUserId == n.ConsultorNotionId)
                ?? usuarios.FirstOrDefault(u => n.ConsultorEmail is not null && string.Equals(u.Email, n.ConsultorEmail, StringComparison.OrdinalIgnoreCase));
            var consultorId = usuario?.Id ?? IdDaPessoaDoNotion(n.ConsultorNotionId ?? n.Consultor);
            if (usuario is null) pessoas.TryAdd(consultorId, new PessoaTv(consultorId, n.Consultor, n.FotoUrl, regionalMg134Id, n.Regional));

            vendas.Add(new VendaTv(Guid.TryParse(n.PageId, out var id) ? id : Guid.NewGuid(), consultorId, n.DataVenda, n.Adesao, n.AtualizadaEm, n.Cliente, n.Placa, null, true));
        }
        return (vendas, pessoas);
    }

    /// <summary>Id estável para quem aparece só no Notion (o mesmo consultor cai sempre na mesma linha do ranking).</summary>
    private static Guid IdDaPessoaDoNotion(string chave) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("tv-notion:" + chave.Trim().ToLowerInvariant())));
}
