using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface ILeadKanbanService
{
    Task<LeadKanbanBoardDto> ObterBoardAsync(LeadKanbanFilterRequest filtro, CancellationToken ct);

    /// <summary>Próxima página de cartões de uma coluna ("Ver mais").</summary>
    Task<IReadOnlyList<LeadKanbanCardDto>> ObterCartoesAsync(LeadKanbanColunaRequest request, CancellationToken ct);
}
