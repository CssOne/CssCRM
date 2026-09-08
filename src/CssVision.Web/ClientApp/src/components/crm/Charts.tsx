import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";
import { useTheme } from "../../context/ThemeContext";
import { formatarMoeda } from "../../lib/format";
import type { EvolucaoVendas, FunilEtapa, OrigemLead } from "../../lib/types";

const paleta = ["#2563eb", "#0ea5e9", "#8b5cf6", "#22c55e", "#f59e0b", "#f97316", "#ef4444", "#64748b"];

function baseOptions(modoEscuro: boolean): ApexOptions {
  return {
    chart: { toolbar: { show: false }, foreColor: modoEscuro ? "#9aa4b2" : "#5b6472", fontFamily: "inherit", background: "transparent" },
    grid: { borderColor: modoEscuro ? "#2a2f3a" : "#e2e5e9" },
    theme: { mode: modoEscuro ? "dark" : "light" },
    tooltip: { theme: modoEscuro ? "dark" : "light" },
    colors: paleta,
  };
}

export function FunilChart({ dados }: { dados: FunilEtapa[] }) {
  const { tema } = useTheme();
  const modoEscuro = tema === "dark";

  const options: ApexOptions = {
    ...baseOptions(modoEscuro),
    chart: { ...baseOptions(modoEscuro).chart, type: "bar" },
    plotOptions: { bar: { horizontal: true, borderRadius: 4, distributed: true } },
    dataLabels: { enabled: false },
    legend: { show: false },
    xaxis: { categories: dados.map((d) => d.etapa) },
    yaxis: { labels: { formatter: (v) => String(Math.round(v)) } },
    tooltip: { ...baseOptions(modoEscuro).tooltip, y: { formatter: (v) => `${v} oportunidade(s)` } },
  };

  if (dados.length === 0) return <SemDados />;

  return <Chart type="bar" height={Math.max(220, dados.length * 42)} options={options} series={[{ name: "Oportunidades", data: dados.map((d) => d.quantidade) }]} />;
}

export function EvolucaoChart({ dados }: { dados: EvolucaoVendas[] }) {
  const { tema } = useTheme();
  const modoEscuro = tema === "dark";

  const options: ApexOptions = {
    ...baseOptions(modoEscuro),
    chart: { ...baseOptions(modoEscuro).chart, type: "area" },
    dataLabels: { enabled: false },
    stroke: { curve: "smooth", width: 2 },
    fill: { type: "gradient", gradient: { opacityFrom: 0.35, opacityTo: 0 } },
    xaxis: { categories: dados.map((d) => d.periodo) },
    yaxis: { labels: { formatter: (v) => formatarMoeda(v) } },
    tooltip: { ...baseOptions(modoEscuro).tooltip, y: { formatter: (v) => formatarMoeda(v) } },
  };

  return <Chart type="area" height={260} options={options} series={[{ name: "Vendas ganhas", data: dados.map((d) => d.valorGanho) }]} />;
}

export function OrigemChart({ dados }: { dados: OrigemLead[] }) {
  const { tema } = useTheme();
  const modoEscuro = tema === "dark";

  if (dados.length === 0) return <SemDados />;

  const options: ApexOptions = {
    ...baseOptions(modoEscuro),
    chart: { ...baseOptions(modoEscuro).chart, type: "donut" },
    labels: dados.map((d) => d.origem),
    legend: { position: "bottom" },
    dataLabels: { enabled: true, formatter: (v: number) => `${v.toFixed(0)}%` },
  };

  return <Chart type="donut" height={280} options={options} series={dados.map((d) => d.quantidade)} />;
}

function SemDados() {
  return <p className="flex h-40 items-center justify-center text-sm text-[var(--fg-muted)]">Sem dados no período.</p>;
}
