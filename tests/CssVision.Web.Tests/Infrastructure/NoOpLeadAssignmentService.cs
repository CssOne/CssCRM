using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.Crm;

namespace CssVision.Web.Tests.Infrastructure;

/// <summary>Nunca atribui automaticamente — usado nos testes que não exercitam a distribuição automática.</summary>
public sealed class NoOpLeadAssignmentService : ILeadAssignmentService
{
    public Task<Guid?> ProximoResponsavelAsync(string? oQue, CancellationToken ct) => Task.FromResult<Guid?>(null);

    public Task<bool> PodeReceberAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult(true);

    public Task<int> DistribuirPendentesAsync(CancellationToken ct) => Task.FromResult(0);

    public Task<NovosLeadsDto> NovosLeadsAsync(Guid usuarioId, DateTimeOffset? desde, CancellationToken ct) =>
        Task.FromResult(new NovosLeadsDto(DateTimeOffset.UtcNow, []));
}
