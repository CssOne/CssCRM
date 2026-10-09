using System.Globalization;
using System.Text.Json;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Discord;

public interface IDiscordComandosService
{
    /// <summary>Executa um comando de barra (<c>/vendas</c>, <c>/meta</c>) e devolve a resposta no formato do Discord (só quem pediu vê).</summary>
    Task<object> ExecutarAsync(JsonElement interacao, CancellationToken ct);
}

/// <summary>
/// Comandos de barra do Discord. Quem pergunta é identificado pela conta do Discord <b>vinculada</b> ao CRM; quem não vinculou (ou foi inativado) não
/// recebe número nenhum. Cada pessoa vê só os próprios números e os da própria regional, sem dados de clientes; as respostas são "efêmeras" (só
/// aparecem para quem pediu).
/// </summary>
public sealed class DiscordComandosService(ApplicationDbContext db, TimeProvider? relogio = null) : IDiscordComandosService
{
    public const string NomeVendas = "vendas";
    public const string NomeMeta = "meta";

    /// <summary>Resposta que só quem pediu enxerga (flag 64).</summary>
    private static object Efemera(string texto) => new { type = 4, data = new { content = texto, flags = 64, allowed_mentions = new { parse = Array.Empty<string>() } } };

    private DateTimeOffset Agora => (relogio ?? TimeProvider.System).GetUtcNow();

    public async Task<object> ExecutarAsync(JsonElement interacao, CancellationToken ct)
    {
        var discordId = QuemPediu(interacao);
        var comando = interacao.TryGetProperty("data", out var data) && data.TryGetProperty("name", out var nome) ? nome.GetString() : null;
        if (discordId is null || comando is null) return Efemera("Não consegui entender o pedido.");

        var usuario = await (from v in db.CrmDiscordVinculos.AsNoTracking()
                             join u in db.Users.AsNoTracking() on v.UsuarioId equals u.Id
                             where v.DiscordUserId == discordId && u.Ativo
                             select new { u.Id, u.NomeCompleto, u.RegionalId }).FirstOrDefaultAsync(ct);
        if (usuario is null)
        {
            return Efemera("Não encontrei a sua conta do CRM. Vincule o seu Discord no CRM (menu Discord → Vincular Discord) e tente de novo.");
        }

        return comando switch
        {
            NomeVendas => Efemera(await VendasAsync(usuario.Id, usuario.RegionalId, Opcao(data, "periodo") ?? "hoje", ct)),
            NomeMeta => Efemera(await MetaAsync(usuario.RegionalId, ct)),
            _ => Efemera("Esse comando não existe."),
        };
    }

    private static string? QuemPediu(JsonElement interacao)
    {
        // No servidor vem em "member.user"; numa conversa direta com o bot, em "user".
        if (interacao.TryGetProperty("member", out var membro) && membro.TryGetProperty("user", out var u) && u.TryGetProperty("id", out var id)) return id.GetString();
        if (interacao.TryGetProperty("user", out var direto) && direto.TryGetProperty("id", out var id2)) return id2.GetString();
        return null;
    }

    private static string? Opcao(JsonElement data, string nome)
    {
        if (!data.TryGetProperty("options", out var opcoes) || opcoes.ValueKind != JsonValueKind.Array) return null;
        foreach (var o in opcoes.EnumerateArray())
        {
            if (o.TryGetProperty("name", out var n) && n.GetString() == nome && o.TryGetProperty("value", out var v)) return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        }

        return null;
    }

    private async Task<string> VendasAsync(Guid usuarioId, Guid? regionalId, string periodo, CancellationToken ct)
    {
        var hoje = HorarioBrasilia.Dia(Agora);
        var (de, ate, rotulo) = periodo == "mes"
            ? (HorarioBrasilia.PrimeiroDiaDoMes(hoje), HorarioBrasilia.PrimeiroDiaDoMes(hoje).AddMonths(1).AddDays(-1), "no mês")
            : (hoje, hoje, "hoje");
        // Mesma regra de "venda do dia" do painel da TV: pela data de ativação; sem ela, pela data da venda.
        var per = PeriodoDeAtivacao.De(de, ate);
        var vendas = db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho
                && ((o.AtivoEm != null && o.AtivoEm >= per.AtivacaoDe && o.AtivoEm < per.AtivacaoAte)
                    || (o.AtivoEm == null && o.DataEfetivaFechamento >= per.VendaDe && o.DataEfetivaFechamento <= per.VendaAte)));

