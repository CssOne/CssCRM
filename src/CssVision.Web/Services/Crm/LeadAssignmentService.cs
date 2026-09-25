using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LeadAssignmentService(ApplicationDbContext db, ICrmEventHub? eventos = null) : ILeadAssignmentService
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

    public async Task<Guid?> ProximoResponsavelAsync(string? oQue, CancellationToken ct)
    {
        var todos = await db.UserRoles
            .Join(db.Roles.Where(r => r.Name == Roles.Comercial), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.Ativo), id => id, u => u.Id, (_, u) => new { u.Id, u.NomeCompleto, u.LimiteMensalLeads, u.LimiteDiarioLeads, u.RecebeSomenteOQue })
            .ToListAsync(ct);

        // Especialistas (ex.: só AGV TRUCK) ficam fora do rodízio geral; nos leads da especialidade
        // deles, têm a preferência — os demais só recebem se nenhum especialista estiver disponível.
        var especialistas = todos.Where(v => FiltroOQue.Aceita(v.RecebeSomenteOQue, oQue) && v.RecebeSomenteOQue is not null).ToList();
        var gerais = todos.Where(v => v.RecebeSomenteOQue is null).ToList();

        return await EscolherAsync(especialistas.Select(v => (v.Id, v.NomeCompleto, v.LimiteMensalLeads, v.LimiteDiarioLeads)).ToList(), ct)
            ?? await EscolherAsync(gerais.Select(v => (v.Id, v.NomeCompleto, v.LimiteMensalLeads, v.LimiteDiarioLeads)).ToList(), ct);
    }

    private async Task<Guid?> EscolherAsync(List<(Guid Id, string NomeCompleto, int? LimiteMensalLeads, int? LimiteDiarioLeads)> vendedores, CancellationToken ct)
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
            .Select(v => new { v.Id, v.NomeCompleto, v.LimiteMensalLeads, v.LimiteDiarioLeads, Recebidos = recebidosNoMes.GetValueOrDefault(v.Id), Hoje = recebidosHoje.GetValueOrDefault(v.Id) })
            .Where(v => v.LimiteMensalLeads is null || v.Recebidos < v.LimiteMensalLeads)
            .Where(v => v.LimiteDiarioLeads is null || v.Hoje < v.LimiteDiarioLeads)
            .OrderBy(v => v.Recebidos)
            .ThenBy(v => v.NomeCompleto, StringComparer.OrdinalIgnoreCase)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefault();
    }

    public async Task<bool> PodeReceberAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.Ativo, u.LimiteMensalLeads, u.LimiteDiarioLeads })
            .FirstOrDefaultAsync(ct);
        if (usuario is null || !usuario.Ativo) return false;

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
