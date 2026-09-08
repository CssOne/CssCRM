using CssVision.Web.Services.Crm;

namespace CssVision.Web.Tests.Infrastructure;

public sealed class NoOpAuditSink : IAuditSink
{
    public Task RegistrarAsync(string acao, string entidadeTipo, Guid? entidadeId, object? detalhes, CancellationToken ct) =>
        Task.CompletedTask;
}