        var minhas = await vendas.Where(o => o.ResponsavelId == usuarioId).GroupBy(_ => 1)
            .Select(g => new { Quantidade = g.Count(), Valor = g.Sum(o => o.PagamentoAdesao) ?? 0m }).FirstOrDefaultAsync(ct);
        var texto = $"📊 **Suas vendas {rotulo}:** {Vendas(minhas?.Quantidade ?? 0)} · adesão {Moeda(minhas?.Valor ?? 0m)}";

        if (regionalId is { } regional)
        {
            var nome = await db.CrmRegionais.AsNoTracking().Where(r => r.Id == regional).Select(r => r.Nome).FirstOrDefaultAsync(ct);
            var time = await vendas.Where(o => o.Responsavel.RegionalId == regional).GroupBy(_ => 1)
                .Select(g => new { Quantidade = g.Count(), Valor = g.Sum(o => o.PagamentoAdesao) ?? 0m }).FirstOrDefaultAsync(ct);
            texto += $"\n🏢 **Regional {nome} {rotulo}:** {Vendas(time?.Quantidade ?? 0)} · adesão {Moeda(time?.Valor ?? 0m)}";
        }

        return texto;
    }

    private async Task<string> MetaAsync(Guid? regionalId, CancellationToken ct)
    {
        if (regionalId is not { } regional) return "Você ainda não está em uma regional no CRM, então não há meta para mostrar.";

        var mes = HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Dia(Agora));
        var nome = await db.CrmRegionais.AsNoTracking().Where(r => r.Id == regional).Select(r => r.Nome).FirstOrDefaultAsync(ct) ?? "sua regional";
        var meta = await db.CrmRegionalGoals.AsNoTracking().FirstOrDefaultAsync(g => g.RegionalId == regional && g.MesReferencia == mes, ct);
        if (meta is null || (meta.MetaQuantidadeVendas <= 0 && meta.MetaValor is not > 0)) return $"A regional {nome} não tem meta cadastrada para este mês.";

        var inicio = HorarioBrasilia.Inicio(mes);
        var fim = HorarioBrasilia.Inicio(mes.AddMonths(1));
        var feito = await db.CrmOpportunities.AsNoTracking()
            .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Ganho && o.Responsavel.RegionalId == regional && o.DataEfetivaFechamento >= inicio && o.DataEfetivaFechamento < fim)
            .GroupBy(_ => 1).Select(g => new { Quantidade = g.Count(), Valor = g.Sum(o => o.PagamentoAdesao) ?? 0m }).FirstOrDefaultAsync(ct);
        var quantidade = feito?.Quantidade ?? 0;
        var valor = feito?.Valor ?? 0m;

        var linhas = new List<string> { $"🎯 **Meta de {nome} em {mes.ToString("MMMM", CultureInfo.GetCultureInfo("pt-BR"))}:**" };
        if (meta.MetaValor is > 0) linhas.Add($"• Adesão: {Moeda(valor)} de {Moeda(meta.MetaValor.Value)} ({Percentual(valor, meta.MetaValor.Value)}%)");
        if (meta.MetaQuantidadeVendas > 0) linhas.Add($"• Vendas: {quantidade} de {meta.MetaQuantidadeVendas} ({Percentual(quantidade, meta.MetaQuantidadeVendas)}%)");
        return string.Join("\n", linhas);
    }

    private static int Percentual(decimal feito, decimal meta) => meta <= 0 ? 0 : Math.Min(999, (int)Math.Round(feito / meta * 100));

    private static string Vendas(int n) => n == 1 ? "1 venda" : $"{n} vendas";

    private static string Moeda(decimal valor) => valor.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));
}
