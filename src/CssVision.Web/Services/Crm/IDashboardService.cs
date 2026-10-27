using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface IDashboardService
{
    Task<DashboardDto> ObterAsync(DashboardFilterRequest filtro, CancellationToken ct);

    /// <summary>As vendas do período por trás de uma fatia do gráfico "Origem das vendas" (Tráfego pago, Indicação ou Sem origem identificada), com os mesmos filtros do painel.</summary>
    Task<RelatorioDetalheDto> ObterVendasDaOrigemAsync(DashboardFilterRequest filtro, string origem, CancellationToken ct);

    /// <summary>As vendas por trás da barra de meta do mês (as mesmas contadas nela), todas ou só de um tipo (Lead, Indicação, Indicação Lead, Pessoal...).</summary>
    Task<RelatorioDetalheDto> ObterVendasDoMesAsync(DashboardFilterRequest filtro, string? tipo, CancellationToken ct);
}
