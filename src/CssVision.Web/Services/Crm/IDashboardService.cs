using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IDashboardService
{
    Task<DashboardDto> ObterAsync(DashboardFilterRequest filtro, CancellationToken ct);

    /// <summary>As vendas do período por trás de uma fatia do gráfico "Origem das vendas" (Tráfego pago, Indicação ou Sem origem identificada), com os mesmos filtros do painel.</summary>
    Task<RelatorioDetalheDto> ObterVendasDaOrigemAsync(DashboardFilterRequest filtro, string origem, CancellationToken ct);
}
