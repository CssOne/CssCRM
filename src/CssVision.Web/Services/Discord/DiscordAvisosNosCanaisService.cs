using System.Globalization;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

public interface IDiscordAvisosNosCanaisService : IVendaPublicador
{
    Task<DiscordAvisosCanaisDto> ObterConfiguracaoAsync(CancellationToken ct);

    Task<DiscordAvisosCanaisDto> DefinirConfiguracaoAsync(DiscordAvisosCanaisDto configuracao, CancellationToken ct);

    /// <summary>
    /// Resumo diário dos leads parados, um por regional, no canal dela. Só roda uma vez por dia, depois das 9h30 (Brasília), e só se estiver
    /// ligado. Devolve quantos resumos foram publicados.
    /// </summary>
    Task<int> PublicarLeadsParadosDoDiaAsync(CancellationToken ct);

    /// <summary>
    /// Resumo do dia de cada regional que vendeu, no canal dela: vendas, adesão, melhores consultores e a meta do mês. Só depois das 18h (Brasília),
    /// uma vez por dia e só se estiver ligado. Devolve quantos resumos foram publicados.
    /// </summary>
    Task<int> PublicarResumoDoDiaAsync(CancellationToken ct);

    /// <summary>
    /// Avisa no canal da regional cada lead de anúncio que acabou de chegar para um consultor dela (com o link para abrir no CRM; sem nome nem
    /// telefone do cliente). Só se estiver ligado. Devolve quantos avisos foram publicados.
    /// </summary>
    Task<int> PublicarLeadsNovosAsync(CancellationToken ct);
}

