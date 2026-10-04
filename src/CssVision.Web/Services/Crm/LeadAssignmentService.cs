using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LeadAssignmentService(ApplicationDbContext db, ICrmEventHub? eventos = null, TimeProvider? relogio = null) : ILeadAssignmentService
{
    /// <summary>Leads que contam no limite mensal: só os do tráfego pago, sem os do Notion.</summary>
    private IQueryable<CrmLead> LeadsDoTrafegoNoMes() =>
        db.CrmLeads.AsNoTracking().Where(OrigemLead.VeioDoTrafegoPago).Where(l => l.CriadoEm >= InicioDoMes());

    /// <summary>Leads do tráfego pago que o vendedor recebeu hoje (horário de Brasília) — contam no limite diário.</summary>
    private IQueryable<CrmLead> LeadsDoTrafegoHoje()
    {
        var inicioDia = InicioDoDia();
        return db.CrmLeads.AsNoTracking().Where(OrigemLead.VeioDoTrafegoPago)
            .Where(l => (l.ResponsavelAtribuidoEm ?? l.CriadoEm) >= inicioDia);
    }

    /// <summary>Meia-noite de hoje no horário de Brasília (UTC-3, sem horário de verão), em UTC.</summary>
    public static DateTimeOffset InicioDoDia()
    {
        var hojeBrasilia = DateTime.UtcNow.AddHours(-3).Date;
        return new DateTimeOffset(hojeBrasilia, TimeSpan.Zero).AddHours(3);
    }

    private DateTimeOffset Agora => (relogio ?? TimeProvider.System).GetUtcNow();

    private const string ChaveContinuarAte = "distribuicao:continuar-ate";

    private async Task<DateTimeOffset?> ContinuarAteAsync(CancellationToken ct)
    {
        var valor = await db.CrmParametros.AsNoTracking().Where(p => p.Chave == ChaveContinuarAte).Select(p => p.Valor).FirstOrDefaultAsync(ct);
        return DateTimeOffset.TryParse(valor, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ate) && ate > Agora
            ? ate
            : null;
    }

    public async Task<Guid?> ProximoResponsavelAsync(string? oQue, CancellationToken ct) =>
        await ProximoAsync(oQue, await ContinuarAteAsync(ct) is not null, ct);

    private async Task<Guid?> ProximoAsync(string? oQue, bool ignorarLimites, CancellationToken ct)
    {
        var todos = await db.UserRoles
            .Join(db.Roles.Where(r => r.Name == Roles.Comercial), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.Ativo && u.RecebeLeads), id => id, u => u.Id, (_, u) => new { u.Id, u.NomeCompleto, u.LimiteMensalLeads, u.LimiteDiarioLeads, u.RecebeSomenteOQue, u.HorarioInicioLeads, u.HorarioFimLeads, u.DiasSemanaLeads })
            .ToListAsync(ct);

        // "Recebe somente leads de..." só RESTRINGE: quem tem a restrição entra no rodízio apenas dos
        // leads daqueles tipos, em pé de igualdade com os demais (recebe quem pegou menos no mês).
        // Antes o especialista tinha preferência, e quem tinha "AGV" levava todos os leads de AGV.
        var agora = Agora;
        var aptos = todos
            // Fora do dia/horário que o gestor definiu para o consultor: não entra no rodízio agora.
            .Where(v => JanelaRecebimentoLeads.Permite(v.HorarioInicioLeads, v.HorarioFimLeads, v.DiasSemanaLeads, agora))
            .Where(v => FiltroOQue.Aceita(v.RecebeSomenteOQue, oQue))
            .Select(v => (v.Id, v.NomeCompleto, v.LimiteMensalLeads, v.LimiteDiarioLeads, Especialista: v.RecebeSomenteOQue is not null))
            .ToList();
        return await EscolherAsync(aptos, ignorarLimites, ct);
    }

    private async Task<Guid?> EscolherAsync(
        List<(Guid Id, string NomeCompleto, int? LimiteMensalLeads, int? LimiteDiarioLeads, bool Especialista)> vendedores, bool ignorarLimites, CancellationToken ct)
    {
        if (vendedores.Count == 0) return null;

        var ids = vendedores.Select(v => v.Id).ToList();

        var recebidosNoMes = await LeadsDoTrafegoNoMes()
            .Where(l => l.ResponsavelId != null && ids.Contains(l.ResponsavelId.Value))
            .GroupBy(l => l.ResponsavelId!.Value)
            .Select(g => new { ResponsavelId = g.Key, Quantidade = g.Count() })
            .ToDictionaryAsync(x => x.ResponsavelId, x => x.Quantidade, ct);

        var recebidosHoje = await LeadsDoTrafegoHoje()
            .Where(l => l.ResponsavelId != null && ids.Contains(l.ResponsavelId.Value))
            .GroupBy(l => l.ResponsavelId!.Value)
            .Select(g => new { ResponsavelId = g.Key, Quantidade = g.Count() })
            .ToDictionaryAsync(x => x.ResponsavelId, x => x.Quantidade, ct);

        return vendedores
            .Select(v => new { v.Id, v.NomeCompleto, v.LimiteMensalLeads, v.LimiteDiarioLeads, v.Especialista, Recebidos = recebidosNoMes.GetValueOrDefault(v.Id), Hoje = recebidosHoje.GetValueOrDefault(v.Id) })
            .Where(v => ignorarLimites || v.LimiteMensalLeads is null || v.Recebidos < v.LimiteMensalLeads)
            .Where(v => ignorarLimites || v.LimiteDiarioLeads is null || v.Hoje < v.LimiteDiarioLeads)
            .OrderBy(v => v.Recebidos)
            // Empate: o especialista (que só pode receber esse tipo) vem primeiro.
            .ThenByDescending(v => v.Especialista)
            .ThenBy(v => v.NomeCompleto, StringComparer.OrdinalIgnoreCase)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefault();
    }

    public async Task<bool> PodeReceberAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.Ativo, u.LimiteMensalLeads, u.LimiteDiarioLeads, u.HorarioInicioLeads, u.HorarioFimLeads, u.DiasSemanaLeads })
            .FirstOrDefaultAsync(ct);
        if (usuario is null || !usuario.Ativo) return false;
        if (!JanelaRecebimentoLeads.Permite(usuario.HorarioInicioLeads, usuario.HorarioFimLeads, usuario.DiasSemanaLeads, Agora)) return false;

        if (await ContinuarAteAsync(ct) is not null) return true;
        if (usuario.LimiteMensalLeads is { } mensal
            && await LeadsDoTrafegoNoMes().CountAsync(l => l.ResponsavelId == usuarioId, ct) >= mensal) return false;
        if (usuario.LimiteDiarioLeads is { } diario
            && await LeadsDoTrafegoHoje().CountAsync(l => l.ResponsavelId == usuarioId, ct) >= diario) return false;
        return true;
    }

    public async Task<int> DistribuirPendentesAsync(CancellationToken ct)
    {
        var pendentes = await db.CrmLeads
            .Where(OrigemLead.VeioDoTrafegoPago)
            .Where(l => l.ResponsavelId == null && !l.Arquivado)
            .OrderBy(l => l.CriadoEm)
            .Take(200)
            .ToListAsync(ct);

        var distribuidos = 0;
        foreach (var lead in pendentes)
        {
            var responsavelId = await ProximoResponsavelAsync(lead.ProdutoInteresse, ct);
            if (responsavelId is null) break; // ninguém disponível agora: tenta de novo no próximo ciclo
            lead.ResponsavelId = responsavelId;
            await db.SaveChangesAsync(ct); // um por vez: o rodízio olha quantos cada um já recebeu
            distribuidos++;
        }

        if (distribuidos > 0) eventos?.PublicarQuadroAtualizado("distribuicao");
        return distribuidos;
    }

    public async Task<AlertaDistribuicaoDto> ObterEstadoDistribuicaoAsync(CancellationToken ct)
    {
        var continuarAte = await ContinuarAteAsync(ct);
        var porTipo = (await db.CrmLeads.AsNoTracking()
                .Where(OrigemLead.VeioDoTrafegoPago)
                .Where(l => l.ResponsavelId == null && !l.Arquivado)
                .Select(l => l.ProdutoInteresse)
                .ToListAsync(ct))
            .GroupBy(oQue => oQue)
            .ToList();
        var semResponsavel = porTipo.Sum(g => g.Count());

        // Parado "por limite": com os limites alguém não receberia o lead, mas sem eles receberia.
        var bloqueados = 0;
        if (continuarAte is null)
        {
            foreach (var grupo in porTipo)
            {
                if (await ProximoAsync(grupo.Key, false, ct) is null && await ProximoAsync(grupo.Key, true, ct) is not null)
                {
                    bloqueados += grupo.Count();
                }
            }
        }

        var agora = Agora;
        var consultores = await db.UserRoles
            .Join(db.Roles.Where(r => r.Name == Roles.Comercial), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.Ativo && u.RecebeLeads), id => id, u => u.Id,
                (_, u) => new { u.Id, u.LimiteMensalLeads, u.LimiteDiarioLeads, u.HorarioInicioLeads, u.HorarioFimLeads, u.DiasSemanaLeads })
            .ToListAsync(ct);
        consultores = consultores
            .Where(c => JanelaRecebimentoLeads.Permite(c.HorarioInicioLeads, c.HorarioFimLeads, c.DiasSemanaLeads, agora))
            .ToList();
        var ids = consultores.Select(c => c.Id).ToList();
        var noMes = await LeadsDoTrafegoNoMes().Where(l => l.ResponsavelId != null && ids.Contains(l.ResponsavelId.Value))
            .GroupBy(l => l.ResponsavelId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var hoje = await LeadsDoTrafegoHoje().Where(l => l.ResponsavelId != null && ids.Contains(l.ResponsavelId.Value))
            .GroupBy(l => l.ResponsavelId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var noLimiteDiario = consultores.Count(c => c.LimiteDiarioLeads is { } d && hoje.GetValueOrDefault(c.Id) >= d);
        var noLimiteMensal = consultores.Count(c => c.LimiteMensalLeads is { } m && noMes.GetValueOrDefault(c.Id) >= m);

        return new AlertaDistribuicaoDto(bloqueados > 0, semResponsavel, bloqueados, consultores.Count, noLimiteDiario, noLimiteMensal, continuarAte);
    }

    public async Task DefinirContinuarAposLimiteAsync(bool continuar, CancellationToken ct)
    {
        var parametro = await db.CrmParametros.FirstOrDefaultAsync(p => p.Chave == ChaveContinuarAte, ct);
        if (!continuar)
        {
            if (parametro is not null) db.CrmParametros.Remove(parametro);
        }
        else
        {
            // Até o fim do dia em Brasília: amanhã o limite diário volta a valer sozinho.
            var fimDoDia = InicioDoDia().AddDays(1);
            var valor = fimDoDia.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            if (parametro is null) db.CrmParametros.Add(new CrmParametro { Chave = ChaveContinuarAte, Valor = valor });
            else parametro.Valor = valor;
        }
        await db.SaveChangesAsync(ct);
        eventos?.PublicarQuadroAtualizado("distribuicao");
        if (continuar) await DistribuirPendentesAsync(ct);
    }

    public async Task<NovosLeadsDto> NovosLeadsAsync(Guid usuarioId, DateTimeOffset? desde, CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        if (desde is null) return new NovosLeadsDto(agora, []);

        var leads = await db.CrmLeads.AsNoTracking()
            .Where(l => l.ResponsavelId == usuarioId && !l.Arquivado
                && l.ResponsavelAtribuidoEm > desde && l.ResponsavelAtribuidoEm <= agora
                // Lead que a própria pessoa cadastrou ou pegou pra si não é novidade pra ela.
                && (l.AtualizadoPorId ?? l.CriadoPorId) != usuarioId)
            .OrderByDescending(l => l.ResponsavelAtribuidoEm)
            .Take(20)
            .Select(l => new NovoLeadDto(l.Id, l.NomeOuRazaoSocial, l.ResponsavelAtribuidoEm!.Value))
            .ToListAsync(ct);
        return new NovosLeadsDto(agora, leads);
    }

    private static DateTimeOffset InicioDoMes() =>
        new(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), TimeSpan.Zero);
}
