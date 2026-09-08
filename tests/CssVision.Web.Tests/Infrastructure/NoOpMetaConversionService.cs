using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Marketing;

namespace CssVision.Web.Tests.Infrastructure;

/// <summary>Nunca envia de verdade — usado nos testes que não exercitam o retorno de conversão offline (CAPI).</summary>
public sealed class NoOpMetaConversionService : IMetaConversionService
{
    public Task<bool> EnviarConversaoVendaAsync(CrmLead lead, CrmOpportunity opportunity, CancellationToken ct) => Task.FromResult(false);

    public Task<bool> EnviarEventoEtapaAsync(CrmLead lead, Guid etapaId, string etapaNome, CancellationToken ct) => Task.FromResult(false);
}
