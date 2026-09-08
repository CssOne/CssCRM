using CssVision.Web.Services.Crm;

namespace CssVision.Web.Tests.Infrastructure;

/// <summary>Nunca atribui automaticamente — usado nos testes que não exercitam a distribuição automática.</summary>
public sealed class NoOpLeadAssignmentService : ILeadAssignmentService
{
    public Task<Guid?> ProximoResponsavelAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
}
