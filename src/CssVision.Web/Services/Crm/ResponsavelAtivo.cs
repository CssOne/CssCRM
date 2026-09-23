using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Consultor inativo (<see cref="Domain.Identity.ApplicationUser.Ativo"/> = false) não recebe lead
/// nem oportunidade. A distribuição automática (LeadAssignmentService) e as listas de consultores das
/// telas já filtram por Ativo; esta checagem cobre quem escolhe o responsável explicitamente
/// (cadastro, atribuição, redistribuição em lote, oportunidade) — inclusive chamadas diretas à API.
/// </summary>
public static class ResponsavelAtivo
{
    public static async Task GarantirAsync(ApplicationDbContext db, Guid usuarioId, CancellationToken ct)
    {
        var ativo = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => (bool?)u.Ativo)
            .FirstOrDefaultAsync(ct);

        if (ativo is null)
        {
            throw new CrmBusinessException("Consultor não encontrado.", "responsavel_inexistente");
        }

        if (ativo == false)
        {
            throw new CrmBusinessException("Este consultor está inativo e não pode receber leads.", "responsavel_inativo");
        }
    }
}
