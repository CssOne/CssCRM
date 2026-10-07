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
    internal const int DiasParado = 5;
    internal const int DiasJanelaParados = 60;

    /// <summary>Venda lançada com data de fechamento mais antiga que isto é correção de cadastro, não "acabou de fechar": não vira festa no canal.</summary>
    private static readonly TimeSpan IdadeMaximaDaVenda = TimeSpan.FromDays(3);

    private static readonly TimeSpan HoraDoResumo = new(9, 30, 0);

    private DateTimeOffset Agora => (relogio ?? TimeProvider.System).GetUtcNow();

    public async Task<DiscordAvisosCanaisDto> ObterConfiguracaoAsync(CancellationToken ct)
    {
        var valores = await db.CrmParametros.AsNoTracking().Where(p => p.Chave == ChaveVenda || p.Chave == ChaveMeta || p.Chave == ChaveParados).ToDictionaryAsync(p => p.Chave, p => p.Valor, ct);
        bool Ligado(string chave) => valores.TryGetValue(chave, out var v) && v == "1";
        return new DiscordAvisosCanaisDto(Ligado(ChaveVenda), Ligado(ChaveMeta), Ligado(ChaveParados));
    }

    public async Task<DiscordAvisosCanaisDto> DefinirConfiguracaoAsync(DiscordAvisosCanaisDto configuracao, CancellationToken ct)
    {
        await GravarAsync(ChaveVenda, configuracao.Venda ? "1" : "0", ct);
        await GravarAsync(ChaveMeta, configuracao.MetaBatida ? "1" : "0", ct);
        await GravarAsync(ChaveParados, configuracao.LeadsParados ? "1" : "0", ct);
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
                var valor = venda.PagamentoAdesao is > 0 ? $" Adesão de {Moeda(venda.PagamentoAdesao.Value)}." : "";
                var onde = venda.Regional is null ? "" : $" ({venda.Regional})";
                await api.PublicarAvisoAsync(canal, $"🎉 **{venda.Consultor}** fechou uma venda{onde}!{valor}", ct);
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

        var partes = new List<string>();
        if (meta.MetaValor is > 0) partes.Add($"{Moeda(realizado.Valor)} de {Moeda(meta.MetaValor.Value)}");
        if (meta.MetaQuantidadeVendas > 0) partes.Add($"{realizado.Quantidade} de {meta.MetaQuantidadeVendas} vendas");
        await api.PublicarAvisoAsync(canal, $"🏆 **A regional {nomeRegional} bateu a meta do mês!** {string.Join(" · ", partes)}.", ct);

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
                await api.PublicarAvisoAsync(canal, $"⚠️ **{nomes.GetValueOrDefault(r.RegionalId, "Regional")}:** {leads} de anúncio {(r.Total == 1 ? "está parado" : "estão parados")} há mais de {DiasParado} dias, sem contato. Vale olhar o painel de alertas.", ct);
                publicados++;
            }
            catch (DiscordApiException ex)
            {
                logger.LogWarning(ex, "Não foi possível publicar o resumo de leads parados da regional {RegionalId} no Discord.", r.RegionalId);
            }
        }

        return publicados;
    }

    /// <summary>Canal do Discord da regional (o grupo "regional" criado pela sincronização). Sem ele, o aviso não sai: não inventa canal.</summary>
    private async Task<string?> CanalDaRegionalAsync(Guid? regionalId, CancellationToken ct)
    {
        if (regionalId is null) return null;
        var chave = $"regional:{regionalId}";
        return await db.CrmDiscordCanais.AsNoTracking().Where(c => c.Chave == chave && c.Ativo).Select(c => c.DiscordCanalId).FirstOrDefaultAsync(ct);
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
