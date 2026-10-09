using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

public interface IDiscordMembrosService
{
    /// <summary>Quem está no servidor do Discord, com a pessoa do CRM que vinculou a conta (se houver) e os cargos.</summary>
    Task<DiscordMembrosDto> ListarAsync(CancellationToken ct);
}

/// <summary>
/// Membros do servidor do Discord vistos de dentro do CRM. O Discord é lido (guardado por 30 segundos, para a tela não pesar nele) e cruzado com
/// os vínculos do CRM: assim aparece quem já vinculou, quem está no servidor sem conta no CRM e quem vinculou mas saiu. Só leitura.
/// </summary>
public sealed class DiscordMembrosService(ApplicationDbContext db, IDiscordGuildApi api, IMemoryCache cache, IOptions<DiscordOptions> options) : IDiscordMembrosService
{
    private const string ChaveDoCache = "discord:membros:servidor";

    public async Task<DiscordMembrosDto> ListarAsync(CancellationToken ct)
    {
        if (!options.Value.Configurado)
        {
            throw new CrmBusinessException("A integração com o Discord ainda não foi configurada no servidor.", "discord_nao_configurado");
        }

        var lido = await cache.GetOrCreateAsync(ChaveDoCache, async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            try
            {
                return (Membros: await api.ListarMembrosAsync(ct), Cargos: await api.ListarCargosAsync(ct));
            }
            catch (DiscordApiException ex)
            {
                throw new CrmBusinessException(ex.Message, "discord_indisponivel");
            }
        });
        var membros = lido!.Membros;
        var cargos = lido.Cargos;

        var nomesDosCargos = cargos.ToDictionary(c => c.Id, c => c.Nome);
        var vinculos = await (from v in db.CrmDiscordVinculos.AsNoTracking()
                              join u in db.Users.AsNoTracking() on v.UsuarioId equals u.Id
                              select new { v.DiscordUserId, u.Id, u.NomeCompleto, u.Ativo, u.RegionalId }).ToListAsync(ct);
        var regionais = await db.CrmRegionais.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        var porDiscord = vinculos.GroupBy(v => v.DiscordUserId).ToDictionary(g => g.Key, g => g.First());

        DiscordMembroVinculoDto? Vinculo(string discordId) => porDiscord.TryGetValue(discordId, out var v)
            ? new DiscordMembroVinculoDto(v.Id, v.NomeCompleto, v.RegionalId is { } r ? regionais.GetValueOrDefault(r) : null, v.Ativo)
            : null;

        var itens = membros
            .Select(m => new DiscordMembroDto(
                m.Id, m.Apelido ?? m.NomeGlobal ?? m.Usuario, m.Usuario, m.AvatarUrl, m.Bot, m.EntrouEm,
                m.Cargos.Where(nomesDosCargos.ContainsKey).Select(c => nomesDosCargos[c]).Where(n => n != "@everyone").OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                Vinculo(m.Id)))
            .OrderBy(m => m.Bot).ThenBy(m => m.Vinculo is null).ThenBy(m => m.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var noServidor = membros.Select(m => m.Id).ToHashSet();
        var fora = vinculos.Where(v => !noServidor.Contains(v.DiscordUserId))
            .Select(v => new DiscordMembroVinculoDto(v.Id, v.NomeCompleto, v.RegionalId is { } r ? regionais.GetValueOrDefault(r) : null, v.Ativo))
            .OrderBy(v => v.Nome, StringComparer.OrdinalIgnoreCase).ToList();

        return new DiscordMembrosDto(
            itens.Count, itens.Count(m => m.Vinculo is not null), itens.Count(m => m.Vinculo is null && !m.Bot), itens.Count(m => m.Bot), itens, fora);
    }
}
