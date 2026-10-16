import { useEffect, useMemo, useRef, useState } from "react";
import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";
import { api, isAbortError } from "../../lib/api";
import { formatarMoeda, formatarPercentual } from "../../lib/format";
import { usePaginacao } from "../../lib/usePaginacao";
import type { LeadStage, Regional, RelatorioComercial, VendedorResumo } from "../../lib/types";
import { useTheme } from "../../context/ThemeContext";
import { useAuth } from "../../context/AuthContext";
import { useEstadoSalvo } from "../../lib/useEstadoSalvo";
import { Button, Card, ErrorState, Input, Pagination, Select, Skeleton } from "../../components/ui";
import { OPCOES_FILTRO_TIPO_INDICACAO } from "../../lib/opcoesLead";
import { MultiSelect } from "../../components/MultiSelect";
import { useCrmEventos } from "../../lib/useCrmEventos";
import { useMudancaDeDia } from "../../lib/useAoVivo";
import { baseOptions, SemDados } from "../../components/crm/Charts";
import { BarrasHorizontaisChart, DonutChart } from "../../components/marketing/GraficosTrafego";
import { DetalheDoCardModal, type AbaDoDetalhe } from "../../components/crm/DetalheDoCardModal";

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
    // Do dia 1 ao último dia do mês anterior (o dia 0 de um mês é o último do anterior).
    { rotulo: "Mês passado", inicio: dataIso(new Date(hoje.getFullYear(), hoje.getMonth() - 1, 1)), fim: dataIso(new Date(hoje.getFullYear(), hoje.getMonth(), 0)) },
    { rotulo: "Últimos 3 meses", inicio: dataIso(new Date(hoje.getFullYear(), hoje.getMonth() - 2, 1)), fim: dataIso(hoje) },
    { rotulo: "Este ano", inicio: `${hoje.getFullYear()}-01-01`, fim: dataIso(hoje) },
    { rotulo: "Desde 01/01/2025", inicio: "2025-01-01", fim: dataIso(hoje) },
  ];
}

