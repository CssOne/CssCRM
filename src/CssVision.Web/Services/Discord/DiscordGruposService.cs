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
    /// Cria um canal de texto extra no Discord (na categoria do CRM), visível só para quem está no grupo <paramref name="acessoChave"/>. Aparece no chat de quem tem acesso.
    /// </summary>
    Task<DiscordCanalDto> CriarCanalAsync(string nome, string acessoChave, string? topico, CancellationToken ct, bool voz = false);

    /// <summary>
    /// Apaga um canal extra no Discord e no CRM. Irreversível: só vale se <paramref name="confirmarNome"/> for o nome do canal. Canais de grupos
    /// (regionais, grupos, geral, gestão) e o das conversas diretas nunca são apagados por aqui.
    /// </summary>
    Task ApagarCanalAsync(string chave, string confirmarNome, CancellationToken ct);

    /// <summary>Esconde (ou volta a mostrar) um canal extra no chat do CRM, sem mexer no Discord. Só canais extras.</summary>
    Task<DiscordCanalDto> ArquivarCanalAsync(string chave, bool arquivar, CancellationToken ct);

    /// <summary>Volta a ligar um grupo cujo canal foi apagado: a próxima sincronização recria o canal (e o de voz) com o cargo que o grupo já tem.</summary>
    Task<DiscordCanalDto> ReligarCanalAsync(string chave, CancellationToken ct);

    /// <summary>Renomeia o canal no Discord e no CRM (e o canal de voz do grupo, se houver). O nome no CRM vale mesmo depois de novas sincronizações.</summary>
    Task<DiscordCanalDto> RenomearCanalAsync(string chave, string nome, CancellationToken ct);

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

    /// <summary>Prefixo da chave dos canais extras, criados por um administrador (não correspondem a regional nem grupo do CRM).</summary>
    public const string PrefixoExtra = "extra:";

    /// <summary>Prefixo da chave dos canais de voz extras (só existem no Discord; não aparecem no chat de texto).</summary>
    public const string PrefixoExtraVoz = "extra-voz:";

    internal static bool EhExtra(string chave) => chave.StartsWith(PrefixoExtra, StringComparison.Ordinal) || chave.StartsWith(PrefixoExtraVoz, StringComparison.Ordinal);

    private sealed record Desejado(string Chave, string Nome, string NomeCanal, string NomeCargo, string Topico, DiscordAparenciaCargo Aparencia);

    /// <summary>
    /// Cores dos cargos das regionais (a primeira regional ganha a primeira cor, e assim por diante). Sem roxo, que é o do cargo de gestão. Os grupos
    /// de uma regional usam a cor dela: na lista de membros do Discord, o nome de cada pessoa já mostra a que regional pertence.
    /// </summary>
    internal static readonly int[] CoresDasRegionais = [0x3498DB, 0xE67E22, 0x1ABC9C, 0xE91E63, 0x2ECC71, 0xF1C40F, 0xE74C3C, 0x34495E];

    internal const int CorGeral = 0x95A5A6;
    internal const int CorGestao = 0x8E44AD;

    /// <summary>Apelido no servidor: o nome do CRM; se passar de 32 caracteres (limite do Discord), primeiro e último nome.</summary>
    internal static string ApelidoNoDiscord(string nomeCompleto)
    {
        var nome = string.Join(' ', nomeCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (nome.Length <= 32) return nome;
        var partes = nome.Split(' ');
        var curto = partes.Length > 1 ? $"{partes[0]} {partes[^1]}" : nome;
        return curto.Length <= 32 ? curto : curto[..32].TrimEnd();
    }

    public async Task<IReadOnlyList<DiscordCanalDto>> ListarCanaisAsync(CancellationToken ct) =>
        (await db.CrmDiscordCanais.AsNoTracking().Where(c => c.Chave != ChaveConversas).ToListAsync(ct))
            .OrderBy(c => c.Chave == ChaveGeral ? 0 : c.Chave == ChaveGestao ? 1 : 2).ThenBy(c => c.Nome)
            .Select(c => new DiscordCanalDto(c.Chave, c.NomeExibido, c.Ativo, EhExtra(c.Chave), c.AcessoChave, c.Chave.StartsWith(PrefixoExtraVoz, StringComparison.Ordinal), c.Desligado))
            .ToList();

    public async Task<DiscordCanalDto> CriarCanalAsync(string nome, string acessoChave, string? topico, CancellationToken ct, bool voz = false)
    {
        ExigirConfigurado();
        nome = NomeDeCanalValido(nome);
        topico = string.IsNullOrWhiteSpace(topico) ? null : topico.Trim();
        if (topico is { Length: > 1024 })
        {
            throw new CrmBusinessException("O tópico do canal pode ter no máximo 1024 caracteres.", "canal_topico_longo");
        }

        var canais = await db.CrmDiscordCanais.ToListAsync(ct);
        var acesso = canais.FirstOrDefault(c => c.Chave == acessoChave && c.Ativo && c.Chave != ChaveConversas && !EhExtra(c.Chave))
            ?? throw new CrmBusinessException("Escolha o grupo que vai ver o canal. Sincronize os grupos antes, se ele ainda não existir no Discord.", "canal_acesso_invalido");
        if (canais.Any(c => c.Ativo && string.Equals(c.NomeExibido, nome, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CrmBusinessException("Já existe um canal com esse nome.", "canal_nome_repetido");
        }

        string canalId;
        try
        {
            var categoriaId = await GarantirCategoriaAsync(ct);
            canalId = voz
                ? await api.CriarCanalDeVozAsync(nome, categoriaId, [new DiscordPermitido(acesso.DiscordCargoId, Pessoa: false)], ct)
                : await api.CriarCanalDeTextoAsync(Slug(nome), categoriaId, acesso.DiscordCargoId, topico ?? $"Canal {nome}.", ct);
        }
        catch (DiscordApiException ex)
        {
            throw new CrmBusinessException(ex.Message, "discord_indisponivel");
        }

        var novo = new CrmDiscordCanal
        {
            Chave = $"{(voz ? PrefixoExtraVoz : PrefixoExtra)}{Guid.NewGuid():N}", Nome = nome, DiscordCanalId = canalId, DiscordCargoId = acesso.DiscordCargoId,
            AcessoChave = acesso.Chave, Ativo = true,
        };
        db.CrmDiscordCanais.Add(novo);
        await db.SaveChangesAsync(ct);
        return new DiscordCanalDto(novo.Chave, novo.NomeExibido, novo.Ativo, true, novo.AcessoChave, voz);
    }

    public async Task<DiscordCanalDto> RenomearCanalAsync(string chave, string nome, CancellationToken ct)
    {
        ExigirConfigurado();
        nome = NomeDeCanalValido(nome);
        var canal = await db.CrmDiscordCanais.FirstOrDefaultAsync(c => c.Chave == chave && c.Chave != ChaveConversas, ct)
            ?? throw new CrmBusinessException("Esse canal não existe.", "canal_nao_encontrado");
        if (await db.CrmDiscordCanais.AnyAsync(c => c.Id != canal.Id && c.Ativo && c.Chave != ChaveConversas && (c.NomePersonalizado ?? c.Nome).ToLower() == nome.ToLower(), ct))
        {
            throw new CrmBusinessException("Já existe um canal com esse nome.", "canal_nome_repetido");
        }

        try
        {
            await api.RenomearCanalAsync(canal.DiscordCanalId, canal.Chave.StartsWith(PrefixoExtraVoz, StringComparison.Ordinal) ? nome : Slug(nome), null, ct);
            // O canal de voz do grupo acompanha o nome; se o Discord recusar, o canal de texto já foi renomeado e o resto continua.
            if (canal.DiscordVozId is { Length: > 0 })
            {
                try { await api.RenomearCanalAsync(canal.DiscordVozId, $"Voz · {nome}", null, ct); }
                catch (DiscordApiException ex) { logger.LogInformation(ex, "Canal de texto renomeado, mas não o canal de voz do grupo {Chave}.", chave); }
            }
        }
        catch (DiscordApiException ex)
        {
            throw new CrmBusinessException(ex.Message, "discord_indisponivel");
        }

        canal.NomePersonalizado = nome;
        if (EhExtra(canal.Chave)) canal.Nome = nome;
        await db.SaveChangesAsync(ct);
        return new DiscordCanalDto(canal.Chave, canal.NomeExibido, canal.Ativo, EhExtra(canal.Chave), canal.AcessoChave, canal.Chave.StartsWith(PrefixoExtraVoz, StringComparison.Ordinal));
    }

    public async Task ApagarCanalAsync(string chave, string confirmarNome, CancellationToken ct)
    {
        ExigirConfigurado();
        var canal = await db.CrmDiscordCanais.FirstOrDefaultAsync(c => c.Chave == chave, ct);
        if (canal is null || canal.Chave == ChaveConversas)
        {
            throw new CrmBusinessException("Esse canal não pode ser apagado por aqui (o das conversas diretas é do sistema).", "canal_nao_apagavel");
        }

        if (canal.Desligado)
        {
            throw new CrmBusinessException("Esse canal já foi apagado no Discord.", "canal_ja_apagado");
        }

        if (!string.Equals((confirmarNome ?? "").Trim(), canal.NomeExibido, StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmBusinessException("Digite o nome do canal exatamente para confirmar que quer apagá-lo.", "canal_confirmacao");
        }

        var idsDoDiscord = canal.DiscordCanalId;
        try
        {
            await api.ApagarCanalAsync(canal.DiscordCanalId, ct);
            // Canal de voz do grupo: sai junto (o que já foi apagado no Discord conta como apagado).
            if (!string.IsNullOrEmpty(canal.DiscordVozId)) await api.ApagarCanalAsync(canal.DiscordVozId, ct);
        }
        catch (DiscordApiException ex)
        {
            throw new CrmBusinessException(ex.Message, "discord_indisponivel");
        }

        // As reações e as marcas de "lido" do canal apagado não servem mais.
        db.CrmDiscordReacoes.RemoveRange(db.CrmDiscordReacoes.Where(r => r.LeituraId == idsDoDiscord));
        db.CrmDiscordLeituras.RemoveRange(db.CrmDiscordLeituras.Where(l => l.Chave == canal.Chave));
        if (EhExtra(canal.Chave))
        {
            db.CrmDiscordCanais.Remove(canal);
        }
        else
        {
            // Canal de grupo: o grupo e o cargo ficam (as pessoas continuam nele); só some o canal, e a sincronização não o recria até religar.
            canal.Desligado = true;
            canal.DiscordCanalId = string.Empty;
            canal.DiscordVozId = null;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<DiscordCanalDto> ReligarCanalAsync(string chave, CancellationToken ct)
    {
        var canal = await db.CrmDiscordCanais.FirstOrDefaultAsync(c => c.Chave == chave && c.Chave != ChaveConversas && c.Desligado, ct)
            ?? throw new CrmBusinessException("Esse grupo não está desligado no Discord.", "canal_nao_desligado");
        canal.Desligado = false;
        await db.SaveChangesAsync(ct);
        return new DiscordCanalDto(canal.Chave, canal.NomeExibido, canal.Ativo, false, canal.AcessoChave, false, false);
    }

    public async Task<DiscordCanalDto> ArquivarCanalAsync(string chave, bool arquivar, CancellationToken ct)
    {
        var canal = await db.CrmDiscordCanais.FirstOrDefaultAsync(c => c.Chave == chave, ct);
        if (canal is null || !EhExtra(canal.Chave))
        {
            throw new CrmBusinessException("Só canais extras podem ser arquivados.", "canal_nao_arquivavel");
        }

        canal.Ativo = !arquivar;
        await db.SaveChangesAsync(ct);
        return new DiscordCanalDto(canal.Chave, canal.NomeExibido, canal.Ativo, true, canal.AcessoChave, canal.Chave.StartsWith(PrefixoExtraVoz, StringComparison.Ordinal));
    }

    private void ExigirConfigurado()
    {
        if (!options.Value.Configurado)
        {
            throw new CrmBusinessException("A integração com o Discord ainda não foi configurada no servidor.", "discord_nao_configurado");
        }
    }

    private static string NomeDeCanalValido(string? nome)
    {
        nome = string.Join(' ', (nome ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (nome.Length == 0)
        {
            throw new CrmBusinessException("Dê um nome ao canal.", "canal_sem_nome");
        }

        if (nome.Length > 60)
        {
            throw new CrmBusinessException("O nome do canal pode ter no máximo 60 caracteres.", "canal_nome_longo");
        }

        return nome;
    }

    public async Task<DiscordSincronizacaoDto> SincronizarAsync(CancellationToken ct)
    {
        if (!options.Value.Configurado)
        {
            throw new CrmBusinessException("A integração com o Discord ainda não foi configurada no servidor.", "discord_nao_configurado");
        }

        var falhas = new List<string>();
        int canaisCriados = 0, cargosCriados = 0, vozCriados = 0;
        var boasVindas = false;

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
                if (existente is { Desligado: true })
                {
                    // Canal apagado de propósito: o grupo segue existindo (nome em dia, cargo mantido), mas não ganha canal de novo até ser religado.
                    existente.Nome = d.Nome;
                    existente.Ativo = true;
                    continue;
                }

                var cargoOk = existente is not null && cargosNoServidor.Contains(existente.DiscordCargoId);
                var canalOk = cargoOk && await api.CanalExisteAsync(existente!.DiscordCanalId, ct);

                if (existente is not null && cargoOk && canalOk)
                {
                    existente.Nome = d.Nome;
                    existente.Ativo = true;
                    if (await GarantirVozAsync(existente, d, categoriaId, ct)) vozCriados++;
                    continue;
                }

                var cargoId = cargoOk ? existente!.DiscordCargoId : await api.CriarCargoAsync(d.NomeCargo, ct, d.Aparencia);
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

        // Comandos de barra (/vendas, /meta): só com a chave pública configurada; falha aqui não atrapalha o resto.
        if (options.Value.ComandosAtivos)
        {
            try
            {
                await api.RegistrarComandosAsync(ct);
            }
            catch (DiscordApiException ex)
            {
                falhas.Add($"Comandos do Discord: {ex.Message}");
            }
        }

        try
        {
            boasVindas = await GarantirBoasVindasAsync(mapa, ct);
        }
        catch (DiscordApiException ex)
        {
            falhas.Add($"Boas-vindas: {ex.Message}");
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
        foreach (var obsoleto in mapa.Values.Where(m => m.Chave != ChaveConversas && !EhExtra(m.Chave) && desejados.All(d => d.Chave != m.Chave))) obsoleto.Ativo = false;
        await db.SaveChangesAsync(ct);

        var (atualizados, foraDoServidor, apelidos) = await SincronizarMembrosAsync(mapa, falhas, ct);

        logger.LogInformation("Grupos do Discord sincronizados: {Canais} canal(is) e {Cargos} cargo(s) criados, {Membros} membro(s) ajustados, {Fora} fora do servidor, {Falhas} falha(s).",
            canaisCriados, cargosCriados, atualizados, foraDoServidor, falhas.Count);
        return new DiscordSincronizacaoDto(canaisCriados, cargosCriados, atualizados, foraDoServidor, falhas, vozCriados, apelidos, boasVindas);
    }

    /// <summary>Os grupos que o CRM quer ter no Discord, a partir das regionais e grupos ativos.</summary>
    private async Task<List<Desejado>> MontarDesejadosAsync(CancellationToken ct)
    {
        var lista = new List<Desejado>
        {
            new(ChaveGeral, "Geral", "geral", "CRM · Todos", "Conversa de toda a equipe da CSS Brasil.", new(CorGeral, Destacar: false)),
            new(ChaveGestao, "Gestão", "gestao", "CRM · Gestão", "Gestores e supervisores.", new(CorGestao, Destacar: true)),
        };

        var coresDasRegionais = new Dictionary<Guid, int>();
        var posicao = 0;
        foreach (var r in await db.CrmRegionais.AsNoTracking().Where(r => r.Ativa).OrderBy(r => r.Nome).ToListAsync(ct))
        {
            var cor = CoresDasRegionais[posicao++ % CoresDasRegionais.Length];
            coresDasRegionais[r.Id] = cor;
            // Regional aparece separada na lista de membros ("destacar"): quem é de qual regional fica visível de relance.
            lista.Add(new($"regional:{r.Id}", r.Nome, $"regional-{Slug(r.Nome)}", $"CRM · {r.Nome}", $"Regional {r.Nome}.", new(cor, Destacar: true)));
        }

        var grupos = await db.CrmGrupos.AsNoTracking().Include(g => g.Regional)
            .Where(g => g.Ativo && g.Regional.Ativa).OrderBy(g => g.Regional.Nome).ThenBy(g => g.Nome).ToListAsync(ct);
        foreach (var g in grupos)
        {
            lista.Add(new($"grupo:{g.Id}", $"{g.Regional.Nome} · {g.Nome}", $"grupo-{Slug(g.Regional.Nome)}-{Slug(g.Nome)}",
                $"CRM · {g.Regional.Nome} / {g.Nome}", $"Grupo {g.Nome} da regional {g.Regional.Nome}.", new(coresDasRegionais.GetValueOrDefault(g.RegionalId, CorGeral), Destacar: false)));
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
    private async Task<(int Atualizados, int ForaDoServidor, int Apelidos)> SincronizarMembrosAsync(Dictionary<string, CrmDiscordCanal> mapa, List<string> falhas, CancellationToken ct)
    {
        var vinculos = await db.CrmDiscordVinculos.ToListAsync(ct);
        if (vinculos.Count == 0) return (0, 0, 0);

        var usuarioIds = vinculos.Select(v => v.UsuarioId).ToList();
        var usuarios = (await db.Users.AsNoTracking().Where(u => usuarioIds.Contains(u.Id))
            .Select(u => new { u.Id, u.NomeCompleto, u.Ativo, u.RegionalId, u.GrupoId }).ToListAsync(ct)).ToDictionary(u => u.Id);
        var gestores = (await db.UserRoles.Where(ur => usuarioIds.Contains(ur.UserId))
            .Join(db.Roles.Where(r => Roles.GestaoComercial.Contains(r.Name!)), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Distinct().ToListAsync(ct)).ToHashSet();

        var gerenciados = mapa.Values.Select(m => m.DiscordCargoId).ToHashSet();
        string? CargoDe(string chave) => mapa.TryGetValue(chave, out var m) && m.Ativo ? m.DiscordCargoId : null;

        int atualizados = 0, fora = 0, apelidos = 0;
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
                var membro = await api.ObterMembroAsync(vinculo.DiscordUserId, ct);
                if (membro is null)
                {
                    vinculo.NoServidor = false;
                    fora++;
                    continue;
                }

                var atuais = membro.Cargos;

                vinculo.NoServidor = true;
                var adicionar = desejados.Where(c => !atuais.Contains(c)).ToList();
                var remover = atuais.Where(c => gerenciados.Contains(c) && !desejados.Contains(c)).ToList();
                foreach (var cargo in adicionar) await api.AtribuirCargoAsync(vinculo.DiscordUserId, cargo, ct);
                foreach (var cargo in remover) await api.RemoverCargoAsync(vinculo.DiscordUserId, cargo, ct);
                if (adicionar.Count + remover.Count > 0) atualizados++;

                // Apelido = nome do CRM, mas só para quem ainda não tem apelido: quem escolheu o seu no servidor continua com ele.
                if (usuario.Ativo && string.IsNullOrWhiteSpace(membro.Apelido) && await TentarDefinirApelidoAsync(vinculo.DiscordUserId, usuario.NomeCompleto, ct)) apelidos++;
            }
            catch (DiscordApiException ex)
            {
                falhas.Add($"{vinculo.DiscordNome}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return (atualizados, fora, apelidos);
    }

    /// <summary>
    /// O Discord não deixa o bot trocar o apelido do dono do servidor nem de quem tem cargo acima do bot, e o bot pode estar sem "Gerenciar apelidos":
    /// nesses casos o apelido é só uma gentileza que não saiu — não vira falha da sincronização.
    /// </summary>
    private async Task<bool> TentarDefinirApelidoAsync(string discordUserId, string nomeCompleto, CancellationToken ct)
    {
        try
        {
            await api.DefinirApelidoAsync(discordUserId, ApelidoNoDiscord(nomeCompleto), ct);
            return true;
        }
        catch (DiscordApiException ex)
        {
            logger.LogDebug(ex, "Não foi possível definir o apelido de {DiscordUserId} no Discord.", discordUserId);
            return false;
        }
    }

    private const string ChaveBoasVindas = "discord:boas-vindas";

    /// <summary>
    /// Mensagem de boas-vindas fixada no canal "Geral": explica como usar o Discord com o CRM. Publicada uma única vez (depois disso o
    /// administrador pode editar ou apagar à vontade; o CRM não volta a publicar). Se não conseguir fixar (falta "Gerenciar mensagens"), publica mesmo assim.
    /// </summary>
    private async Task<bool> GarantirBoasVindasAsync(Dictionary<string, CrmDiscordCanal> mapa, CancellationToken ct)
    {
        if (!mapa.TryGetValue(ChaveGeral, out var geral) || string.IsNullOrEmpty(geral.DiscordCanalId) || geral.Desligado) return false;
        if (await db.CrmParametros.AnyAsync(p => p.Chave == ChaveBoasVindas, ct)) return false;

        var link = options.Value.UrlPublica.TrimEnd('/');
        var cartao = new DiscordCartao(
            "👋 Bem-vindo(a) ao Discord da CSS Brasil!",
            "Este servidor conversa com o CRM. Você usa o Discord e o CRM juntos — o que você escreve em um aparece no outro.",
            CorGeral,
            [
                new DiscordCampo("💬 Chat no CRM", link.Length > 0 ? $"Converse pelo CRM em {link}/app/chat — com o seu nome e a sua foto." : "Converse pelo menu Chat do CRM, com o seu nome e a sua foto.", Lado: false),
                new DiscordCampo("👥 Grupos", "Cada regional e cada grupo tem o seu canal. Você só vê os canais do que é seu.", Lado: false),
                new DiscordCampo("📞 Chamadas de voz", "Use o botão Chamada de voz do chat para abrir o canal de voz do grupo ou da conversa.", Lado: false),
                new DiscordCampo("🔔 Avisos no celular", "Instale o app do Discord e deixe as notificações ligadas: lead novo e avisos do CRM chegam por mensagem direta.", Lado: false),
            ],
            "CRM CSS Brasil");

        var mensagem = await api.PublicarCartaoAsync(geral.DiscordCanalId, cartao, ct);
        try
        {
            await api.FixarMensagemAsync(geral.DiscordCanalId, mensagem.Id, ct);
        }
        catch (DiscordApiException ex)
        {
            logger.LogInformation(ex, "Boas-vindas publicadas no Discord, mas não foi possível fixar a mensagem.");
        }

        db.CrmParametros.Add(new CrmParametro { Chave = ChaveBoasVindas, Valor = mensagem.Id });
        await db.SaveChangesAsync(ct);
        return true;
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
