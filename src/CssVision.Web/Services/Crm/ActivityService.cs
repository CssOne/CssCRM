using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class ActivityService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEquipeComercialService equipe,
    IAuditSink audit) : IActivityService
{
    public async Task<PagedResult<ActivityDto>> ListarAsync(ActivityFilterRequest filtro, CancellationToken ct)
    {
        var query = db.CrmActivities.AsNoTracking()
            .Include(a => a.Lead)
            .Include(a => a.Opportunity)
            .Include(a => a.Responsavel)
            .Where(a => !a.Arquivado)
            .AsQueryable();

        if (filtro.ResponsavelId.HasValue)
        {
            if (!await equipe.PodeAcessarVendedorAsync(filtro.ResponsavelId.Value, ct)) throw new CrmForbiddenException();
            query = query.Where(a => a.ResponsavelId == filtro.ResponsavelId);
        }
        else if (filtro.Visao == VisaoAtividade.Minhas)
        {
            query = query.Where(a => a.ResponsavelId == currentUser.UserId);
        }
        else
        {
            var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
            if (visiveis is not null) query = query.Where(a => visiveis.Contains(a.ResponsavelId));
        }

        if (filtro.Tipo.HasValue) query = query.Where(a => a.Tipo == filtro.Tipo);
        if (filtro.LeadId.HasValue) query = query.Where(a => a.LeadId == filtro.LeadId);

        var agora = DateTimeOffset.UtcNow;
        var referencia = filtro.DataReferencia ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var inicioDia = referencia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fimDia = referencia.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        query = filtro.Visao switch
        {
            VisaoAtividade.Hoje => query.Where(a => a.Status == StatusAtividade.Pendente && a.DataHoraPrevista >= inicioDia && a.DataHoraPrevista <= fimDia),
            VisaoAtividade.Proximas => query.Where(a => a.Status == StatusAtividade.Pendente && a.DataHoraPrevista > agora),
            VisaoAtividade.Atrasadas => query.Where(a => a.Status == StatusAtividade.Pendente && a.DataHoraPrevista < agora),
            VisaoAtividade.Concluidas => query.Where(a => a.Status == StatusAtividade.Concluida),
            VisaoAtividade.Semana => query.Where(a => a.DataHoraPrevista >= InicioDaSemana(referencia) && a.DataHoraPrevista < InicioDaSemana(referencia).AddDays(7)),
            _ => query
        };

        var total = await query.CountAsync(ct);

        var pagina = await query
            .OrderBy(a => a.DataHoraPrevista)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .ToListAsync(ct);

        var itens = pagina.Select(a => ParaDto(a, agora)).ToList();

        return new PagedResult<ActivityDto>
        {
            Itens = itens,
            Pagina = filtro.Pagina,
            TamanhoPagina = filtro.TamanhoPagina,
            TotalRegistros = total
        };
    }

    public async Task<ActivityDto> CriarAsync(ActivityCreateRequest request, CancellationToken ct)
    {
        var lead = await db.CrmLeads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct)
            ?? throw new CrmNotFoundException("Lead", request.LeadId);

        var responsavelId = request.ResponsavelId ?? currentUser.UserId;
        if (!await equipe.PodeAcessarVendedorAsync(responsavelId, ct))
        {
            throw new CrmForbiddenException("Você não pode criar atividades para este vendedor.");
        }

        if (!await equipe.PodeAcessarVendedorAsync(lead.ResponsavelId ?? Guid.Empty, ct))
        {
            throw new CrmForbiddenException();
        }

        if (request.OpportunityId.HasValue)
        {
            var pertence = await db.CrmOpportunities.AnyAsync(o => o.Id == request.OpportunityId && o.LeadId == lead.Id, ct);
            if (!pertence) throw new CrmBusinessException("A oportunidade informada não pertence a este lead.", "oportunidade_invalida");
        }

        var atividade = new CrmActivity
        {
            LeadId = lead.Id,
            OpportunityId = request.OpportunityId,
            ResponsavelId = responsavelId,
            Tipo = request.Tipo,
            Assunto = request.Assunto.Trim(),
            Descricao = request.Descricao,
            DataHoraPrevista = request.DataHoraPrevista,
            LembreteMinutosAntes = request.LembreteMinutosAntes
        };

        db.CrmActivities.Add(atividade);
        await db.SaveChangesAsync(ct);
        await RecalcularDatasContatoAsync(lead.Id, ct);
        await audit.RegistrarAsync("AtividadeCriada", nameof(CrmActivity), atividade.Id, new { atividade.Tipo, atividade.Assunto }, ct);

        return await ObterDtoAsync(atividade.Id, ct);
    }

    public async Task<ActivityDto> AtualizarAsync(Guid id, ActivityUpdateRequest request, CancellationToken ct)
    {
        var atividade = await CarregarComEscopoAsync(id, ct);
        db.Entry(atividade).Property(a => a.RowVersion).OriginalValue = request.RowVersion;

        atividade.Tipo = request.Tipo;
        atividade.Assunto = request.Assunto.Trim();
        atividade.Descricao = request.Descricao;
        atividade.DataHoraPrevista = request.DataHoraPrevista;
        atividade.LembreteMinutosAntes = request.LembreteMinutosAntes;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await RecalcularDatasContatoAsync(atividade.LeadId, ct);
        return await ObterDtoAsync(atividade.Id, ct);
    }

    public async Task<ActivityDto> ConcluirAsync(Guid id, ActivityCompleteRequest request, CancellationToken ct)
    {
        var atividade = await CarregarComEscopoAsync(id, ct);
        db.Entry(atividade).Property(a => a.RowVersion).OriginalValue = request.RowVersion;

        if (atividade.Status == StatusAtividade.Concluida)
        {
            throw new CrmBusinessException("Esta atividade já está concluída.", "atividade_ja_concluida");
        }

        atividade.Status = StatusAtividade.Concluida;
        atividade.Resultado = request.Resultado;
        atividade.DataHoraConclusao = request.DataHoraConclusao ?? DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await RecalcularDatasContatoAsync(atividade.LeadId, ct);
        await audit.RegistrarAsync("AtividadeConcluida", nameof(CrmActivity), atividade.Id, new { atividade.Resultado }, ct);

        return await ObterDtoAsync(atividade.Id, ct);
    }

    // --- auxiliares ---

    private async Task<CrmActivity> CarregarComEscopoAsync(Guid id, CancellationToken ct)
    {
        var atividade = await db.CrmActivities.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new CrmNotFoundException("Atividade", id);

        if (!await equipe.PodeAcessarVendedorAsync(atividade.ResponsavelId, ct))
        {
            throw new CrmForbiddenException();
        }

        return atividade;
    }

    private async Task<ActivityDto> ObterDtoAsync(Guid id, CancellationToken ct)
    {
        var atividade = await db.CrmActivities.AsNoTracking()
            .Include(a => a.Lead)
            .Include(a => a.Opportunity)
            .Include(a => a.Responsavel)
            .FirstAsync(a => a.Id == id, ct);

        return ParaDto(atividade, DateTimeOffset.UtcNow);
    }

    private async Task RecalcularDatasContatoAsync(Guid leadId, CancellationToken ct)
    {
        var lead = await db.CrmLeads.FirstAsync(l => l.Id == leadId, ct);

        lead.UltimoContatoEm = await db.CrmActivities.AsNoTracking()
            .Where(a => a.LeadId == leadId && a.Status == StatusAtividade.Concluida)
            .OrderByDescending(a => a.DataHoraConclusao)
            .Select(a => a.DataHoraConclusao)
            .FirstOrDefaultAsync(ct);

        lead.ProximoContatoEm = await db.CrmActivities.AsNoTracking()
            .Where(a => a.LeadId == leadId && a.Status == StatusAtividade.Pendente)
            .OrderBy(a => a.DataHoraPrevista)
            .Select(a => (DateTimeOffset?)a.DataHoraPrevista)
            .FirstOrDefaultAsync(ct);

        await db.SaveChangesAsync(ct);
    }

    private static DateTimeOffset InicioDaSemana(DateOnly referencia)
    {
        var data = referencia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var diasParaSegunda = ((int)data.DayOfWeek + 6) % 7;
        return data.AddDays(-diasParaSegunda);
    }

    private static ActivityDto ParaDto(CrmActivity a, DateTimeOffset agora) => new(
        a.Id, a.LeadId, a.Lead.NomeOuRazaoSocial, a.OpportunityId, a.Opportunity?.Titulo,
        a.ResponsavelId, a.Responsavel.NomeCompleto, a.Tipo, a.Assunto, a.Descricao,
        a.DataHoraPrevista, a.DataHoraConclusao, a.Resultado, a.Status, a.LembreteMinutosAntes,
        a.Status == StatusAtividade.Pendente && a.DataHoraPrevista < agora, a.RowVersion);
}