function Kpi({ rotulo, valor, detalhe, onClick }: { rotulo: string; valor: string; detalhe?: string; onClick?: () => void }) {
  const conteudo = (
    <>
      <p className="text-xs text-[var(--fg-muted)]">{rotulo}</p>
      <p className="mt-1 text-2xl font-semibold text-[var(--fg)]">{valor}</p>
      {detalhe && <p className="mt-0.5 text-xs text-[var(--fg-muted)]">{detalhe}</p>}
    </>
  );
  if (!onClick) return <Card className="p-4">{conteudo}</Card>;
  // Clicável: abre a lista de clientes por trás do número.
  return (
    <Card className="p-0">
      <button
        type="button"
        onClick={onClick}
        title="Ver os clientes por trás deste número"
        className="focus-ring block h-full w-full cursor-pointer rounded-xl p-4 text-left transition hover:bg-[var(--surface-hover)]"
      >
        {conteudo}
      </button>
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

interface Filtros {
  inicio: string;
  fim: string;
  consultorId: string;
  regionalId: string;
  etapaIds: string[];
  chegadaInicio: string;
  chegadaFim: string;
  vendaInicio: string;
  vendaFim: string;
  indicacao: "" | "sim" | "nao";
  tiposIndicacao: string[];
}

/** Os filtros do relatório como parâmetros do endereço (o relatório e o detalhe de cada card usam os mesmos). */
function paramsDoFiltro(f: Filtros): URLSearchParams {
  const filtros = new URLSearchParams({ dataInicio: f.inicio, dataFim: f.fim });
  if (f.consultorId) filtros.set("consultorId", f.consultorId);
  if (f.regionalId) filtros.set("regionalId", f.regionalId);
  // "Sem etapa" (id nulo) vai como Guid vazio.
  f.etapaIds.forEach((id) => filtros.append("etapaId", id));
  if (f.chegadaInicio) filtros.set("chegadaInicio", f.chegadaInicio);
  if (f.chegadaFim) filtros.set("chegadaFim", f.chegadaFim);
  if (f.vendaInicio) filtros.set("vendaInicio", f.vendaInicio);
  if (f.vendaFim) filtros.set("vendaFim", f.vendaFim);
  if (f.indicacao) filtros.set("indicacao", f.indicacao === "sim" ? "true" : "false");
  f.tiposIndicacao.forEach((t) => filtros.append("tipoIndicacao", t));
  return filtros;
}

export function RelatorioComercialPage() {
  const { tema } = useTheme();
  const { temPapel } = useAuth();
  // Gestão vê todos os consultores e regionais; o consultor vê só os próprios resultados (o servidor também restringe).
  const podeGerir = temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial");
  const base = baseOptions(tema === "dark");
  const atalhos = useMemo(atalhosPeriodo, []);

  // Cada filtro fica guardado no navegador (por usuário) e só sai com "Limpar filtros". Abre em "Este mês" na primeira vez.
  const [inicio, setInicio] = useEstadoSalvo("relatorio-comercial:inicio", atalhos[0].inicio);
  const [fim, setFim] = useEstadoSalvo("relatorio-comercial:fim", dataIso(new Date()));
  const [consultorId, setConsultorId] = useEstadoSalvo("relatorio-comercial:consultor", "");
  const [regionalId, setRegionalId] = useEstadoSalvo("relatorio-comercial:regional", "");
  const [etapaIds, setEtapaIds] = useEstadoSalvo<string[]>("relatorio-comercial:etapas", []);
  // Filtros extras (como no Notion). Datas vazias usam o período principal acima.
  const [chegadaInicio, setChegadaInicio] = useEstadoSalvo("relatorio-comercial:chegada-inicio", "");
  const [chegadaFim, setChegadaFim] = useEstadoSalvo("relatorio-comercial:chegada-fim", "");
  const [vendaInicio, setVendaInicio] = useEstadoSalvo("relatorio-comercial:venda-inicio", "");
  const [vendaFim, setVendaFim] = useEstadoSalvo("relatorio-comercial:venda-fim", "");
  const [indicacao, setIndicacao] = useEstadoSalvo<"" | "sim" | "nao">("relatorio-comercial:indicacao", "");
  const [tiposIndicacao, setTiposIndicacao] = useEstadoSalvo<string[]>("relatorio-comercial:tipos-indicacao", []);

  const filtrosDaTela = (): Filtros => ({
    inicio, fim,
    // Consultor/regional só valem para a gestão (um valor que sobrou guardado de antes não pode filtrar a tela de quem não gere).
    consultorId: podeGerir ? consultorId : "",
    regionalId: podeGerir ? regionalId : "",
    etapaIds, chegadaInicio, chegadaFim, vendaInicio, vendaFim, indicacao, tiposIndicacao,
  });
  // O que está de fato aplicado no relatório. Mudar um filtro na tela NÃO atualiza o relatório: só o botão "Pesquisar" (ou Enter).
  // Ao abrir, já vem aplicado o que ficou guardado.
  const [aplicado, setAplicado] = useState<Filtros>(filtrosDaTela);
  const [avisoFiltro, setAvisoFiltro] = useState<string | null>(null);
  const pendente = JSON.stringify(filtrosDaTela()) !== JSON.stringify(aplicado);

  const pesquisar = (extra?: Partial<Filtros>) => {
    const novos = { ...filtrosDaTela(), ...extra };
    if (!novos.inicio || !novos.fim || novos.fim < novos.inicio) {
      setAvisoFiltro("A data inicial não pode ser maior que a final.");
      return;
    }
    setAvisoFiltro(null);
    setAplicado(novos);
  };

  const limparFiltros = () => {
    const padrao: Filtros = {
      inicio: atalhos[0].inicio, fim: dataIso(new Date()), consultorId: "", regionalId: "", etapaIds: [],
      chegadaInicio: "", chegadaFim: "", vendaInicio: "", vendaFim: "", indicacao: "", tiposIndicacao: [],
    };
    setInicio(padrao.inicio); setFim(padrao.fim); setConsultorId(""); setRegionalId(""); setEtapaIds([]);
    setChegadaInicio(""); setChegadaFim(""); setVendaInicio(""); setVendaFim(""); setIndicacao(""); setTiposIndicacao([]);
    setAvisoFiltro(null);
    setAplicado(padrao);
  };

  const temFiltro = !!(
    consultorId || regionalId || etapaIds.length || chegadaInicio || chegadaFim || vendaInicio || vendaFim || indicacao || tiposIndicacao.length
    || inicio !== atalhos[0].inicio || fim !== dataIso(new Date())
  );

  const [dados, setDados] = useState<RelatorioComercial | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const filtroCarregadoRef = useRef<Filtros | null>(null);
  const [atualizadoEm, setAtualizadoEm] = useState<Date | null>(null);
  // Card aberto: a lista de clientes por trás do número.
  const [detalhe, setDetalhe] = useState<{ titulo: string; abas: AbaDoDetalhe[] } | null>(null);
  const [consultores, setConsultores] = useState<VendedorResumo[]>([]);
  // Abas por regional: "Todas" + uma por regional (mesmo com uma só cadastrada).
  const [regionais, setRegionais] = useState<Regional[]>([]);
  const [etapas, setEtapas] = useState<LeadStage[]>([]);

  useEffect(() => {
    if (podeGerir) {
      api.get<VendedorResumo[]>("/crm/management/vendedores?incluirInativos=true").then(setConsultores).catch(() => setConsultores([]));
      api.get<Regional[]>("/crm/settings/regionals").then(setRegionais).catch(() => setRegionais([]));
    }
    api.get<LeadStage[]>("/crm/settings/lead-stages").then(setEtapas).catch(() => setEtapas([]));
  }, [podeGerir]);

  useEffect(() => {
    const f = aplicado;
    if (!f.inicio || !f.fim || f.fim < f.inicio) {
      // Filtro guardado inválido (ex.: datas trocadas): não fica carregando para sempre.
      setCarregando(false);
      return;
    }
    const controller = new AbortController();
    // Tempo real: o recarregamento por evento (mesmos filtros) é silencioso — os números trocam sem piscar. Só mudar de filtro mostra o carregamento.
    const mudouFiltro = filtroCarregadoRef.current !== aplicado;
    filtroCarregadoRef.current = aplicado;
    if (mudouFiltro) setCarregando(true);
    setErro(null);
    api
      .get<RelatorioComercial>(`/crm/relatorio-comercial?${paramsDoFiltro(f).toString()}`, controller.signal)
      .then((d) => {
        setDados(d);
        setAtualizadoEm(new Date());
      })
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o relatório."); })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, [aplicado, recarregar]);

  // Tempo real (vendas, leads, sincronização); na virada do dia, o período que terminava "hoje" passa a
  // terminar no dia novo — quem escolheu um período passado continua nele.
  // (rede de segurança de 30 s: se a conexão em tempo real cair, a tela se atualiza do mesmo jeito)
  useCrmEventos(() => setRecarregar((n) => n + 1), 500, 30_000);
  useMudancaDeDia((hoje, anterior) => {
    // "Este mês" aberto na virada do mês: o período passa para o mês novo (início e fim juntos).
    const virouMes = (i: string, f: string) => f === anterior && i.slice(0, 7) === anterior.slice(0, 7) && i.endsWith("-01") && hoje.slice(0, 7) !== anterior.slice(0, 7);
    if (virouMes(inicio, fim)) setInicio(`${hoje.slice(0, 7)}-01`);
    setFim((atual) => (atual === anterior ? hoje : atual));
    setAplicado((a) => ({ ...a, inicio: virouMes(a.inicio, a.fim) ? `${hoje.slice(0, 7)}-01` : a.inicio, fim: a.fim === anterior ? hoje : a.fim }));
    setRecarregar((n) => n + 1);
  });

  const paginaVendedores = usePaginacao(dados?.porVendedor, 10);
  const paginaOrigens = usePaginacao(dados?.porOrigem, 8);
  const paginaProdutos = usePaginacao(dados?.porProduto, 8);

  const filtro = (
    <form
      onSubmit={(e) => { e.preventDefault(); pesquisar(); }}
      className="flex w-full flex-wrap items-end gap-2"
    >
      <div className="flex flex-wrap gap-1.5">
        {atalhos.map((a) => (
          <button
            key={a.rotulo}
            type="button"
            onClick={() => { setInicio(a.inicio); setFim(a.fim); pesquisar({ inicio: a.inicio, fim: a.fim }); }}
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
      {podeGerir && (
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
      )}
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
      <div className="flex items-center gap-2">
        <Button type="submit" size="sm">Pesquisar</Button>
        <Button type="button" size="sm" variant="ghost" onClick={limparFiltros} disabled={!temFiltro && !pendente}>Limpar filtros</Button>
      </div>
      {pendente && !avisoFiltro && <p className="w-full text-xs text-[var(--fg-muted)]">Você mudou filtros: clique em <strong>Pesquisar</strong> para aplicá-los.</p>}
      {avisoFiltro && <p role="alert" className="w-full text-xs text-[var(--danger)]">{avisoFiltro}</p>}
    </form>
  );

  const cabecalho = (
    <div className="flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Relatório comercial</h1>
        <p className="text-sm text-[var(--fg-muted)]">
          Vendas, leads, origem e retorno — os relatórios do Notion, calculados com os dados do CRM. Vendas pela data de ativação; leads pela data de chegada.
          {!podeGerir && " Aqui você vê só os seus resultados."}
        </p>
      </div>
      <p className="flex items-center gap-1.5 self-start text-xs text-[var(--fg-muted)]" title="Os números e as listas se atualizam sozinhos quando algo é cadastrado ou muda no sistema">
        <span className="relative flex size-2">
          <span className="absolute inline-flex size-full animate-ping rounded-full bg-[var(--success)] opacity-60" />
          <span className="relative inline-flex size-2 rounded-full bg-[var(--success)]" />
        </span>
        Ao vivo{atualizadoEm ? ` · atualizado às ${atualizadoEm.toLocaleTimeString("pt-BR")}` : ""}
      </p>
      {filtro}
      {podeGerir && (
      <div role="tablist" aria-label="Regional" className="flex w-full flex-wrap gap-1.5 border-b border-[var(--border)] pb-2">
        {(dados?.regionalFixaId ? regionais.filter((r) => r.id === dados.regionalFixaId) : [{ id: "", nome: "Todas as regionais" }, ...regionais.filter((r) => r.ativa)]).map((r) => (
          <button
            key={r.id || "todas"}
            type="button"
            role="tab"
            aria-selected={!!dados?.regionalFixaId || regionalId === r.id}
            onClick={() => { setRegionalId(r.id); setConsultorId(""); pesquisar({ regionalId: r.id, consultorId: "" }); }}
            className={`focus-ring cursor-pointer rounded-lg px-3 py-1.5 text-sm font-medium ${
              dados?.regionalFixaId || regionalId === r.id ? "bg-[var(--brand)] text-white" : "text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
            }`}
          >
            {r.nome}
          </button>
        ))}
      </div>
      )}
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
  const abrirDetalhe = (titulo: string, abas: [string, string][]) =>
    setDetalhe({
      titulo,
      abas: abas.map(([rotulo, tipo]) => {
        const params = paramsDoFiltro(aplicado);
        params.set("tipo", tipo);
        return { rotulo, url: `/crm/relatorio-comercial/detalhe?${params.toString()}` };
      }),
    });
  const meses = dados.porMes.map((m) => rotuloMes(m.mes));

  const vendasEAdesao: ApexOptions = {
    ...base,
    chart: { ...base.chart, type: "line" },
    colors: ["#467fc5", "#22c55e"],
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
    colors: ["#467fc5", "#7fb0e6", "#f59e0b", "#a6c3e0"],
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
    colors: ["#467fc5", "#ef4444", "#22c55e"],
    stroke: { width: [0, 3, 3], curve: "smooth" },
    plotOptions: { bar: { borderRadius: 2, columnWidth: "65%" } },
    dataLabels: { enabled: false },
    legend: { position: "top", horizontalAlign: "left" },
    xaxis: { categories: dados.porSemana.map((s) => rotuloDia(s.inicio)), tickAmount: Math.min(dados.porSemana.length, 16), labels: { rotate: -45 } },
    yaxis: { labels: { formatter: (v) => String(Math.round(v)) } },
    tooltip: { ...base.tooltip, shared: true, intersect: false },
  };

  const totalVendasFipe = dados.faixasFipe.reduce((soma, f) => soma + f.vendas, 0);
  const estados = dados.porEstado.filter((e) => e.vendas + e.leads > 0).slice(0, 15);

  return (
    <div className="space-y-4">
      {cabecalho}

      <div className={`grid gap-4 sm:grid-cols-2 lg:grid-cols-3 ${carregando ? "opacity-60" : ""}`}>
        <Kpi
          rotulo="Leads pegos"
          valor={inteiro(t.leads)}
          detalhe={`${inteiro(t.leadsPerdidos)} perdidos · só tráfego pago (CRM e Notion)`}
          onClick={() => abrirDetalhe("Leads pegos", [["Pegos", "leads"], ["Perdidos", "perdidos"]])}
        />
        <Kpi
          rotulo="Vendas concluídas"
          valor={inteiro(t.vendas)}
          detalhe={`conversão de ${formatarPercentual(t.taxaConversao)} sobre os leads · ${inteiro(t.vendasTrafegoPago ?? 0)} tráfego pago · ${inteiro(t.vendasIndicacaoCard ?? 0)} indicação${
            (t.vendasSemOrigemIdentificada ?? 0) > 0 ? ` · ${inteiro(t.vendasSemOrigemIdentificada ?? 0)} sem origem de anúncio identificada` : ""
          }`}
          onClick={() =>
            abrirDetalhe("Vendas concluídas", [["Todas", "vendas"], ["Tráfego pago", "trafego-vendas"], ["Indicação", "indicacao-vendas"], ["Sem origem identificada", "sem-origem-vendas"]])
          }
        />
        <Kpi rotulo="Adesão" valor={formatarMoeda(t.adesao)} detalhe={t.vendas > 0 ? `média de ${formatarMoeda(t.adesao / t.vendas)} por venda` : undefined} onClick={() => abrirDetalhe("Adesão", [["Vendas com adesão", "adesao"]])} />
        <Kpi rotulo="Mensalidade" valor={formatarMoeda(t.mensalidade)} detalhe={`ticket médio de ${formatarMoeda(t.ticketMensalidade)}`} onClick={() => abrirDetalhe("Mensalidade", [["Vendas com mensalidade", "mensalidade"]])} />
        <Kpi rotulo="Rastreador" valor={formatarMoeda(t.rastreador)} onClick={() => abrirDetalhe("Rastreador", [["Vendas com rastreador", "rastreador"]])} />
        <Kpi rotulo="Vistoria" valor={formatarMoeda(t.vistoria)} onClick={() => abrirDetalhe("Vistoria", [["Vendas com vistoria", "vistoria"]])} />
        <Kpi
          rotulo="Indicação"
          valor={formatarMoeda(t.indicacao)}
          detalhe={`${inteiro(t.vendasIndicacao)} venda(s) por indicação`}
          onClick={() => abrirDetalhe("Indicação", [["Vendas com indicação", "indicacao"]])}
        />
        <Kpi
          rotulo="Conversão — tráfego pago"
          valor={formatarPercentual(t.taxaConversaoTrafegoPago ?? 0)}
          detalhe={`${inteiro(t.vendasTrafegoPago ?? 0)} venda(s) ÷ ${inteiro(t.leadsTrafegoPago ?? 0)} lead(s) de tráfego pago (CRM e Notion)`}
          onClick={() => abrirDetalhe("Conversão — tráfego pago", [["Vendas", "trafego-vendas"], ["Leads", "trafego-leads"]])}
        />
        <Kpi
          rotulo="Conversão — indicação"
          valor={formatarPercentual(t.taxaConversaoIndicacaoCard ?? 0)}
          detalhe={`${inteiro(t.vendasIndicacaoCard ?? 0)} venda(s) ÷ ${inteiro(t.leadsIndicacaoCard ?? 0)} card(s) de indicação`}
          onClick={() => abrirDetalhe("Conversão — indicação", [["Vendas", "indicacao-vendas"], ["Cards", "indicacao-leads"]])}
        />
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

      {/* Origem dos leads e Produto: só para a gestão. O consultor vê os próprios números nos totais, gráficos de período, etapa, estado e FIPE. */}
      {podeGerir && (
      <div className="grid gap-4 lg:grid-cols-2">
        <Secao titulo="Origem dos leads" descricao="Tag de campanha do lead (Lookalike, UGC, Pmax…) ou a origem de cadastro.">
          <DonutChart itens={dados.porOrigem.slice(0, 8).map((o) => ({ nome: o.origem, valor: o.leads }))} altura={260} />
          <Tabela
            colunas={["Origem", "Leads", "Vendas", "Adesão"]}
            linhas={paginaOrigens.itensDaPagina.map((o) => [o.origem, inteiro(o.leads), inteiro(o.vendas), formatarMoeda(o.adesao)])}
          />
          <Pagination pagina={paginaOrigens.pagina} totalPaginas={paginaOrigens.totalPaginas} onChange={paginaOrigens.setPagina} />
        </Secao>
        <Secao titulo="Produto (O que?)" descricao="Produto de interesse informado no lead.">
          <DonutChart itens={dados.porProduto.slice(0, 8).map((p) => ({ nome: p.produto, valor: p.leads }))} altura={260} />
          <Tabela
            colunas={["Produto", "Leads", "Vendas", "Adesão"]}
            linhas={paginaProdutos.itensDaPagina.map((p) => [p.produto, inteiro(p.leads), inteiro(p.vendas), formatarMoeda(p.adesao)])}
          />
          <Pagination pagina={paginaProdutos.pagina} totalPaginas={paginaProdutos.totalPaginas} onChange={paginaProdutos.setPagina} />
        </Secao>
      </div>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <Secao titulo="Vendas por estado" descricao="Estado informado na venda (ou no lead).">
          <BarrasHorizontaisChart
            categorias={estados.map((e) => e.estado)}
            series={[{ nome: "Vendas", valores: estados.map((e) => e.vendas) }, { nome: "Leads", valores: estados.map((e) => e.leads) }]}
          />
        </Secao>
        <Secao titulo="Vendas por faixa de FIPE" descricao="Valor FIPE do veículo das vendas do período.">
          <BarrasHorizontaisChart categorias={dados.faixasFipe.map((f) => f.faixa)} series={[{ nome: "Vendas", valores: dados.faixasFipe.map((f) => f.vendas) }]} />
          <Tabela
            colunas={["Faixa", "Vendas", "% das vendas"]}
            linhas={dados.faixasFipe.map((f) => [f.faixa, inteiro(f.vendas), formatarPercentual(totalVendasFipe > 0 ? (100 * f.vendas) / totalVendasFipe : 0)])}
          />
        </Secao>
      </div>

      {podeGerir && (
      <Secao titulo="Desempenho por vendedor" descricao="Leads que o vendedor recebeu e vendas que fechou no período.">
        <Tabela
          colunas={["Vendedor", "Leads", "Vendas", "Conversão", "Adesão", "Mensalidade"]}
          linhas={paginaVendedores.itensDaPagina.map((v) => [
            v.vendedor, inteiro(v.leads), inteiro(v.vendas), formatarPercentual(v.taxaConversao), formatarMoeda(v.adesao), formatarMoeda(v.mensalidade),
          ])}
        />
        <Pagination pagina={paginaVendedores.pagina} totalPaginas={paginaVendedores.totalPaginas} onChange={paginaVendedores.setPagina} />
      </Secao>
      )}

      {podeGerir && dados.marketingNotion.length > 0 && (
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

      <DetalheDoCardModal aberto={!!detalhe} titulo={detalhe?.titulo ?? ""} abas={detalhe?.abas ?? []} onFechar={() => setDetalhe(null)} />
    </div>
  );
}
