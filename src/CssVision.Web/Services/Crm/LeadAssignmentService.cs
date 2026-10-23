using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CssVision.Web.Services.Crm;

public sealed class LeadAssignmentService(
    ApplicationDbContext db, ICrmEventHub? eventos = null, TimeProvider? relogio = null, IOptions<DistribuicaoOptions>? opcoes = null) : ILeadAssignmentService
{
    /// <summary>Regionais exclusivas (nomes normalizados): seus consultores só recebem leads marcados com a regional.</summary>
    private HashSet<string> RegionaisExclusivas =>
        (opcoes?.Value.RegionaisExclusivas ?? []).Select(NomeDeRegional.Normalizar).Where(r => r is not null).Select(r => r!).ToHashSet();

    /// <summary>
    /// Leads que contam no limite mensal: só os do tráfego pago, sem os do Notion. Lead excluído (arquivado) não conta: a vez do
    /// consultor que o recebeu não foi "gasta", então ele volta a ser o próximo do rodízio. O lead é de quem o tem agora, no mês em que passou
    /// para ele: atribuído a outro consultor, sai da conta de quem o tinha e entra na de quem recebeu (ver <see cref="OrigemLead.ComPosseDesde"/>).
    /// </summary>
    private IQueryable<CrmLead> LeadsDoTrafegoNoMes() =>
        db.CrmLeads.AsNoTracking().Where(OrigemLead.VeioDoTrafegoPago).Where(l => !l.Arquivado).Where(OrigemLead.ComPosseDesde(InicioDoMes()));

    /// <summary>Leads do tráfego pago que o vendedor recebeu hoje (horário de Brasília) — contam no limite diário.</summary>
    private IQueryable<CrmLead> LeadsDoTrafegoHoje()
    {
        var inicioDia = InicioDoDia();
        return db.CrmLeads.AsNoTracking().Where(OrigemLead.VeioDoTrafegoPago).Where(l => !l.Arquivado)
            .Where(l => (l.ResponsavelAtribuidoEm ?? l.CriadoEm) >= inicioDia);
    }

    /// <summary>Meia-noite de hoje no horário de Brasília (UTC-3, sem horário de verão), em UTC.</summary>
    public static DateTimeOffset InicioDoDia() => InicioDoDia(DateTimeOffset.UtcNow);

    public static DateTimeOffset InicioDoDia(DateTimeOffset agora)
    {
        var hojeBrasilia = agora.UtcDateTime.AddHours(-3).Date;
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

    /// <summary>
    /// Quem entra no rodízio: usuário ativo com "Recebe leads" ligado que é consultor (papel Comercial) ou administrador que atua nas vendas (visão total com
    /// "Pega leads / atua nas vendas" marcado). A Gestão comercial já lista esses administradores com a opção "Recebe leads"; sem isto o rodízio os ignorava.
    /// </summary>
    private IQueryable<Domain.Identity.ApplicationUser> QuemRecebeLeads() =>
        db.Users.Where(u => u.Ativo && u.RecebeLeads
            && db.UserRoles.Any(ur => ur.UserId == u.Id
                && db.Roles.Any(r => r.Id == ur.RoleId && (r.Name == Roles.Comercial || (u.AtuaNasVendas && Roles.VisaoTotal.Contains(r.Name!))))));

    public Task<Guid?> ProximoResponsavelAsync(string? oQue, CancellationToken ct) => ProximoResponsavelAsync(oQue, null, ct);

    public async Task<Guid?> ProximoResponsavelAsync(string? oQue, string? regional, CancellationToken ct)
    {
        // Só entra no rodízio quem está dentro do dia e do horário de recebimento definidos na Gestão comercial, e abaixo dos limites.
        var normal = await ProximoAsync(oQue, regional, false, false, ct);
        if (normal is not null) return normal;

        // "Continuar distribuindo hoje" do gestor afrouxa só os LIMITES (diário/mensal). A agenda de dias e horários nunca é ignorada:
        // quem está fora dela não recebe lead, nem por esse modo, nem ao ter o "Recebe leads" ligado. Sem ninguém dentro da agenda o lead
        // espera sem responsável até alguém entrar no horário (o gestor é avisado e pode ajustar os horários).
        return await ContinuarAteAsync(ct) is null ? null : await ProximoAsync(oQue, regional, true, false, ct);
    }

    private async Task<Guid?> ProximoAsync(string? oQue, string? regional, bool ignorarLimites, bool ignorarHorario, CancellationToken ct)
    {
        var todos = await QuemRecebeLeads()
            .Select(u => new { u.Id, u.NomeCompleto, u.LimiteMensalLeads, u.LimiteDiarioLeads, u.RecebeSomenteOQue, u.HorarioInicioLeads, u.HorarioFimLeads, u.DiasSemanaLeads, RegionalNome = u.Regional != null ? u.Regional.Nome : null })
            .ToListAsync(ct);

        // Regional do lead: com ela, só concorrem os consultores dela. Sem ela, ficam de fora os das regionais exclusivas.
        var regionalDoLead = NomeDeRegional.Normalizar(regional);
        var exclusivas = RegionaisExclusivas;
        todos = todos.Where(v =>
        {
            var regionalDoConsultor = NomeDeRegional.Normalizar(v.RegionalNome);
            return regionalDoLead is not null
                ? regionalDoConsultor == regionalDoLead
                : regionalDoConsultor is null || !exclusivas.Contains(regionalDoConsultor);
        }).ToList();

        // "Recebe somente leads de..." só RESTRINGE: quem tem a restrição entra no rodízio apenas dos
        // leads daqueles tipos, em pé de igualdade com os demais (recebe quem pegou menos no mês).
        // Antes o especialista tinha preferência, e quem tinha "AGV" levava todos os leads de AGV.
        var agora = Agora;
        var aptos = todos
            // Fora do dia/horário que o gestor definiu para o consultor: não entra no rodízio agora.
            .Where(v => ignorarHorario || JanelaRecebimentoLeads.Permite(v.HorarioInicioLeads, v.HorarioFimLeads, v.DiasSemanaLeads, agora))
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

        // Último lead (não excluído) que cada um recebeu: no empate, quem espera há mais tempo (ou nunca recebeu) vem primeiro.
        var ultimaAtribuicao = (await db.CrmLeads.AsNoTracking()
                .Where(OrigemLead.VeioDoTrafegoPago)
                .Where(l => !l.Arquivado && l.ResponsavelId != null && ids.Contains(l.ResponsavelId.Value))
                .GroupBy(l => l.ResponsavelId!.Value)
                .Select(g => new { ResponsavelId = g.Key, Ultima = g.Max(l => l.ResponsavelAtribuidoEm ?? l.CriadoEm) })
                .ToListAsync(ct))
            .ToDictionary(x => x.ResponsavelId, x => x.Ultima);

        return vendedores
            .Select(v => new { v.Id, v.NomeCompleto, v.LimiteMensalLeads, v.LimiteDiarioLeads, v.Especialista, Recebidos = recebidosNoMes.GetValueOrDefault(v.Id), Hoje = recebidosHoje.GetValueOrDefault(v.Id), Ultima = ultimaAtribuicao.TryGetValue(v.Id, out var u) ? u : DateTimeOffset.MinValue })
            .Where(v => ignorarLimites || v.LimiteMensalLeads is null || v.Recebidos < v.LimiteMensalLeads)
            .Where(v => ignorarLimites || v.LimiteDiarioLeads is null || v.Hoje < v.LimiteDiarioLeads)
            // Quem ainda não recebeu hoje vem antes de quem já recebeu: todos entram no rodízio do dia, mesmo quem pegou mais
            // no mês (antes, só quem estava abaixo na contagem do mês recebia, e quem estava na frente passava o dia sem lead).
            .OrderBy(v => v.Hoje)
            .ThenBy(v => v.Recebidos)
            // Empate: o especialista (que só pode receber esse tipo) vem primeiro.
            .ThenByDescending(v => v.Especialista)
            // Depois, quem recebeu o último lead há mais tempo; o nome só desempata quem nunca recebeu / recebeu no mesmo instante.
            .ThenBy(v => v.Ultima)
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
        // Dia e horário de recebimento valem sempre — o "Continuar distribuindo hoje" afrouxa só os limites; um consultor fora da agenda
        // só recebe por ele quando ninguém dentro da agenda pode (isso é decidido no rodízio, em ProximoResponsavelAsync).
        if (!JanelaRecebimentoLeads.Permite(usuario.HorarioInicioLeads, usuario.HorarioFimLeads, usuario.DiasSemanaLeads, Agora)) return false;

        var continuar = await ContinuarAteAsync(ct) is not null;
        if (continuar) return true;
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
        // Combinações (tipo, regional) sem ninguém disponível neste ciclo: não repete a consulta, e um lead de uma equipe parada não
        // trava os das outras (ficam para o próximo ciclo).
        var semDisponivel = new HashSet<(string?, string?)>();
        foreach (var lead in pendentes)
        {
            var chave = (lead.ProdutoInteresse, NomeDeRegional.Normalizar(lead.Regional));
            if (semDisponivel.Contains(chave)) continue;
            var responsavelId = await ProximoResponsavelAsync(lead.ProdutoInteresse, lead.Regional, ct);
            if (responsavelId is null) { semDisponivel.Add(chave); continue; } // tenta de novo no próximo ciclo
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
                .Select(l => new { l.ProdutoInteresse, l.Regional })
                .ToListAsync(ct))
            .GroupBy(l => (OQue: l.ProdutoInteresse, Regional: NomeDeRegional.Normalizar(l.Regional)))
            .ToList();
        var semResponsavel = porTipo.Sum(g => g.Count());

        // Parado por limite ou por horário. Por limite: alguém dentro da agenda receberia se o limite fosse ignorado (o gestor pode liberar). Por
        // horário: ninguém está dentro do dia/horário de recebimento — a agenda não se ignora, então o lead espera até alguém entrar nela.
        int bloqueados = 0, porLimite = 0, porHorario = 0;
        foreach (var grupo in porTipo)
        {
            if (await ProximoAsync(grupo.Key.OQue, grupo.Key.Regional, false, false, ct) is not null) continue;

            var quantidade = grupo.Count();
            if (await ProximoAsync(grupo.Key.OQue, grupo.Key.Regional, true, false, ct) is not null)
            {
                // Com o modo "continuar" ligado esse lead já está sendo entregue: não conta como parado.
                if (continuarAte is not null) continue;
                bloqueados += quantidade;
                porLimite += quantidade;
                continue;
            }

            // Ninguém dentro da agenda. Só avisa se existe alguém que receberia fora dela (senão não há consultor nenhum: não é horário).
            if (await ProximoAsync(grupo.Key.OQue, grupo.Key.Regional, true, true, ct) is null) continue;
            bloqueados += quantidade;
            porHorario += quantidade;
        }

        var agora = Agora;
        var consultores = await QuemRecebeLeads()
            .Select(u => new { u.Id, u.LimiteMensalLeads, u.LimiteDiarioLeads, u.HorarioInicioLeads, u.HorarioFimLeads, u.DiasSemanaLeads })
            .ToListAsync(ct);
        var totalAtivos = consultores.Count;
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

        return new AlertaDistribuicaoDto(bloqueados > 0, semResponsavel, porLimite, consultores.Count, noLimiteDiario, noLimiteMensal, continuarAte,
            bloqueados, porHorario, totalAtivos - consultores.Count);
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
            // Até o fim do dia em Brasília: amanhã os limites e o horário voltam a valer sozinhos.
            var fimDoDia = InicioDoDia(Agora).AddDays(1);
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
        HorarioBrasilia.Inicio(HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje));
}
