import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";
import { useTheme } from "../../context/ThemeContext";
import { formatarMoeda } from "../../lib/format";
import type { EvolucaoVendas } from "../../lib/types";

/**
 * Compara a evolução real de vendas (últimos 6 meses) com a meta do mês atual — usada como
 * referência constante nos meses anteriores por simplicidade, já que o histórico de metas por
 * mês não é mantido separadamente hoje.
 */
export function DesempenhoChart({ dados, metaAtual }: { dados: EvolucaoVendas[]; metaAtual: number }) {
  const { tema } = useTheme();
  const modoEscuro = tema === "dark";

  const options: ApexOptions = {
    chart: { toolbar: { show: false }, foreColor: modoEscuro ? "#9aa4b2" : "#5b6472", fontFamily: "inherit", background: "transparent" },
    grid: { borderColor: modoEscuro ? "#2a2f3a" : "#e2e5e9" },
    theme: { mode: modoEscuro ? "dark" : "light" },
    colors: ["#2f6fed", "#94a3b8"],
    stroke: { curve: "smooth", width: [3, 2], dashArray: [0, 6] },
    dataLabels: { enabled: false },
    markers: { size: 4 },
    xaxis: { categories: dados.map((d) => d.periodo) },
    yaxis: { labels: { formatter: (v) => formatarMoeda(v) } },
    legend: { position: "top", horizontalAlign: "left" },
    tooltip: { theme: modoEscuro ? "dark" : "light", y: { formatter: (v) => formatarMoeda(v) } },
  };

  const series = [
    { name: "Vendas (R$)", data: dados.map((d) => d.valorGanho) },
    { name: "Meta (R$)", data: dados.map(() => metaAtual) },
  ];

  return <Chart type="line" height={280} options={options} series={series} />;
}
