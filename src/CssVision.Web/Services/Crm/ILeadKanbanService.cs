using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface ILeadKanbanService
{
    Task<LeadKanbanBoardDto> ObterBoardAsync(LeadKanbanFilterRequest filtro, CancellationToken ct);

    /// <summary>Próxima página de cartões de uma coluna ("Ver mais").</summary>
    Task<IReadOnlyList<LeadKanbanCardDto>> ObterCartoesAsync(LeadKanbanColunaRequest request, CancellationToken ct);

    /// <summary>"Por que este cliente não aparece no quadro?": para cada cliente achado pela busca, a etapa, a carteira e o veredito.</summary>
    Task<IReadOnlyList<LeadNoQuadroDto>> DiagnosticarAsync(string busca, CancellationToken ct);

    /// <summary>Procura, na carteira que o usuário enxerga, os grupos de clientes que o quadro pode esconder.</summary>
    Task<IReadOnlyList<AuditoriaQuadroGrupoDto>> AuditarAsync(CancellationToken ct);
}
