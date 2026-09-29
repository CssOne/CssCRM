import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";
import { useTheme } from "../../context/ThemeContext";
import type { MarketingEvolucao, MarketingHorario, MarketingSerie } from "../../lib/types";
import { baseOptions, paleta, SemDados } from "../crm/Charts";

/** Cores fixas para o "O que?" — a mesma tag tem sempre a mesma cor em todos os gráficos. */
export const CORES_O_QUE: Record<string, string> = {
  AGV: "#2563eb",
  "AGV ELÉTRICO": "#22c55e",
  "AGV TRUCK": "#f97316",
  "Não informado": "#94a3b8",
};

export function corOQue(nome: string, indice: number) {
  return CORES_O_QUE[nome] ?? paleta[(indice + 2) % paleta.length];
}

function useBase() {
  const { tema } = useTheme();
  return baseOptions(tema === "dark");
}

/** Leads por dia empilhados por "O que?", com a linha de vendas. */
export function LeadsPorDiaChart({ evolucao, series }: { evolucao: MarketingEvolucao[]; series: MarketingSerie[] }) {
  const base = useBase();
  if (evolucao.every((d) => d.quantidade === 0)) return <SemDados />;

  const barras = series.length > 0 ? series : [{ nome: "Leads", valores: evolucao.map((d) => d.quantidade) }];
  const options: ApexOptions = {
    ...base,
    chart: { ...base.chart, type: "line", stacked: true },
    colors: [...barras.map((s, i) => corOQue(s.nome, i)), "#e11d48"],
    plotOptions: { bar: { borderRadius: 2, columnWidth: "65%" } },
    stroke: { width: [...barras.map(() => 0), 3], curve: "smooth" },
    markers: { size: [...barras.map(() => 0), 3] },
    dataLabels: { enabled: false },
    legend: { position: "top", horizontalAlign: "left" },
    xaxis: { categories: evolucao.map((d) => d.data), tickAmount: Math.min(evolucao.length, 15), labels: { rotate: -45 } },
    yaxis: { labels: { formatter: (v) => String(Math.round(v)) } },
    tooltip: { ...base.tooltip, shared: true, intersect: false },
  };
  const serie = [
    ...barras.map((s) => ({ name: s.nome, type: "bar", data: s.valores })),
    { name: "Vendas", type: "line", data: evolucao.map((d) => d.ganhos ?? 0) },
  ];
  return <Chart type="line" height={300} options={options} series={serie} />;
}

export function DonutChart({ itens, cores, altura = 280 }: { itens: { nome: string; valor: number }[]; cores?: string[]; altura?: number }) {
  const base = useBase();
  if (itens.length === 0 || itens.every((i) => i.valor === 0)) return <SemDados />;
  const options: ApexOptions = {
    ...base,
    chart: { ...base.chart, type: "donut" },
    colors: cores ?? paleta,
    labels: itens.map((i) => i.nome),
    legend: { position: "bottom" },
    dataLabels: { enabled: true, formatter: (v: number) => `${v.toFixed(0)}%` },
    plotOptions: { pie: { donut: { labels: { show: true, total: { show: true, label: "Total" } } } } },
  };
  return <Chart type="donut" height={altura} options={options} series={itens.map((i) => i.valor)} />;
}

/**
 * Barras horizontais com uma ou duas séries (ex.: leads x vendas por consultor). `cores` por barra
 * pinta cada categoria de uma cor (funil).
 */
export function BarrasHorizontaisChart({
  categorias,
  series,
  coresPorBarra,
  sufixo = "",
}: {
  categorias: string[];
  series: { nome: string; valores: number[] }[];
  coresPorBarra?: string[];
  sufixo?: string;
}) {
  const base = useBase();
  if (categorias.length === 0) return <SemDados />;
  const options: ApexOptions = {
    ...base,
    chart: { ...base.chart, type: "bar" },
    ...(coresPorBarra ? { colors: coresPorBarra } : {}),
    plotOptions: { bar: { horizontal: true, borderRadius: 3, barHeight: series.length > 1 ? "75%" : "60%", distributed: !!coresPorBarra } },
    dataLabels: { enabled: true, style: { fontSize: "11px" } },
    legend: { show: series.length > 1 && !coresPorBarra, position: "top", horizontalAlign: "left" },
    xaxis: { categories: categorias, labels: { formatter: (v) => String(Math.round(Number(v))) } },
    tooltip: { ...base.tooltip, y: { formatter: (v) => `${v}${sufixo}` } },
  };
  return (
    <Chart
      type="bar"
      height={Math.max(200, categorias.length * (series.length > 1 ? 38 : 30) + 60)}
      options={options}
      series={series.map((s) => ({ name: s.nome, data: s.valores }))}
    />
  );
}

const DIAS = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

/** Mapa de calor: em que dia da semana e horário os leads chegam (horário de Brasília). */
export function HorarioChart({ dados }: { dados: MarketingHorario[] }) {
  const base = useBase();
  if (dados.length === 0) return <SemDados />;
  const porChave = new Map(dados.map((d) => [`${d.diaSemana}-${d.hora}`, d.quantidade]));
  // Apex desenha a primeira série embaixo: segunda-feira no topo, domingo por último.
  const ordem = [0, 6, 5, 4, 3, 2, 1];
  const series = ordem.map((dia) => ({
    name: DIAS[dia],
    data: Array.from({ length: 24 }, (_, hora) => ({ x: `${hora}h`, y: porChave.get(`${dia}-${hora}`) ?? 0 })),
  }));
  const options: ApexOptions = {
    ...base,
    chart: { ...base.chart, type: "heatmap" },
    colors: ["#2563eb"],
    dataLabels: { enabled: false },
    plotOptions: { heatmap: { radius: 3, shadeIntensity: 0.6, enableShades: true } },
    tooltip: { ...base.tooltip, y: { formatter: (v) => `${v} lead(s)` } },
    xaxis: { labels: { rotate: 0, hideOverlappingLabels: true } },
  };
  return <Chart type="heatmap" height={280} options={options} series={series} />;
}
