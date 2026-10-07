using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Discord;

public interface IDiscordGruposService
{
    Task<IReadOnlyList<DiscordCanalDto>> ListarCanaisAsync(CancellationToken ct);

    /// <summary>
    /// Deixa o servidor do Discord igual ao CRM: cria o que falta (categoria, cargos, canais) e acerta os cargos de cada pessoa que
    /// vinculou a conta. Pode rodar quantas vezes quiser: só mexe no que está diferente.
    /// </summary>
    Task<DiscordSincronizacaoDto> SincronizarAsync(CancellationToken ct);
}

/// <summary>
/// Grupos da empresa no Discord. Cada grupo é um <b>canal</b> visível só para quem tem o <b>cargo</b> dele; as pessoas ganham e perdem
/// cargos conforme a regional, o grupo e o perfil que têm no CRM. Usa cargos (e não permissão por pessoa em cada canal) porque o Discord
/// limita a 100 permissões individuais por canal. Grupos: geral (todos), gestão (gestores), um por regional e um por grupo.
/// O Discord nunca é lido para decidir o que o CRM mostra: o CRM manda, o Discord obedece.
/// </summary>
public sealed class DiscordGruposService(
    ApplicationDbContext db,
    IDiscordGuildApi api,
    IOptions<DiscordOptions> options,
    ILogger<DiscordGruposService> logger) : IDiscordGruposService
{
    internal const string ChaveCategoria = "discord:categoria-crm";
    private const string NomeCategoria = "CRM CSS Brasil";
    public const string ChaveGeral = "geral";
    public const string ChaveGestao = "gestao";

    /// <summary>Canal onde moram as threads privadas das conversas 1:1 (não é um grupo: fica fora das listas de grupos e do chat).</summary>
    public const string ChaveConversas = "conversas";

    private sealed record Desejado(string Chave, string Nome, string NomeCanal, string NomeCargo, string Topico);

    public async Task<IReadOnlyList<DiscordCanalDto>> ListarCanaisAsync(CancellationToken ct) =>
        (await db.CrmDiscordCanais.AsNoTracking().Where(c => c.Chave != ChaveConversas).ToListAsync(ct))
            .OrderBy(c => c.Chave == ChaveGeral ? 0 : c.Chave == ChaveGestao ? 1 : 2).ThenBy(c => c.Nome)
            .Select(c => new DiscordCanalDto(c.Chave, c.Nome, c.Ativo))
            .ToList();

    public async Task<DiscordSincronizacaoDto> SincronizarAsync(CancellationToken ct)
    {
        if (!options.Value.Configurado)
        {
            throw new CrmBusinessException("A integração com o Discord ainda não foi configurada no servidor.", "discord_nao_configurado");
        }

        var falhas = new List<string>();
        int canaisCriados = 0, cargosCriados = 0, vozCriados = 0;

        var desejados = await MontarDesejadosAsync(ct);
        var mapa = await db.CrmDiscordCanais.ToDictionaryAsync(c => c.Chave, ct);

        string categoriaId;
        HashSet<string> cargosNoServidor;
        try
        {
            categoriaId = await GarantirCategoriaAsync(ct);
            cargosNoServidor = await api.ListarIdsDeCargosAsync(ct);
        }
        catch (DiscordApiException ex)
        {
            // Sem categoria ou sem poder listar os cargos não dá para seguir: o administrador precisa ver o motivo.
            return new DiscordSincronizacaoDto(0, 0, 0, 0, [ex.Message]);
        }

        foreach (var d in desejados)
        {
            try
            {
                mapa.TryGetValue(d.Chave, out var existente);
                var cargoOk = existente is not null && cargosNoServidor.Contains(existente.DiscordCargoId);
                var canalOk = cargoOk && await api.CanalExisteAsync(existente!.DiscordCanalId, ct);

                if (existente is not null && cargoOk && canalOk)
                {
                    existente.Nome = d.Nome;
                    existente.Ativo = true;
                    if (await GarantirVozAsync(existente, d, categoriaId, ct)) vozCriados++;
                    continue;
                }

                var cargoId = cargoOk ? existente!.DiscordCargoId : await api.CriarCargoAsync(d.NomeCargo, ct);
                if (!cargoOk) cargosCriados++;
                var canalId = await api.CriarCanalDeTextoAsync(d.NomeCanal, categoriaId, cargoId, d.Topico, ct);
                canaisCriados++;

                if (existente is null)
                {
                    existente = new CrmDiscordCanal { Chave = d.Chave };
                    db.CrmDiscordCanais.Add(existente);
                    mapa[d.Chave] = existente;
                }

                existente.Nome = d.Nome;
                existente.DiscordCanalId = canalId;
                existente.DiscordCargoId = cargoId;
                existente.Ativo = true;
                cargosNoServidor.Add(cargoId);
                if (await GarantirVozAsync(existente, d, categoriaId, ct)) vozCriados++;
            }
            catch (DiscordApiException ex)
            {
                falhas.Add($"{d.Nome}: {ex.Message}");
            }
        }

        try
        {
            if (await GarantirCanalDeConversasAsync(mapa, categoriaId, ct)) canaisCriados++;
        }
        catch (DiscordApiException ex)
        {
            falhas.Add($"Conversas diretas: {ex.Message}");
        }

        // Regional ou grupo que não existe mais no CRM: o canal fica no Discord, mas ninguém novo entra.
        foreach (var obsoleto in mapa.Values.Where(m => m.Chave != ChaveConversas && desejados.All(d => d.Chave != m.Chave))) obsoleto.Ativo = false;
        await db.SaveChangesAsync(ct);

        var (atualizados, foraDoServidor) = await SincronizarMembrosAsync(mapa, falhas, ct);

        logger.LogInformation("Grupos do Discord sincronizados: {Canais} canal(is) e {Cargos} cargo(s) criados, {Membros} membro(s) ajustados, {Fora} fora do servidor, {Falhas} falha(s).",
            canaisCriados, cargosCriados, atualizados, foraDoServidor, falhas.Count);
        return new DiscordSincronizacaoDto(canaisCriados, cargosCriados, atualizados, foraDoServidor, falhas, vozCriados);
    }

    /// <summary>Os grupos que o CRM quer ter no Discord, a partir das regionais e grupos ativos.</summary>
    private async Task<List<Desejado>> MontarDesejadosAsync(CancellationToken ct)
    {
        var lista = new List<Desejado>
        {
            new(ChaveGeral, "Geral", "geral", "CRM · Todos", "Conversa de toda a equipe da CSS Brasil."),
            new(ChaveGestao, "Gestão", "gestao", "CRM · Gestão", "Gestores e supervisores."),
        };

        foreach (var r in await db.CrmRegionais.AsNoTracking().Where(r => r.Ativa).OrderBy(r => r.Nome).ToListAsync(ct))
        {
            lista.Add(new($"regional:{r.Id}", r.Nome, $"regional-{Slug(r.Nome)}", $"CRM · {r.Nome}", $"Regional {r.Nome}."));
        }

        var grupos = await db.CrmGrupos.AsNoTracking().Include(g => g.Regional)
            .Where(g => g.Ativo && g.Regional.Ativa).OrderBy(g => g.Regional.Nome).ThenBy(g => g.Nome).ToListAsync(ct);
        foreach (var g in grupos)
        {
            lista.Add(new($"grupo:{g.Id}", $"{g.Regional.Nome} · {g.Nome}", $"grupo-{Slug(g.Regional.Nome)}-{Slug(g.Nome)}",
                $"CRM · {g.Regional.Nome} / {g.Nome}", $"Grupo {g.Nome} da regional {g.Regional.Nome}."));
        }

        return lista;
    }

    /// <summary>Garante o canal de voz do grupo (visível só a quem tem o cargo). Devolve se criou um novo.</summary>
    private async Task<bool> GarantirVozAsync(CrmDiscordCanal entrada, Desejado d, string categoriaId, CancellationToken ct)
    {
        if (entrada.DiscordVozId is { Length: > 0 } atual && await api.CanalExisteAsync(atual, ct)) return false;

        entrada.DiscordVozId = await api.CriarCanalDeVozAsync($"Voz · {d.Nome}", categoriaId, [new DiscordPermitido(entrada.DiscordCargoId, Pessoa: false)], ct);
        return true;
    }

    /// <summary>Cria o canal das conversas 1:1 (visível a todos os que têm o cargo "geral"). Devolve se criou.</summary>
    private async Task<bool> GarantirCanalDeConversasAsync(Dictionary<string, CrmDiscordCanal> mapa, string categoriaId, CancellationToken ct)
    {
        if (!mapa.TryGetValue(ChaveGeral, out var geral) || string.IsNullOrEmpty(geral.DiscordCargoId)) return false; // sem o grupo geral não há cargo para dar acesso

        mapa.TryGetValue(ChaveConversas, out var existente);
        if (existente is not null && await api.CanalExisteAsync(existente.DiscordCanalId, ct))
        {
            existente.Ativo = true;
            return false;
        }

        var canalId = await api.CriarCanalDeConversasAsync("conversas-diretas", categoriaId, geral.DiscordCargoId, ct);
        if (existente is null)
        {
            existente = new CrmDiscordCanal { Chave = ChaveConversas };
            db.CrmDiscordCanais.Add(existente);
            mapa[ChaveConversas] = existente;
        }

        existente.Nome = "Conversas diretas";
        existente.DiscordCanalId = canalId;
        existente.DiscordCargoId = geral.DiscordCargoId;
        existente.Ativo = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string> GarantirCategoriaAsync(CancellationToken ct)
    {
        var salvo = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == ChaveCategoria, ct);
        if (salvo is not null && await api.CanalExisteAsync(salvo.Valor, ct)) return salvo.Valor;

        var id = await api.CriarCategoriaAsync(NomeCategoria, ct);
        if (salvo is null) db.CrmParametros.Add(new CrmParametro { Chave = ChaveCategoria, Valor = id });
        else salvo.Valor = id;
        await db.SaveChangesAsync(ct);
        return id;
    }

    /// <summary>Dá e tira cargos de cada pessoa que vinculou o Discord, para refletir regional, grupo e perfil no CRM.</summary>
    private async Task<(int Atualizados, int ForaDoServidor)> SincronizarMembrosAsync(Dictionary<string, CrmDiscordCanal> mapa, List<string> falhas, CancellationToken ct)
    {
        var vinculos = await db.CrmDiscordVinculos.ToListAsync(ct);
        if (vinculos.Count == 0) return (0, 0);

        var usuarioIds = vinculos.Select(v => v.UsuarioId).ToList();
        var usuarios = (await db.Users.AsNoTracking().Where(u => usuarioIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Ativo, u.RegionalId, u.GrupoId }).ToListAsync(ct)).ToDictionary(u => u.Id);
        var gestores = (await db.UserRoles.Where(ur => usuarioIds.Contains(ur.UserId))
            .Join(db.Roles.Where(r => Roles.GestaoComercial.Contains(r.Name!)), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Distinct().ToListAsync(ct)).ToHashSet();

        var gerenciados = mapa.Values.Select(m => m.DiscordCargoId).ToHashSet();
        string? CargoDe(string chave) => mapa.TryGetValue(chave, out var m) && m.Ativo ? m.DiscordCargoId : null;

        int atualizados = 0, fora = 0;
        foreach (var vinculo in vinculos)
        {
            if (!usuarios.TryGetValue(vinculo.UsuarioId, out var usuario)) continue;

            // Quem foi inativado no CRM perde todos os cargos do CRM.
            var desejados = new HashSet<string>();
            if (usuario.Ativo)
            {
                if (CargoDe(ChaveGeral) is { } geral) desejados.Add(geral);
                if (gestores.Contains(usuario.Id) && CargoDe(ChaveGestao) is { } gestao) desejados.Add(gestao);
                if (usuario.RegionalId is { } regional && CargoDe($"regional:{regional}") is { } cargoRegional) desejados.Add(cargoRegional);
                if (usuario.GrupoId is { } grupo && CargoDe($"grupo:{grupo}") is { } cargoGrupo) desejados.Add(cargoGrupo);
            }

            try
            {
                var atuais = await api.ObterCargosDoMembroAsync(vinculo.DiscordUserId, ct);
                if (atuais is null)
                {
                    vinculo.NoServidor = false;
                    fora++;
                    continue;
                }

                vinculo.NoServidor = true;
                var adicionar = desejados.Where(c => !atuais.Contains(c)).ToList();
                var remover = atuais.Where(c => gerenciados.Contains(c) && !desejados.Contains(c)).ToList();
                foreach (var cargo in adicionar) await api.AtribuirCargoAsync(vinculo.DiscordUserId, cargo, ct);
                foreach (var cargo in remover) await api.RemoverCargoAsync(vinculo.DiscordUserId, cargo, ct);
                if (adicionar.Count + remover.Count > 0) atualizados++;
            }
            catch (DiscordApiException ex)
            {
                falhas.Add($"{vinculo.DiscordNome}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return (atualizados, fora);
    }

    /// <summary>Nome de canal do Discord: minúsculas, sem acento, só letras, números e hífen.</summary>
    internal static string Slug(string texto)
    {
        var semAcento = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) semAcento.Append(c);
        }

        var slug = Regex.Replace(semAcento.ToString().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "grupo" : slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }
}
