import { useEffect, useMemo, useState } from "react";
import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";
import { api, isAbortError } from "../../lib/api";
import { formatarMoeda, formatarPercentual } from "../../lib/format";
import { usePaginacao } from "../../lib/usePaginacao";
import type { LeadStage, RelatorioComercial, VendedorResumo } from "../../lib/types";
import { useTheme } from "../../context/ThemeContext";
import { Button, Card, ErrorState, Input, Pagination, Select, Skeleton } from "../../components/ui";
import { OPCOES_FILTRO_TIPO_INDICACAO } from "../../lib/opcoesLead";
import { MultiSelect } from "../../components/MultiSelect";
import { useCrmEventos } from "../../lib/useCrmEventos";
import { useMudancaDeDia } from "../../lib/useAoVivo";
import { baseOptions, SemDados } from "../../components/crm/Charts";
import { BarrasHorizontaisChart, DonutChart } from "../../components/marketing/GraficosTrafego";

const EMPTY_GUID = "00000000-0000-0000-0000-000000000000";
const MESES = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

/** "2025-03" → "mar/25". */
function rotuloMes(mes: string) {
  const [ano, m] = mes.split("-");
  return `${MESES[Number(m) - 1]}/${ano.slice(2)}`;
}

/** "2025-03-17" → "17/03". */
function rotuloDia(data: string) {
  const [, m, d] = data.split("-");
  return `${d}/${m}`;
}

const inteiro = (n: number) => n.toLocaleString("pt-BR");