/// <summary>
/// Avisos automáticos do CRM nos canais do Discord (regional): venda fechada, meta da regional batida e resumo diário de leads parados.
/// Tudo vem <b>desligado</b> e o administrador liga cada um na tela do Discord. Os avisos não levam dados de clientes (nem nome, nem telefone):
/// só o consultor, a regional e números. Falha do Discord nunca atrapalha o CRM (a venda é salva antes, e o aviso é um "extra").
/// </summary>
public sealed class DiscordAvisosNosCanaisService(
    ApplicationDbContext db,
    IDiscordGuildApi api,
    IOptions<DiscordOptions> options,
    ILogger<DiscordAvisosNosCanaisService> logger,
    TimeProvider? relogio = null) : IDiscordAvisosNosCanaisService
{
    internal const string ChaveVenda = "discord:avisos:venda";
    internal const string ChaveMeta = "discord:avisos:meta";
    internal const string ChaveParados = "discord:avisos:parados";
    internal const string ChaveUltimoResumo = "discord:avisos:parados:ultimo";
    internal const string ChaveResumoDiario = "discord:avisos:resumo";
    internal const string ChaveUltimoResumoDiario = "discord:avisos:resumo:ultimo";
    internal const string ChaveLeadNovo = "discord:avisos:leadnovo";
    internal const string ChaveLeadNovoAte = "discord:avisos:leadnovo:ate";
    private const int MaximoDeLeadsPorCiclo = 10;
    internal const int DiasParado = 5;
    internal const int DiasJanelaParados = 60;

    /// <summary>Venda lançada com data de fechamento mais antiga que isto é correção de cadastro, não "acabou de fechar": não vira festa no canal.</summary>
    private static readonly TimeSpan IdadeMaximaDaVenda = TimeSpan.FromDays(3);

    private static readonly TimeSpan HoraDoResumo = new(9, 30, 0);
    private static readonly TimeSpan HoraDoResumoDiario = new(18, 0, 0);

    // Cores da barra lateral dos cartões.
    private const int CorVenda = 0x2ECC71;
    private const int CorMeta = 0xF1C40F;
    private const int CorAlerta = 0xF39C12;
    private const int CorLead = 0x3498DB;
    private const int CorResumo = 0x9B59B6;

    private DateTimeOffset Agora => (relogio ?? TimeProvider.System).GetUtcNow();

    public async Task<DiscordAvisosCanaisDto> ObterConfiguracaoAsync(CancellationToken ct)
    {
        var chaves = new[] { ChaveVenda, ChaveMeta, ChaveParados, ChaveResumoDiario, ChaveLeadNovo };
        var valores = await db.CrmParametros.AsNoTracking().Where(p => chaves.Contains(p.Chave)).ToDictionaryAsync(p => p.Chave, p => p.Valor, ct);
        bool Ligado(string chave) => valores.TryGetValue(chave, out var v) && v == "1";
        return new DiscordAvisosCanaisDto(Ligado(ChaveVenda), Ligado(ChaveMeta), Ligado(ChaveParados), Ligado(ChaveResumoDiario), Ligado(ChaveLeadNovo));
    }

    public async Task<DiscordAvisosCanaisDto> DefinirConfiguracaoAsync(DiscordAvisosCanaisDto configuracao, CancellationToken ct)
    {
        await GravarAsync(ChaveVenda, configuracao.Venda ? "1" : "0", ct);
        await GravarAsync(ChaveMeta, configuracao.MetaBatida ? "1" : "0", ct);
        await GravarAsync(ChaveParados, configuracao.LeadsParados ? "1" : "0", ct);
        await GravarAsync(ChaveResumoDiario, configuracao.ResumoDiario ? "1" : "0", ct);
        await GravarAsync(ChaveLeadNovo, configuracao.LeadNovo ? "1" : "0", ct);
        // Ao ligar o aviso de lead novo, só valem os leads daqui para frente: nada de despejar o que chegou antes.
        if (configuracao.LeadNovo && !await db.CrmParametros.AnyAsync(p => p.Chave == ChaveLeadNovoAte, ct)) await GravarAsync(ChaveLeadNovoAte, Agora.ToString("O", CultureInfo.InvariantCulture), ct);
        return await ObterConfiguracaoAsync(ct);
    }

    public async Task PublicarVendaAsync(Guid oportunidadeId, CancellationToken ct)
    {
        if (!options.Value.Configurado) return;

        var configuracao = await ObterConfiguracaoAsync(ct);
        if (!configuracao.Venda && !configuracao.MetaBatida) return;

        var venda = await db.CrmOpportunities.AsNoTracking().Where(o => o.Id == oportunidadeId)
            .Select(o => new { o.Id, o.PagamentoAdesao, o.DataEfetivaFechamento, Consultor = o.Responsavel.NomeCompleto, o.Responsavel.RegionalId, Regional = o.Responsavel.Regional != null ? o.Responsavel.Regional.Nome : null })
            .FirstOrDefaultAsync(ct);
        if (venda is null) return;
        if (venda.DataEfetivaFechamento is { } fechamento && Agora - fechamento > IdadeMaximaDaVenda) return;

        var canal = await CanalDaRegionalAsync(venda.RegionalId, ct);
        if (canal is null) return;

        try
        {
            if (configuracao.Venda)
            {
                var campos = new List<DiscordCampo>();
                if (venda.Regional is not null) campos.Add(new DiscordCampo("Regional", venda.Regional));
                if (venda.PagamentoAdesao is > 0) campos.Add(new DiscordCampo("Adesão", Moeda(venda.PagamentoAdesao.Value)));
                await api.PublicarCartaoAsync(canal, new DiscordCartao("🎉 Venda fechada!", $"**{venda.Consultor}** fechou uma venda.", CorVenda, campos, "CRM CSS Brasil"), ct);
            }

            if (configuracao.MetaBatida && venda.RegionalId is { } regionalId) await PublicarMetaSeBatidaAsync(regionalId, venda.Regional ?? "regional", venda.PagamentoAdesao ?? 0m, canal, ct);
        }
        catch (DiscordApiException ex)
        {
            logger.LogWarning(ex, "Não foi possível publicar o aviso da venda {VendaId} no Discord.", oportunidadeId);
        }
    }

    /// <summary>Publica "meta batida" quando ESTA venda foi a que fez a regional alcançar a meta do mês (e só uma vez por regional e mês).</summary>
    private async Task PublicarMetaSeBatidaAsync(Guid regionalId, string nomeRegional, decimal valorDestaVenda, string canal, CancellationToken ct)
    {
        var mes = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Dia(Agora));
        var meta = await db.CrmRegionalGoals.AsNoTracking().FirstOrDefaultAsync(g => g.RegionalId == regionalId && g.MesReferencia == mes, ct);
        if (meta is null || (meta.MetaQuantidadeVendas <= 0 && meta.MetaValor is not > 0)) return;

        var marca = $"discord:avisos:meta:{regionalId}:{mes:yyyy-MM}";
        if (await db.CrmParametros.AnyAsync(p => p.Chave == marca, ct)) return;

        var inicio = HorarioBrasilia.Inicio(mes);
        var fim = HorarioBrasilia.Inicio(mes.AddMonths(1));
        var realizado = await db.CrmOpportunities.AsNoTracking()
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.DataEfetivaFechamento >= inicio && o.DataEfetivaFechamento < fim
                        && o.Responsavel.RegionalId == regionalId)
            .GroupBy(_ => 1)
            .Select(g => new { Valor = g.Sum(o => o.PagamentoAdesao) ?? 0m, Quantidade = g.Count() })
            .FirstOrDefaultAsync(ct);
        if (realizado is null) return;

        bool Batida(decimal valor, int quantidade) =>
            (meta.MetaQuantidadeVendas <= 0 || quantidade >= meta.MetaQuantidadeVendas) && (meta.MetaValor is not > 0 || valor >= meta.MetaValor);

        // Só quando esta venda virou a chave: antes dela a meta ainda não estava batida.
        if (!Batida(realizado.Valor, realizado.Quantidade) || Batida(realizado.Valor - valorDestaVenda, realizado.Quantidade - 1)) return;

        var campos = new List<DiscordCampo>();
        if (meta.MetaValor is > 0) campos.Add(new DiscordCampo("Valor", $"{Moeda(realizado.Valor)} de {Moeda(meta.MetaValor.Value)}"));
        if (meta.MetaQuantidadeVendas > 0) campos.Add(new DiscordCampo("Vendas", $"{realizado.Quantidade} de {meta.MetaQuantidadeVendas}"));
        await api.PublicarCartaoAsync(canal, new DiscordCartao("🏆 Meta do mês batida!", $"**A regional {nomeRegional} bateu a meta do mês!** Parabéns, time!", CorMeta, campos, "CRM CSS Brasil"), ct);

        db.CrmParametros.Add(new CrmParametro { Chave = marca, Valor = Agora.ToString("O", CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> PublicarLeadsParadosDoDiaAsync(CancellationToken ct)
    {
        if (!options.Value.Configurado) return 0;
        if (!(await ObterConfiguracaoAsync(ct)).LeadsParados) return 0;

        var agora = Agora;
        var hoje = HorarioBrasilia.Dia(agora);
        var horaLocal = agora.UtcDateTime.AddHours(-3).TimeOfDay;
        if (horaLocal < HoraDoResumo) return 0;

        var ultimo = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == ChaveUltimoResumo, ct);
        if (ultimo?.Valor == hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) return 0; // já saiu hoje

        // Marca antes de publicar: se o Discord falhar no meio, não repete tudo a cada ciclo (amanhã tenta de novo).
        if (ultimo is null) db.CrmParametros.Add(ultimo = new CrmParametro { Chave = ChaveUltimoResumo, Valor = "" });
        ultimo.Valor = hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct);

        var limite = agora.AddDays(-DiasParado);
        var janela = agora.AddDays(-DiasJanelaParados);
        var porRegional = await db.CrmLeads.AsNoTracking()
            .Where(OrigemLead.VeioDoTrafegoPago)
            .Where(l => !l.Arquivado && (l.Etapa == null || !l.Etapa.Fechada))
            .Where(l => l.Responsavel != null && l.Responsavel.Ativo && l.Responsavel.RegionalId != null)
            .Where(l => l.CriadoEm >= janela && l.CriadoEm < limite)
            .Where(l => l.UltimoContatoEm == null || l.UltimoContatoEm < limite)
            .Where(l => l.AtualizadoEm == null || l.AtualizadoEm < limite)
            .Where(l => l.ResponsavelAtribuidoEm == null || l.ResponsavelAtribuidoEm < limite)
            .GroupBy(l => l.Responsavel!.RegionalId!.Value)
            .Select(g => new { RegionalId = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var nomes = await db.CrmRegionais.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        var publicados = 0;
        foreach (var r in porRegional.Where(r => r.Total > 0))
        {
            var canal = await CanalDaRegionalAsync(r.RegionalId, ct);
            if (canal is null) continue;
            try
            {
                var leads = r.Total == 1 ? "1 lead" : $"{r.Total} leads";
                await api.PublicarCartaoAsync(canal,
                    new DiscordCartao("⚠️ Leads parados", $"**{nomes.GetValueOrDefault(r.RegionalId, "Regional")}:** {leads} de anúncio {(r.Total == 1 ? "está parado" : "estão parados")} há mais de {DiasParado} dias, sem contato. Vale olhar o painel de alertas.", CorAlerta, null, "CRM CSS Brasil"), ct);
                publicados++;
            }
            catch (DiscordApiException ex)
            {
                logger.LogWarning(ex, "Não foi possível publicar o resumo de leads parados da regional {RegionalId} no Discord.", r.RegionalId);
            }
        }

        return publicados;
    }

    public async Task<int> PublicarResumoDoDiaAsync(CancellationToken ct)
    {
        if (!options.Value.Configurado) return 0;
        if (!(await ObterConfiguracaoAsync(ct)).ResumoDiario) return 0;

        var agora = Agora;
        var hoje = HorarioBrasilia.Dia(agora);
        if (agora.UtcDateTime.AddHours(-3).TimeOfDay < HoraDoResumoDiario) return 0;

        var diaTexto = hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ultimo = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == ChaveUltimoResumoDiario, ct);
        if (ultimo?.Valor == diaTexto) return 0; // já saiu hoje

        // Marca antes de publicar (como o resumo de leads parados): se o Discord falhar no meio, não repete tudo a cada ciclo.
        if (ultimo is null) db.CrmParametros.Add(ultimo = new CrmParametro { Chave = ChaveUltimoResumoDiario, Valor = "" });
        ultimo.Valor = diaTexto;
        await db.SaveChangesAsync(ct);

        // Mesma regra de "venda do dia" do painel da TV e dos filtros: pela data de ativação; sem ela, pela data da venda.
        var per = PeriodoDeAtivacao.De(hoje, hoje);
        var vendas = await db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.Responsavel.RegionalId != null
                && ((o.AtivoEm != null && o.AtivoEm >= per.AtivacaoDe && o.AtivoEm < per.AtivacaoAte)
                    || (o.AtivoEm == null && o.DataEfetivaFechamento >= per.VendaDe && o.DataEfetivaFechamento <= per.VendaAte)))
            .Select(o => new { RegionalId = o.Responsavel.RegionalId!.Value, Consultor = o.Responsavel.NomeCompleto, Valor = o.PagamentoAdesao ?? 0m })
            .ToListAsync(ct);
        if (vendas.Count == 0) return 0;

        var nomes = await db.CrmRegionais.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        var mes = HorarioBrasilia.PrimeiroDiaDoMes(hoje);
        var publicados = 0;
        foreach (var regional in vendas.GroupBy(v => v.RegionalId))
        {
            var canal = await CanalDaRegionalAsync(regional.Key, ct);
            if (canal is null) continue;

            var lista = regional.ToList();
            var medalhas = new[] { "🥇", "🥈", "🥉" };
            var ranking = lista.GroupBy(v => v.Consultor).Select(g => new { Nome = g.Key, Vendas = g.Count(), Valor = g.Sum(v => v.Valor) })
                .OrderByDescending(g => g.Vendas).ThenByDescending(g => g.Valor).ThenBy(g => g.Nome, StringComparer.OrdinalIgnoreCase).Take(3).ToList();
            var campos = new List<DiscordCampo>
            {
                new("Vendas", lista.Count.ToString(CultureInfo.InvariantCulture)),
                new("Adesão", Moeda(lista.Sum(v => v.Valor))),
                new("Melhores do dia", string.Join("\n", ranking.Select((r, i) => $"{medalhas[i]} {r.Nome} — {(r.Vendas == 1 ? "1 venda" : $"{r.Vendas} vendas")}")), Lado: false),
            };

            var meta = await db.CrmRegionalGoals.AsNoTracking().FirstOrDefaultAsync(g => g.RegionalId == regional.Key && g.MesReferencia == mes, ct);
            if (meta is not null && (meta.MetaQuantidadeVendas > 0 || meta.MetaValor is > 0))
            {
                var inicio = HorarioBrasilia.Inicio(mes);
                var fim = HorarioBrasilia.Inicio(mes.AddMonths(1));
                var mesAteAgora = await db.CrmOpportunities.AsNoTracking()
                    .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.Responsavel.RegionalId == regional.Key && o.DataEfetivaFechamento >= inicio && o.DataEfetivaFechamento < fim)
                    .GroupBy(_ => 1).Select(g => new { Valor = g.Sum(o => o.PagamentoAdesao) ?? 0m, Quantidade = g.Count() }).FirstOrDefaultAsync(ct);
                if (mesAteAgora is not null)
                {
                    campos.Add(meta.MetaValor is > 0
                        ? new DiscordCampo("Meta do mês", $"{Moeda(mesAteAgora.Valor)} de {Moeda(meta.MetaValor.Value)} ({Math.Min(999, (int)Math.Round(mesAteAgora.Valor / meta.MetaValor.Value * 100))}%)", Lado: false)
                        : new DiscordCampo("Meta do mês", $"{mesAteAgora.Quantidade} de {meta.MetaQuantidadeVendas} vendas", Lado: false));
                }
            }

            try
            {
                await api.PublicarCartaoAsync(canal, new DiscordCartao($"📊 Resumo do dia — {nomes.GetValueOrDefault(regional.Key, "Regional")}", "Vendas de hoje da regional. Bom descanso, time!", CorResumo, campos, "CRM CSS Brasil"), ct);
                publicados++;
            }
            catch (DiscordApiException ex)
            {
                logger.LogWarning(ex, "Não foi possível publicar o resumo do dia da regional {RegionalId} no Discord.", regional.Key);
            }
        }

        return publicados;
    }

    public async Task<int> PublicarLeadsNovosAsync(CancellationToken ct)
    {
        if (!options.Value.Configurado) return 0;
        if (!(await ObterConfiguracaoAsync(ct)).LeadNovo) return 0;

        var agora = Agora;
        var marca = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == ChaveLeadNovoAte, ct);
        if (marca is null || !DateTimeOffset.TryParse(marca.Valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var desde))
        {
            // Primeira vez (ou marca perdida): começa daqui, sem despejar o que chegou antes.
            if (marca is null) db.CrmParametros.Add(marca = new CrmParametro { Chave = ChaveLeadNovoAte, Valor = "" });
            marca.Valor = agora.ToString("O", CultureInfo.InvariantCulture);
            await db.SaveChangesAsync(ct);
            return 0;
        }

        // Avança a marca antes de publicar: falha no Discord não repete os mesmos leads a cada ciclo.
        marca.Valor = agora.ToString("O", CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct);

        var leads = await db.CrmLeads.AsNoTracking()
            .Where(OrigemLead.VeioDoTrafegoPago)
            .Where(l => l.ResponsavelId != null && !l.Arquivado && l.VeiculoAdicionalDeLeadId == null
                && l.ResponsavelAtribuidoEm > desde && l.ResponsavelAtribuidoEm <= agora
                && (l.AtualizadoPorId ?? l.CriadoPorId) != l.ResponsavelId)
            .OrderBy(l => l.ResponsavelAtribuidoEm)
            .Select(l => new { l.Id, l.ProdutoInteresse, l.Origem, Consultor = l.Responsavel!.NomeCompleto, l.Responsavel.RegionalId })
            .Take(MaximoDeLeadsPorCiclo)
            .ToListAsync(ct);

        var baseUrl = options.Value.UrlPublica.TrimEnd('/');
        var publicados = 0;
        foreach (var l in leads)
        {
            var canal = await CanalDaRegionalAsync(l.RegionalId, ct);
            if (canal is null) continue;
            var campos = new List<DiscordCampo>();
            if (!string.IsNullOrWhiteSpace(l.ProdutoInteresse)) campos.Add(new DiscordCampo("Produto", l.ProdutoInteresse));
            if (!string.IsNullOrWhiteSpace(l.Origem)) campos.Add(new DiscordCampo("Origem", l.Origem));
            try
            {
                await api.PublicarCartaoAsync(canal,
                    new DiscordCartao("📥 Lead novo", $"**{l.Consultor}** recebeu um lead novo. Toque no título para abrir no CRM.", CorLead, campos, "CRM CSS Brasil",
                        baseUrl.Length > 0 ? $"{baseUrl}/app/crm/leads/kanban?lead={l.Id}" : null), ct);
                publicados++;
            }
            catch (DiscordApiException ex)
            {
                logger.LogWarning(ex, "Não foi possível avisar o lead novo {LeadId} no Discord.", l.Id);
            }
        }

        return publicados;
    }

    /// <summary>Canal do Discord da regional (o grupo "regional" criado pela sincronização). Sem ele, o aviso não sai: não inventa canal.</summary>
    private async Task<string?> CanalDaRegionalAsync(Guid? regionalId, CancellationToken ct)
    {
        if (regionalId is null) return null;
        var chave = $"regional:{regionalId}";
        return await db.CrmDiscordCanais.AsNoTracking().Where(c => c.Chave == chave && c.Ativo && !c.Desligado).Select(c => c.DiscordCanalId).FirstOrDefaultAsync(ct);
    }

    private async Task GravarAsync(string chave, string valor, CancellationToken ct)
    {
        var parametro = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == chave, ct);
        if (parametro is null) db.CrmParametros.Add(new CrmParametro { Chave = chave, Valor = valor });
        else parametro.Valor = valor;
        await db.SaveChangesAsync(ct);
    }

    private static string Moeda(decimal valor) => valor.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));
}
