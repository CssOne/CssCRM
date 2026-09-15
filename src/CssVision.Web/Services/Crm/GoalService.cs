using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class GoalService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEquipeComercialService equipe,
    UserManager<ApplicationUser> userManager) : IGoalService
{
    /// <summary>
    /// Sempre lista todo consultor visível para o mês, com ou sem meta já cadastrada (Id nulo
    /// quando ainda não há), em vez de só quem já tem uma <see cref="CrmSalesGoal"/> — senão um
    /// administrador/gestor não enxerga quem ainda está sem meta definida.
    /// </summary>
    public async Task<IReadOnlyList<SalesGoalDto>> ListarAsync(DateOnly? mesReferencia, CancellationToken ct)
    {
        var mes = NormalizarMes(mesReferencia ?? DateOnly.FromDateTime(DateTime.UtcNow));
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);

        var consultores = (await userManager.GetUsersInRoleAsync(Roles.Comercial))
            .Where(u => u.Ativo && (visiveis is null || visiveis.Contains(u.Id)))
            .ToList();
        var vendedorIds = consultores.Select(c => c.Id).ToList();

        var metas = await db.CrmSalesGoals.AsNoTracking()
            .Where(g => g.MesReferencia == mes && vendedorIds.Contains(g.VendedorId))
            .ToListAsync(ct);
        var metaPorVendedor = metas.ToDictionary(m => m.VendedorId);

        var inicioMes = mes.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fimMes = mes.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var realizadoPorVendedor = await db.CrmOpportunities.AsNoTracking()
            .Where(o => vendedorIds.Contains(o.ResponsavelId) && o.Etapa.Tipo == TipoEtapaPipeline.Ganho &&
                        o.DataEfetivaFechamento >= inicioMes && o.DataEfetivaFechamento < fimMes)
            .GroupBy(o => o.ResponsavelId)
            .Select(g => new { VendedorId = g.Key, Valor = g.Sum(o => o.ValorFinal ?? o.ValorEstimado), Quantidade = g.Count() })
            .ToListAsync(ct);

        var realizadoMap = realizadoPorVendedor.ToDictionary(r => r.VendedorId);

        return consultores
            .Select(c =>
            {
                metaPorVendedor.TryGetValue(c.Id, out var meta);
                realizadoMap.TryGetValue(c.Id, out var realizado);
                return new SalesGoalDto(meta?.Id, c.Id, c.NomeCompleto, mes,
                    meta?.MetaQuantidadeVendas, meta?.MetaValor, realizado?.Valor ?? 0m, realizado?.Quantidade ?? 0);
            })
            .OrderBy(d => d.VendedorNome)
            .ToList();
    }

    public async Task<SalesGoalDto> DefinirMetaAsync(SalesGoalUpsertRequest request, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem definir metas.");
        }

        if (!await equipe.PodeAcessarVendedorAsync(request.VendedorId, ct))
        {
            throw new CrmForbiddenException("Você não pode definir metas para este vendedor.");
        }

        if (request.MetaQuantidadeVendas < 0)
        {
            throw new CrmBusinessException("A meta de quantidade não pode ser negativa.", "meta_invalida");
        }

        if (request.MetaValor is < 0)
        {
            throw new CrmBusinessException("A meta de valor não pode ser negativa.", "meta_invalida");
        }

        var mes = NormalizarMes(request.MesReferencia);

        var meta = await db.CrmSalesGoals.Include(g => g.Vendedor)
            .FirstOrDefaultAsync(g => g.VendedorId == request.VendedorId && g.MesReferencia == mes, ct);

        if (meta is null)
        {
            var vendedor = await db.Users.FirstOrDefaultAsync(u => u.Id == request.VendedorId, ct)
                ?? throw new CrmNotFoundException("Vendedor", request.VendedorId);
            meta = new CrmSalesGoal { VendedorId = vendedor.Id, Vendedor = vendedor, MesReferencia = mes };
            db.CrmSalesGoals.Add(meta);
        }

        meta.MetaQuantidadeVendas = request.MetaQuantidadeVendas;
        meta.MetaValor = request.MetaValor;

        await db.SaveChangesAsync(ct);

        return new SalesGoalDto(meta.Id, meta.VendedorId, meta.Vendedor.NomeCompleto, meta.MesReferencia, meta.MetaQuantidadeVendas, meta.MetaValor, 0m, 0);
    }

    /// <summary>
    /// Lista todas as regionais ativas, com ou sem meta geral já cadastrada (Id nulo quando ainda
    /// não há) — restrito a administradores, que são quem define essas metas.
    /// </summary>
    public async Task<IReadOnlyList<RegionalGoalDto>> ListarRegionaisAsync(DateOnly? mesReferencia, CancellationToken ct)
    {
        if (!currentUser.TemVisaoTotal)
        {
            throw new CrmForbiddenException("Apenas administradores podem ver as metas gerais por regional.");
        }

        var mes = NormalizarMes(mesReferencia ?? DateOnly.FromDateTime(DateTime.UtcNow));

        var regionais = await db.CrmRegionais.AsNoTracking()
            .Where(r => r.Ativa)
            .OrderBy(r => r.Nome)
            .ToListAsync(ct);
        var regionalIds = regionais.Select(r => r.Id).ToList();

        var metas = await db.CrmRegionalGoals.AsNoTracking()
            .Where(g => g.MesReferencia == mes && regionalIds.Contains(g.RegionalId))
            .ToListAsync(ct);
        var metaPorRegional = metas.ToDictionary(m => m.RegionalId);

        var inicioMes = mes.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fimMes = mes.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var realizadoPorRegional = await db.CrmOpportunities.AsNoTracking()
            .Where(o => o.Etapa.Tipo == TipoEtapaPipeline.Ganho &&
                        o.DataEfetivaFechamento >= inicioMes && o.DataEfetivaFechamento < fimMes &&
                        o.Responsavel.RegionalId != null && regionalIds.Contains(o.Responsavel.RegionalId.Value))
            .GroupBy(o => o.Responsavel.RegionalId!.Value)
            .Select(g => new { RegionalId = g.Key, Valor = g.Sum(o => o.ValorFinal ?? o.ValorEstimado), Quantidade = g.Count() })
            .ToListAsync(ct);

        var realizadoMap = realizadoPorRegional.ToDictionary(r => r.RegionalId);

        return regionais
            .Select(r =>
            {
                metaPorRegional.TryGetValue(r.Id, out var meta);
                realizadoMap.TryGetValue(r.Id, out var realizado);
                return new RegionalGoalDto(meta?.Id, r.Id, r.Nome, mes,
                    meta?.MetaQuantidadeVendas, meta?.MetaValor, realizado?.Valor ?? 0m, realizado?.Quantidade ?? 0);
            })
            .ToList();
    }

    public async Task<RegionalGoalDto> DefinirMetaRegionalAsync(RegionalGoalUpsertRequest request, CancellationToken ct)
    {
        if (!currentUser.TemVisaoTotal)
        {
            throw new CrmForbiddenException("Apenas administradores podem definir a meta geral de uma regional.");
        }

        if (request.MetaQuantidadeVendas < 0)
        {
            throw new CrmBusinessException("A meta de quantidade não pode ser negativa.", "meta_invalida");
        }

        if (request.MetaValor is < 0)
        {
            throw new CrmBusinessException("A meta de valor não pode ser negativa.", "meta_invalida");
        }

        var mes = NormalizarMes(request.MesReferencia);

        var meta = await db.CrmRegionalGoals.Include(g => g.Regional)
            .FirstOrDefaultAsync(g => g.RegionalId == request.RegionalId && g.MesReferencia == mes, ct);

        if (meta is null)
        {
            var regional = await db.CrmRegionais.FirstOrDefaultAsync(r => r.Id == request.RegionalId, ct)
                ?? throw new CrmNotFoundException("Regional", request.RegionalId);
            meta = new CrmRegionalGoal { RegionalId = regional.Id, Regional = regional, MesReferencia = mes };
            db.CrmRegionalGoals.Add(meta);
        }

        meta.MetaQuantidadeVendas = request.MetaQuantidadeVendas;
        meta.MetaValor = request.MetaValor;

        await db.SaveChangesAsync(ct);

        return new RegionalGoalDto(meta.Id, meta.RegionalId, meta.Regional.Nome, meta.MesReferencia, meta.MetaQuantidadeVendas, meta.MetaValor, 0m, 0);
    }

    private static DateOnly NormalizarMes(DateOnly data) => new(data.Year, data.Month, 1);
}