function dataIso(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function atalhosPeriodo() {
  const hoje = new Date();
  const primeiroDoMes = new Date(hoje.getFullYear(), hoje.getMonth(), 1);
  return [
    { rotulo: "Este mês", inicio: dataIso(primeiroDoMes), fim: dataIso(hoje) },
    { rotulo: "Últimos 3 meses", inicio: dataIso(new Date(hoje.getFullYear(), hoje.getMonth() - 2, 1)), fim: dataIso(hoje) },
    { rotulo: "Este ano", inicio: `${hoje.getFullYear()}-01-01`, fim: dataIso(hoje) },
    { rotulo: "Desde 01/01/2025", inicio: "2025-01-01", fim: dataIso(hoje) },
  ];
}

function Kpi({ rotulo, valor, detalhe }: { rotulo: string; valor: string; detalhe?: string }) {
  return (
    <Card className="p-4">
      <p className="text-xs text-[var(--fg-muted)]">{rotulo}</p>
      <p className="mt-1 text-2xl font-semibold text-[var(--fg)]">{valor}</p>
      {detalhe && <p className="mt-0.5 text-xs text-[var(--fg-muted)]">{detalhe}</p>}
    </Card>
  );
}

function Secao({ titulo, descricao, className = "", children }: { titulo: string; descricao?: string; className?: string; children: React.ReactNode }) {
  return (
    <Card className={`p-4 ${className}`}>
      <h2 className="text-sm font-semibold text-[var(--fg)]">{titulo}</h2>
      {descricao && <p className="mb-2 text-xs text-[var(--fg-muted)]">{descricao}</p>}
      <div className={descricao ? "" : "mt-2"}>{children}</div>
    </Card>
  );
}

function Tabela({ colunas, linhas }: { colunas: string[]; linhas: (string | number)[][] }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[420px] text-sm">
        <thead>
          <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
            {colunas.map((c, i) => (
              <th key={c} className={`pb-2 font-medium ${i > 0 ? "text-right" : ""}`}>{c}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {linhas.map((l, i) => (
            <tr key={i} className="border-b border-[var(--border)] last:border-0">
              {l.map((c, j) => (
                <td key={j} className={`py-2 ${j === 0 ? "text-[var(--fg)]" : "text-right text-[var(--fg-muted)]"}`}>{c}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function RelatorioComercialPage() {
  const { tema } = useTheme();
  const base = baseOptions(tema === "dark");
  const atalhos = useMemo(atalhosPeriodo, []);
  const [inicio, setInicio] = useState("2025-01-01");
  const [fim, setFim] = useState(() => dataIso(new Date()));
  const [dados, setDados] = useState<RelatorioComercial | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [consultorId, setConsultorId] = useState("");
  const [etapaIds, setEtapaIds] = useState<string[]>([]);
  // Filtros extras (como no Notion). Datas vazias usam o período principal acima.
  const [chegadaInicio, setChegadaInicio] = useState("");
  const [chegadaFim, setChegadaFim] = useState("");
  const [vendaInicio, setVendaInicio] = useState("");
  const [vendaFim, setVendaFim] = useState("");
  const [indicacao, setIndicacao] = useState<"" | "sim" | "nao">("");
  const [tiposIndicacao, setTiposIndicacao] = useState<string[]>([]);
  const temFiltroExtra = !!(chegadaInicio || chegadaFim || vendaInicio || vendaFim || indicacao || tiposIndicacao.length);
  const limparExtras = () => {
    setChegadaInicio(""); setChegadaFim(""); setVendaInicio(""); setVendaFim(""); setIndicacao(""); setTiposIndicacao([]);
  };
  const [consultores, setConsultores] = useState<VendedorResumo[]>([]);
  const [etapas, setEtapas] = useState<LeadStage[]>([]);

  useEffect(() => {
    api.get<VendedorResumo[]>("/crm/management/vendedores?incluirInativos=true").then(setConsultores).catch(() => setConsultores([]));
    api.get<LeadStage[]>("/crm/settings/lead-stages").then(setEtapas).catch(() => setEtapas([]));
  }, []);

  useEffect(() => {
    if (!inicio || !fim || fim < inicio) return;
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    const filtros = new URLSearchParams({ dataInicio: inicio, dataFim: fim });
    if (consultorId) filtros.set("consultorId", consultorId);
    // "Sem etapa" (id nulo) vai como Guid vazio.
    etapaIds.forEach((id) => filtros.append("etapaId", id));
    if (chegadaInicio) filtros.set("chegadaInicio", chegadaInicio);
    if (chegadaFim) filtros.set("chegadaFim", chegadaFim);
    if (vendaInicio) filtros.set("vendaInicio", vendaInicio);
    if (vendaFim) filtros.set("vendaFim", vendaFim);
    if (indicacao) filtros.set("indicacao", indicacao === "sim" ? "true" : "false");
    tiposIndicacao.forEach((t) => filtros.append("tipoIndicacao", t));
    api
      .get<RelatorioComercial>(`/crm/relatorio-comercial?${filtros.toString()}`, controller.signal)
      .then(setDados)
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o relatório."); })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, [inicio, fim, consultorId, etapaIds, chegadaInicio, chegadaFim, vendaInicio, vendaFim, indicacao, tiposIndicacao, recarregar]);

  // Tempo real (vendas, leads, sincronização); na virada do dia, o período que terminava "hoje" passa a
  // terminar no dia novo — quem escolheu um período passado continua nele.
  useCrmEventos(() => setRecarregar((n) => n + 1), 500);
  useMudancaDeDia((hoje, anterior) => {
    setFim((atual) => (atual === anterior ? hoje : atual));
    setRecarregar((n) => n + 1);
  });

  const paginaVendedores = usePaginacao(dados?.porVendedor, 10);
  const paginaOrigens = usePaginacao(dados?.porOrigem, 8);
  const paginaProdutos = usePaginacao(dados?.porProduto, 8);

  const filtro = (
    <div className="flex w-full flex-wrap items-end gap-2">
      <div className="flex flex-wrap gap-1.5">
        {atalhos.map((a) => (
          <button
            key={a.rotulo}
            type="button"
            onClick={() => { setInicio(a.inicio); setFim(a.fim); }}
            className={`focus-ring cursor-pointer rounded-full border px-3 py-1 text-xs font-medium ${
              inicio === a.inicio && fim === a.fim ? "border-[var(--brand)] bg-[var(--brand)] text-white" : "border-[var(--border)] text-[var(--fg-muted)] hover:text-[var(--fg)]"
            }`}
          >
            {a.rotulo}
          </button>
        ))}
      </div>
      <label className="text-xs text-[var(--fg-muted)]">
        De
        <Input type="date" className="mt-0.5 h-8 w-36" value={inicio} max={fim} onChange={(e) => setInicio(e.target.value)} />
      </label>
      <label className="text-xs text-[var(--fg-muted)]">
        Até
        <Input type="date" className="mt-0.5 h-8 w-36" value={fim} min={inicio} onChange={(e) => setFim(e.target.value)} />
      </label>
      <label className="text-xs text-[var(--fg-muted)]">
        Consultor
        <Select className="mt-0.5 h-8 w-52" value={consultorId} onChange={(e) => setConsultorId(e.target.value)} aria-label="Consultor">
          <option value="">Todos</option>
          {consultores.map((c) => (
            <option key={c.id} value={c.id}>
              {c.nome}
              {c.ativo === false ? " (inativo)" : ""}
            </option>
          ))}
        </Select>
      </label>
      <div className="w-52 text-xs text-[var(--fg-muted)]">
        Etapas
        <div className="mt-0.5">
          <MultiSelect
            ariaLabel="Etapas do quadro de leads"
            rotuloTodos="Todas"
            opcoes={[
              ...etapas.filter((e) => e.ativa !== false).map((e) => ({ valor: e.id ?? EMPTY_GUID, rotulo: e.nome })),
              ...(etapas.some((e) => e.id === null) ? [] : [{ valor: EMPTY_GUID, rotulo: "Sem etapa" }]),
            ]}
            valores={etapaIds}
            onChange={setEtapaIds}
          />
        </div>
      </div>
      <div className="w-52 text-xs text-[var(--fg-muted)]">
        Tipo de indicação
        <div className="mt-0.5">
          <MultiSelect
            ariaLabel="Tipo de indicação"
            rotuloTodos="Todos"
            opcoes={OPCOES_FILTRO_TIPO_INDICACAO.map((t) => ({ valor: t, rotulo: t }))}
            valores={tiposIndicacao}
            onChange={setTiposIndicacao}
          />
        </div>
      </div>
      <label className="text-xs text-[var(--fg-muted)]">
        Indicação?
        <Select className="mt-0.5 h-8 w-28" value={indicacao} onChange={(e) => setIndicacao(e.target.value as typeof indicacao)} aria-label="Indicação?">
          <option value="">Todos</option>
          <option value="sim">Sim</option>
          <option value="nao">Não</option>
        </Select>
      </label>
      <div className="flex items-end gap-1" title="Vazio = usa o período acima. Escolhendo, as vendas passam a ser só dos leads que chegaram nessas datas.">
        <label className="text-xs text-[var(--fg-muted)]">
          Chegada de
          <Input type="date" className="mt-0.5 h-8 w-36" value={chegadaInicio} max={chegadaFim || undefined} onChange={(e) => setChegadaInicio(e.target.value)} />
        </label>
        <label className="text-xs text-[var(--fg-muted)]">
          até
          <Input type="date" className="mt-0.5 h-8 w-36" value={chegadaFim} min={chegadaInicio || undefined} onChange={(e) => setChegadaFim(e.target.value)} />
        </label>
      </div>
      <div className="flex items-end gap-1" title="Vazio = usa o período acima.">
        <label className="text-xs text-[var(--fg-muted)]">
          Ativação de
          <Input type="date" className="mt-0.5 h-8 w-36" value={vendaInicio} max={vendaFim || undefined} onChange={(e) => setVendaInicio(e.target.value)} />
        </label>
        <label className="text-xs text-[var(--fg-muted)]">
          até
          <Input type="date" className="mt-0.5 h-8 w-36" value={vendaFim} min={vendaInicio || undefined} onChange={(e) => setVendaFim(e.target.value)} />
        </label>
      </div>
      {temFiltroExtra && (
        <Button size="sm" variant="ghost" onClick={limparExtras}>Redefinir filtros</Button>
      )}
    </div>
  );

  const cabecalho = (
    <div className="flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Relatório comercial</h1>
        <p className="text-sm text-[var(--fg-muted)]">
          Vendas, leads, origem e retorno — os relatórios do Notion, calculados com os dados do CRM. Vendas pela data de ativação; leads pela data de chegada.
        </p>
      </div>
      {filtro}
    </div>
  );

  if (erro) {
    return (
      <div className="space-y-4">
        {cabecalho}
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      </div>
    );
  }

  if (!dados) {
    return (
      <div className="space-y-4">
        {cabecalho}
        <div className="grid gap-4 sm:grid-cols-4">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24" />)}</div>
        <Skeleton className="h-72" />
      </div>
    );
  }

  const t = dados.totais;
  const meses = dados.porMes.map((m) => rotuloMes(m.mes));

  const vendasEAdesao: ApexOptions = {
    ...base,
    chart: { ...base.chart, type: "line" },
    colors: ["#2563eb", "#22c55e"],
    stroke: { width: [0, 3], curve: "smooth" },
    plotOptions: { bar: { borderRadius: 3, columnWidth: "55%" } },
    dataLabels: { enabled: false },
    legend: { position: "top", horizontalAlign: "left" },
    xaxis: { categories: meses },
    yaxis: [
      { title: { text: "Adesão (R$)" }, labels: { formatter: (v) => formatarMoeda(v) } },
      { opposite: true, title: { text: "Vendas" }, labels: { formatter: (v) => String(Math.round(v)) } },
    ],
    tooltip: { ...base.tooltip, shared: true, intersect: false },
  };

  const valoresPorMes: ApexOptions = {
    ...base,
    chart: { ...base.chart, type: "bar", stacked: true },
    colors: ["#2563eb", "#0ea5e9", "#f59e0b", "#8b5cf6"],
    plotOptions: { bar: { borderRadius: 2, columnWidth: "55%" } },
    dataLabels: { enabled: false },
    legend: { position: "top", horizontalAlign: "left" },
    xaxis: { categories: meses },
    yaxis: { labels: { formatter: (v) => formatarMoeda(v) } },
    tooltip: { ...base.tooltip, y: { formatter: (v) => formatarMoeda(v) } },
  };

  const leadsPorSemana: ApexOptions = {
    ...base,
    chart: { ...base.chart, type: "line" },
    colors: ["#2563eb", "#ef4444", "#22c55e"],
    stroke: { width: [0, 3, 3], curve: "smooth" },
    plotOptions: { bar: { borderRadius: 2, columnWidth: "65%" } },
    dataLabels: { enabled: false },
    legend: { position: "top", horizontalAlign: "left" },
    xaxis: { categories: dados.porSemana.map((s) => rotuloDia(s.inicio)), tickAmount: Math.min(dados.porSemana.length, 16), labels: { rotate: -45 } },
    yaxis: { labels: { formatter: (v) => String(Math.round(v)) } },
    tooltip: { ...base.tooltip, shared: true, intersect: false },
  };

  const estados = dados.porEstado.filter((e) => e.vendas + e.leads > 0).slice(0, 15);

  return (
    <div className="space-y-4">
      {cabecalho}

      <div className={`grid gap-4 sm:grid-cols-2 lg:grid-cols-4 ${carregando ? "opacity-60" : ""}`}>
        <Kpi rotulo="Leads recebidos" valor={inteiro(t.leads)} detalhe={`${inteiro(t.leadsPerdidos)} perdidos`} />
        <Kpi rotulo="Vendas concluídas" valor={inteiro(t.vendas)} detalhe={`conversão de ${formatarPercentual(t.taxaConversao)} sobre os leads`} />
        <Kpi rotulo="Adesão" valor={formatarMoeda(t.adesao)} detalhe={t.vendas > 0 ? `média de ${formatarMoeda(t.adesao / t.vendas)} por venda` : undefined} />
        <Kpi rotulo="Mensalidade" valor={formatarMoeda(t.mensalidade)} detalhe={`ticket médio de ${formatarMoeda(t.ticketMensalidade)}`} />
        <Kpi rotulo="Rastreador" valor={formatarMoeda(t.rastreador)} />
        <Kpi rotulo="Vistoria" valor={formatarMoeda(t.vistoria)} />
        <Kpi rotulo="Indicação" valor={formatarMoeda(t.indicacao)} detalhe={`${inteiro(t.vendasIndicacao)} venda(s) por indicação`} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Secao titulo="Vendas e adesão por mês" descricao="Barras: adesão recebida. Linha: quantidade de vendas.">
          {dados.porMes.length === 0 ? <SemDados /> : (
            <Chart
              type="line"
              height={300}
              options={vendasEAdesao}
              series={[
                { name: "Adesão", type: "bar", data: dados.porMes.map((m) => m.adesao) },
                { name: "Vendas", type: "line", data: dados.porMes.map((m) => m.vendas) },
              ]}
            />
          )}
        </Secao>
        <Secao titulo="Mensalidade, rastreador, vistoria e indicação por mês">
          {dados.porMes.length === 0 ? <SemDados /> : (
            <Chart
              type="bar"
              height={300}
              options={valoresPorMes}
              series={[
                { name: "Mensalidade", data: dados.porMes.map((m) => m.mensalidade) },
                { name: "Rastreador", data: dados.porMes.map((m) => m.rastreador) },
                { name: "Vistoria", data: dados.porMes.map((m) => m.vistoria) },
                { name: "Indicação", data: dados.porMes.map((m) => m.indicacao) },
              ]}
            />
          )}
        </Secao>
      </div>

      <Secao titulo="Leads por semana" descricao="Leads recebidos (barras), perdidos e vendas (linhas) em cada semana, a partir da segunda-feira.">
        {dados.porSemana.length === 0 ? <SemDados /> : (
          <Chart
            type="line"
            height={300}
            options={leadsPorSemana}
            series={[
              { name: "Leads", type: "bar", data: dados.porSemana.map((s) => s.leads) },
              { name: "Perdidos", type: "line", data: dados.porSemana.map((s) => s.leadsPerdidos) },
              { name: "Vendas", type: "line", data: dados.porSemana.map((s) => s.vendas) },
            ]}
          />
        )}
      </Secao>

      <Secao titulo="Leads por etapa" descricao="Etapa atual, no quadro de leads, dos leads que chegaram no período.">
        <BarrasHorizontaisChart categorias={dados.porEtapa.map((e) => e.etapa)} series={[{ nome: "Leads", valores: dados.porEtapa.map((e) => e.leads) }]} />
      </Secao>

      <div className="grid gap-4 lg:grid-cols-2">
        <Secao titulo="Origem dos leads" descricao="Tag de campanha do lead (Lookalike, UGC, Pmax…) ou a origem de cadastro.">
          <DonutChart itens={dados.porOrigem.slice(0, 8).map((o) => ({ nome: o.origem, valor: o.leads }))} altura={260} />
          <Tabela
            colunas={["Origem", "Leads", "Vendas", "Adesão"]}
            linhas={paginaOrigens.itensDaPagina.map((o) => [o.origem, inteiro(o.leads), inteiro(o.vendas), formatarMoeda(o.adesao)])}
          />
          <Pagination pagina={paginaOrigens.pagina} totalPaginas={paginaOrigens.totalPaginas} onChange={paginaOrigens.setPagina} />
        </Secao>
        <Secao titulo="Produto (O que?)">
          <Tabela
            colunas={["Produto", "Leads", "Vendas", "Adesão"]}
            linhas={paginaProdutos.itensDaPagina.map((p) => [p.produto, inteiro(p.leads), inteiro(p.vendas), formatarMoeda(p.adesao)])}
          />
          <Pagination pagina={paginaProdutos.pagina} totalPaginas={paginaProdutos.totalPaginas} onChange={paginaProdutos.setPagina} />
        </Secao>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Secao titulo="Vendas por estado" descricao="Estado informado na venda (ou no lead).">
          <BarrasHorizontaisChart
            categorias={estados.map((e) => e.estado)}
            series={[{ nome: "Vendas", valores: estados.map((e) => e.vendas) }, { nome: "Leads", valores: estados.map((e) => e.leads) }]}
          />
        </Secao>
        <Secao titulo="Vendas por faixa de FIPE" descricao="Valor FIPE do veículo das vendas do período.">
          <BarrasHorizontaisChart categorias={dados.faixasFipe.map((f) => f.faixa)} series={[{ nome: "Vendas", valores: dados.faixasFipe.map((f) => f.vendas) }]} />
        </Secao>
      </div>

      <Secao titulo="Desempenho por vendedor" descricao="Leads que o vendedor recebeu e vendas que fechou no período.">
        <Tabela
          colunas={["Vendedor", "Leads", "Vendas", "Conversão", "Adesão", "Mensalidade"]}
          linhas={paginaVendedores.itensDaPagina.map((v) => [
            v.vendedor, inteiro(v.leads), inteiro(v.vendas), formatarPercentual(v.taxaConversao), formatarMoeda(v.adesao), formatarMoeda(v.mensalidade),
          ])}
        />
        <Pagination pagina={paginaVendedores.pagina} totalPaginas={paginaVendedores.totalPaginas} onChange={paginaVendedores.setPagina} />
      </Secao>

      {dados.marketingNotion.length > 0 && (
        <Secao
          titulo="Investimento e retorno (controle de marketing do Notion)"
          descricao="Gasto em mídia, leads e faturamento lançados no Notion — o CRM não tem o valor investido em anúncios. Custo por lead = gastos ÷ leads; ROAS = faturamento ÷ gastos."
        >
          <Tabela
            colunas={["Mês", "Leads", "Vendas", "Conversão", "Gastos", "Custo/lead", "Faturamento", "Meta", "ROAS"]}
            linhas={dados.marketingNotion.map((m) => [
              rotuloMes(m.mes), inteiro(m.leadsGerados), inteiro(m.vendas), m.taxaConversao == null ? "—" : formatarPercentual(m.taxaConversao),
              formatarMoeda(m.totalGastos), m.custoPorLead == null ? "—" : formatarMoeda(m.custoPorLead), formatarMoeda(m.faturamento),
              formatarMoeda(m.metaFaturamento), m.roas == null ? "—" : `${m.roas.toLocaleString("pt-BR")}x`,
            ])}
          />
        </Secao>
      )}
    </div>
  );
}
