using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class GoalService(ApplicationDbContext db, ICurrentUserService currentUser, IEquipeComercialService equipe) : IGoalService
{
    public async Task<IReadOnlyList<SalesGoalDto>> ListarAsync(DateOnly? mesReferencia, CancellationToken ct)
    {
        var mes = NormalizarMes(mesReferencia ?? DateOnly.FromDateTime(DateTime.UtcNow));
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);

        var metasQuery = db.CrmSalesGoals.AsNoTracking().Include(g => g.Vendedor).Where(g => g.MesReferencia == mes);
        if (visiveis is not null) metasQuery = metasQuery.Where(g => visiveis.Contains(g.VendedorId));
        var metas = await metasQuery.ToListAsync(ct);

        var inicioMes = mes.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fimMes = mes.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var vendedorIds = visiveis ?? metas.Select(m => m.VendedorId).Distinct().ToList();
        if (visiveis is null)
        {
            // Visão total sem metas ainda cadastradas: nada a listar além do que existir em CrmSalesGoals.
            vendedorIds = metas.Select(m => m.VendedorId).Distinct().ToList();
        }

        var realizadoPorVendedor = await db.CrmOpportunities.AsNoTracking()
            .Where(o => vendedorIds.Contains(o.ResponsavelId) && o.Etapa.Tipo == TipoEtapaPipeline.Ganho &&
                        o.DataEfetivaFechamento >= inicioMes && o.DataEfetivaFechamento < fimMes)
            .GroupBy(o => o.ResponsavelId)
            .Select(g => new { VendedorId = g.Key, Valor = g.Sum(o => o.ValorFinal ?? o.ValorEstimado), Quantidade = g.Count() })
            .ToListAsync(ct);

        var realizadoMap = realizadoPorVendedor.ToDictionary(r => r.VendedorId);

        return metas.Select(m =>
        {
            realizadoMap.TryGetValue(m.VendedorId, out var realizado);
            return new SalesGoalDto(m.Id, m.VendedorId, m.Vendedor.NomeCompleto, m.MesReferencia, m.MetaValor,
                m.MetaQuantidadeVendas, realizado?.Valor ?? 0m, realizado?.Quantidade ?? 0);
        }).ToList();
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

        if (request.MetaValor < 0)
        {
            throw new CrmBusinessException("A meta não pode ser negativa.", "meta_invalida");
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

        meta.MetaValor = request.MetaValor;
        meta.MetaQuantidadeVendas = request.MetaQuantidadeVendas;

        await db.SaveChangesAsync(ct);

        return new SalesGoalDto(meta.Id, meta.VendedorId, meta.Vendedor.NomeCompleto, meta.MesReferencia, meta.MetaValor, meta.MetaQuantidadeVendas, 0m, 0);
    }

    private static DateOnly NormalizarMes(DateOnly data) => new(data.Year, data.Month, 1);
}
