using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public record CriarLeadResultado(LeadDetailDto? Lead, LeadDuplicateWarningDto? Duplicidade);

public interface ILeadService
{
    Task<PagedResult<LeadListItemDto>> ListarAsync(LeadFilterRequest filtro, CancellationToken ct);
    Task<LeadDetailDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<LeadTimelineItemDto>> ObterTimelineAsync(Guid id, CancellationToken ct);
    Task<CriarLeadResultado> CriarAsync(LeadCreateRequest request, CancellationToken ct);
    Task<LeadDetailDto> AtualizarAsync(Guid id, LeadUpdateRequest request, CancellationToken ct);
    Task AtribuirAsync(Guid id, LeadAssignRequest request, CancellationToken ct);
    Task<LeadDetailDto> MudarEtapaAsync(Guid id, ChangeLeadStageRequest request, CancellationToken ct);

    /// <summary>Anexa a proposta do concorrente a um lead perdido por "Fechado na Concorrência" (PDF/imagem, até 10 MB).</summary>
    Task<LeadDetailDto> AnexarPropostaConcorrenteAsync(Guid id, IFormFile arquivo, CancellationToken ct);
    Task<IReadOnlyList<Guid>> CriarVeiculosAdicionaisAsync(Guid id, LeadVeiculosAdicionaisRequest request, CancellationToken ct);
    Task<int> AtribuirEmLoteAsync(LeadBulkAssignRequest request, CancellationToken ct);
    Task<LeadImportResultDto> ImportarAsync(Stream planilha, CancellationToken ct);
    Task<byte[]> ExportarAsync(LeadFilterRequest filtro, CancellationToken ct);

    /// <summary>Contagem e somas dos leads que batem com os filtros (rodapé da lista).</summary>
    Task<LeadTotaisDto> ObterTotaisAsync(LeadFilterRequest filtro, CancellationToken ct);
    Task<Guid> AdicionarNotaAsync(Guid leadId, string texto, CancellationToken ct);

    /// <summary>Anota que o consultor iniciou um contato (hoje: pelo botão de WhatsApp): atualiza o "último contato" do lead e deixa uma nota na linha do tempo.</summary>
    Task RegistrarContatoAsync(Guid leadId, string canal, CancellationToken ct);
    Task ExcluirAsync(Guid id, CancellationToken ct);

    /// <summary>Lixeira do quadro: leads excluídos por alguém no CRM (podem voltar para o quadro).</summary>
    Task<IReadOnlyList<LeadLixeiraDto>> ListarLixeiraAsync(CancellationToken ct);

    Task RestaurarAsync(Guid id, CancellationToken ct);
}
