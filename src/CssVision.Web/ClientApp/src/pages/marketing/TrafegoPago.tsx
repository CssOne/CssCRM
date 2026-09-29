import {
  CalendarClock,
  Clock,
  Filter,
  Handshake,
  Megaphone,
  PhoneMissed,
  Save,
  Target,
  Timer,
  Trash2,
  TrendingDown,
  TrendingUp,
  UserPlus,
  Users,
  Wallet,
  X,
  XCircle,
} from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarDataHora, formatarMoeda, formatarPercentual, formatarTelefone } from "../../lib/format";
import type { MarketingDashboard, MarketingLeadItem, PagedResult } from "../../lib/types";
import { Badge, Button, Card, ErrorState, Input, Pagination, Select, Skeleton } from "../../components/ui";
import { StatCard } from "../../components/crm/StatCard";
import { MultiSelect } from "../../components/MultiSelect";
import { useCrmEventos } from "../../lib/useCrmEventos";
import { BarrasHorizontaisChart, corOQue, DonutChart, HorarioChart, LeadsPorDiaChart } from "../../components/marketing/GraficosTrafego";

// ---------- Filtros (ficam salvos no navegador; dá pra guardar combinações com nome) ----------

type Periodo = "hoje" | "7d" | "30d" | "mes" | "mesPassado" | "90d" | "personalizado";

interface Filtros {
  periodo: Periodo;
  dataInicio: string;
  dataFim: string;
  fonte: "trafego" | "novo" | "todos";
  oQue: string[];
  origem: string[];
  campanha: string[];
  canal: string[];
  responsavelId: string[];
  estado: string[];
  etapa: string[];
}

const FILTROS_PADRAO: Filtros = {
  periodo: "30d",
  dataInicio: "",
  dataFim: "",
  fonte: "trafego",
  oQue: [],
  origem: [],
  campanha: [],
  canal: [],
  responsavelId: [],
  estado: [],
  etapa: [],
};

const CHAVE_FILTROS = "css:trafego:filtros";
const CHAVE_SALVOS = "css:trafego:filtros-salvos";
const TAGS_O_QUE = ["AGV", "AGV ELÉTRICO", "AGV TRUCK"];

const ROTULO_PERIODO: Record<Periodo, string> = {
  hoje: "Hoje",
  "7d": "7 dias",
  "30d": "30 dias",
  mes: "Este mês",
  mesPassado: "Mês passado",
  "90d": "90 dias",
  personalizado: "Personalizado",
};

function lerJson<T>(chave: string, padrao: T): T {
  try {
    const bruto = localStorage.getItem(chave);
    return bruto ? (JSON.parse(bruto) as T) : padrao;
  } catch {
    return padrao;
  }
}

function gravarJson(chave: string, valor: unknown) {
  try {
    localStorage.setItem(chave, JSON.stringify(valor));
  } catch {
    // navegador sem armazenamento: os filtros só não ficam salvos
  }
}

function normalizar(bruto: Partial<Filtros> | null | undefined): Filtros {
  const f = { ...FILTROS_PADRAO, ...(bruto ?? {}) };
  for (const campo of ["oQue", "origem", "campanha", "canal", "responsavelId", "estado", "etapa"] as const) {
    if (!Array.isArray(f[campo])) f[campo] = [];
  }
  return f;
}

function iso(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function intervalo(f: Filtros): { dataInicio?: string; dataFim?: string } {
  const hoje = new Date();
  const dias = (n: number) => iso(new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() - n));
  switch (f.periodo) {
    case "hoje":
      return { dataInicio: iso(hoje), dataFim: iso(hoje) };
    case "7d":
      return { dataInicio: dias(6), dataFim: iso(hoje) };
    case "30d":
      return { dataInicio: dias(29), dataFim: iso(hoje) };
    case "90d":
      return { dataInicio: dias(89), dataFim: iso(hoje) };
    case "mes":
      return { dataInicio: iso(new Date(hoje.getFullYear(), hoje.getMonth(), 1)), dataFim: iso(hoje) };
    case "mesPassado":
      return {
        dataInicio: iso(new Date(hoje.getFullYear(), hoje.getMonth() - 1, 1)),
        dataFim: iso(new Date(hoje.getFullYear(), hoje.getMonth(), 0)),
      };
    default:
      return { dataInicio: f.dataInicio || undefined, dataFim: f.dataFim || undefined };
  }
}

function variacao(atual: number, anterior: number): string | undefined {
  if (anterior === 0) return atual > 0 ? "0 no período anterior" : undefined;
  const pct = ((atual - anterior) / anterior) * 100;
  return `${pct >= 0 ? "▲" : "▼"} ${Math.abs(pct).toFixed(0)}% vs. anterior (${anterior})`;
}

function horas(h: number | null | undefined): string {
  if (h == null) return "—";
  if (h < 1) return `${Math.round(h * 60)} min`;
  if (h < 48) return `${h.toFixed(1).replace(".", ",")} h`;
  return `${(h / 24).toFixed(1).replace(".", ",")} dias`;
}

const LEADS_POR_PAGINA = 20;
const CONSULTORES_POR_PAGINA = 10;
const CONSULTORES_POR_PAGINA_GRAFICO = 8;
const MESES = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

/** "2026-09" → "set/26". */
function rotuloMes(mes: string) {
  const [ano, m] = mes.split("-");
  return `${MESES[Number(m) - 1] ?? m}/${ano.slice(2)}`;
}

function Secao({ titulo, icone, acao, children, className = "" }: { titulo: string; icone?: ReactNode; acao?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <Card className={`p-4 ${className}`}>
      <div className="mb-3 flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 text-sm font-semibold text-[var(--fg)]">
          {icone}
          {titulo}
        </h2>
        {acao}
      </div>
      {children}
    </Card>
  );
}

function Campo({ rotulo, children, largura = "w-48" }: { rotulo: string; children: ReactNode; largura?: string }) {
  return (
    <div className={largura}>
      <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">{rotulo}</label>
      {children}
    </div>
  );
}

export function TrafegoPagoPage() {
  const [dados, setDados] = useState<MarketingDashboard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [filtros, setFiltros] = useState<Filtros>(() => normalizar(lerJson<Partial<Filtros> | null>(CHAVE_FILTROS, null)));
  const [salvos, setSalvos] = useState<{ nome: string; filtros: Filtros }[]>(() => lerJson(CHAVE_SALVOS, []));
  const [nomeNovoFiltro, setNomeNovoFiltro] = useState("");
  const [salvando, setSalvando] = useState(false);
  const [paginaConsultores, setPaginaConsultores] = useState(1);
  const [paginaGrafico, setPaginaGrafico] = useState(1);
  const [metricaMensal, setMetricaMensal] = useState<"leads" | "ganhos">("leads");
  const [paginaLeads, setPaginaLeads] = useState(1);
  const [listaLeads, setListaLeads] = useState<PagedResult<MarketingLeadItem> | null>(null);
  const [carregandoLeads, setCarregandoLeads] = useState(false);

  useEffect(() => gravarJson(CHAVE_FILTROS, filtros), [filtros]);

  const consulta = useMemo(() => {
    const { periodo: _periodo, dataInicio: _i, dataFim: _f, ...resto } = filtros;
    return toQueryString({ ...resto, ...intervalo(filtros) });
  }, [filtros]);

  useEffect(() => {
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    api
      .get<MarketingDashboard>(`/marketing/dashboard${consulta}`, controller.signal)
      .then(setDados)
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o painel.");
      })
      .finally(() => {
        if (!controller.signal.aborted) setCarregando(false);
      });
    return () => controller.abort();
  }, [consulta, recarregar]);

  // Filtro novo volta todas as listas para a primeira página.
  useEffect(() => {
    setPaginaLeads(1);
    setPaginaConsultores(1);
    setPaginaGrafico(1);
  }, [consulta]);

  // "Últimos leads" paginado no servidor (o período pode ter milhares de leads).
  useEffect(() => {
    const controller = new AbortController();
    setCarregandoLeads(true);
    const separador = consulta ? "&" : "?";
    api
      .get<PagedResult<MarketingLeadItem>>(
        `/marketing/leads${consulta}${separador}pagina=${paginaLeads}&tamanhoPagina=${LEADS_POR_PAGINA}`,
        controller.signal
      )
      .then(setListaLeads)
      .catch((e) => {
        if (!isAbortError(e)) setListaLeads(null);
      })
      .finally(() => {
        if (!controller.signal.aborted) setCarregandoLeads(false);
      });
    return () => controller.abort();
  }, [consulta, paginaLeads, recarregar]);

  // Lead novo chegando ou mudando de etapa atualiza o painel sozinho.
  useCrmEventos(() => setRecarregar((n) => n + 1), 1500);

  function set<K extends keyof Filtros>(campo: K, valor: Filtros[K]) {
    setFiltros((f) => ({ ...f, [campo]: valor }));
  }

  function alternarOQue(tag: string) {
    setFiltros((f) => ({ ...f, oQue: f.oQue.includes(tag) ? f.oQue.filter((t) => t !== tag) : [...f.oQue, tag] }));
  }

  function salvarFiltro() {
    const nome = nomeNovoFiltro.trim();
    if (!nome) return;
    const lista = [...salvos.filter((s) => s.nome !== nome), { nome, filtros }].sort((a, b) => a.nome.localeCompare(b.nome));
    setSalvos(lista);
    gravarJson(CHAVE_SALVOS, lista);
    setNomeNovoFiltro("");
    setSalvando(false);
  }

  function excluirFiltro(nome: string) {
    const lista = salvos.filter((s) => s.nome !== nome);
    setSalvos(lista);
    gravarJson(CHAVE_SALVOS, lista);
  }

  const qtdFiltros =
    filtros.oQue.length + filtros.origem.length + filtros.campanha.length + filtros.canal.length +
    filtros.responsavelId.length + filtros.estado.length + filtros.etapa.length + (filtros.fonte !== "trafego" ? 1 : 0);

  if (carregando && !dados) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-28" />
        <div className="grid grid-cols-2 gap-4 lg:grid-cols-5">
          {Array.from({ length: 10 }).map((_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      </div>
    );
  }

  if (erro || !dados) {
    return <ErrorState message={erro ?? "Não foi possível carregar o painel."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  const { indicadores: ind, opcoes } = dados;
  const oQueTags = Array.from(new Set([...TAGS_O_QUE, ...opcoes.oQue.filter((o) => o !== "Não informado")]));
  const periodoTexto = `${dados.periodoInicio.split("-").reverse().join("/")} a ${dados.periodoFim.split("-").reverse().join("/")}`;
  const estados = dados.porEstado.slice(0, 12);

  // Tabela de consultores, paginada.
  const totalPaginasConsultores = Math.max(1, Math.ceil(dados.porConsultor.length / CONSULTORES_POR_PAGINA));
  const consultoresDaPagina = dados.porConsultor.slice(
    (paginaConsultores - 1) * CONSULTORES_POR_PAGINA,
    paginaConsultores * CONSULTORES_POR_PAGINA
  );

  // Gráfico mês a mês: uma série por mês do período, consultores (mais leads primeiro) paginados.
  const meses = Array.from(new Set(dados.porConsultorMensal.map((c) => c.mes))).sort();
  const totalPaginasGrafico = Math.max(1, Math.ceil(dados.porConsultor.length / CONSULTORES_POR_PAGINA_GRAFICO));
  const consultoresGrafico = dados.porConsultor.slice(
    (paginaGrafico - 1) * CONSULTORES_POR_PAGINA_GRAFICO,
    paginaGrafico * CONSULTORES_POR_PAGINA_GRAFICO
  );
  const valorMensal = (id: string | null | undefined, nome: string, mes: string) => {
    const linha = dados.porConsultorMensal.find((c) => (c.id ?? null) === (id ?? null) && c.nome === nome && c.mes === mes);
    return linha ? linha[metricaMensal] : 0;
  };

  return (
    <div className={`space-y-5 transition-opacity ${carregando ? "opacity-60" : ""}`}>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Tráfego pago</h1>
          <p className="text-sm text-[var(--fg-muted)]">
            Gestão dos leads de anúncios · {periodoTexto}
            {qtdFiltros > 0 && ` · ${qtdFiltros} filtro(s) ativo(s)`}
          </p>
        </div>
        <div className="flex flex-wrap gap-1 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-1">
          {(Object.keys(ROTULO_PERIODO) as Periodo[]).map((p) => (
            <button
              key={p}
              type="button"
              onClick={() => set("periodo", p)}
              className={`cursor-pointer rounded-md px-2.5 py-1 text-xs font-medium transition ${
                filtros.periodo === p ? "bg-[var(--brand)] text-white" : "text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
              }`}
            >
              {ROTULO_PERIODO[p]}
            </button>
          ))}
        </div>
      </div>

      {/* Filtros */}
      <Card className="space-y-3 p-3">
        <div className="flex flex-wrap items-center gap-2">
          <span className="flex items-center gap-1 text-xs font-medium text-[var(--fg-muted)]">
            <Filter className="size-3.5" /> O que?
          </span>
          {oQueTags.map((tag, i) => {
            const ativo = filtros.oQue.includes(tag);
            const cor = corOQue(tag, i);
            return (
              <button
                key={tag}
                type="button"
                onClick={() => alternarOQue(tag)}
                style={ativo ? { backgroundColor: cor, borderColor: cor } : { borderColor: cor, color: cor }}
                className={`cursor-pointer rounded-full border px-3 py-1 text-xs font-semibold transition ${ativo ? "text-white shadow-sm" : "bg-transparent hover:opacity-80"}`}
              >
                {tag}
              </button>
            );
          })}
          {filtros.oQue.length > 0 && (
            <button type="button" onClick={() => set("oQue", [])} className="cursor-pointer text-xs text-[var(--fg-muted)] hover:text-[var(--fg)]">
              Todos
            </button>
          )}
        </div>

        <div className="flex flex-wrap items-end gap-3">
          {filtros.periodo === "personalizado" && (
            <>
              <Campo rotulo="Chegada de" largura="w-36">
                <Input type="date" value={filtros.dataInicio} onChange={(e) => set("dataInicio", e.target.value)} />
              </Campo>
              <Campo rotulo="até" largura="w-36">
                <Input type="date" value={filtros.dataFim} onChange={(e) => set("dataFim", e.target.value)} />
              </Campo>
            </>
          )}
          <Campo rotulo="Leads considerados" largura="w-56">
            <Select value={filtros.fonte} onChange={(e) => set("fonte", e.target.value as Filtros["fonte"])}>
              <option value="trafego">Tráfego pago (sistema + Notion)</option>
              <option value="novo">Só tráfego do sistema novo</option>
              <option value="todos">Todos os leads</option>
            </Select>
          </Campo>
          <Campo rotulo="Canal">
            <MultiSelect ariaLabel="Canal" opcoes={opcoes.canais.map((c) => ({ valor: c, rotulo: c }))} valores={filtros.canal} onChange={(v) => set("canal", v)} />
          </Campo>
          <Campo rotulo="Origem">
            <MultiSelect ariaLabel="Origem" rotuloTodos="Todas" opcoes={opcoes.origens.map((o) => ({ valor: o, rotulo: o }))} valores={filtros.origem} onChange={(v) => set("origem", v)} />
          </Campo>
          <Campo rotulo="Campanha" largura="w-56">
            <MultiSelect ariaLabel="Campanha" rotuloTodos="Todas" opcoes={opcoes.campanhas.map((c) => ({ valor: c, rotulo: c }))} valores={filtros.campanha} onChange={(v) => set("campanha", v)} />
          </Campo>
          <Campo rotulo="Consultor">
            <MultiSelect
              ariaLabel="Consultor"
              opcoes={opcoes.consultores.map((c) => ({ valor: c.id, rotulo: c.nome }))}
              valores={filtros.responsavelId}
              onChange={(v) => set("responsavelId", v)}
            />
          </Campo>
          <Campo rotulo="Estado" largura="w-36">
            <MultiSelect ariaLabel="Estado" opcoes={opcoes.estados.map((e) => ({ valor: e, rotulo: e }))} valores={filtros.estado} onChange={(v) => set("estado", v)} />
          </Campo>
          <Campo rotulo="Etapa">
            <MultiSelect ariaLabel="Etapa" rotuloTodos="Todas" opcoes={opcoes.etapas.map((e) => ({ valor: e, rotulo: e }))} valores={filtros.etapa} onChange={(v) => set("etapa", v)} />
          </Campo>
          {qtdFiltros > 0 && (
            <Button variant="ghost" size="sm" onClick={() => setFiltros((f) => ({ ...FILTROS_PADRAO, periodo: f.periodo, dataInicio: f.dataInicio, dataFim: f.dataFim }))}>
              <X className="size-4" /> Limpar filtros
            </Button>
          )}
        </div>

        {/* Filtros salvos */}
        <div className="flex flex-wrap items-center gap-2 border-t border-[var(--border)] pt-3">
          <span className="text-xs font-medium text-[var(--fg-muted)]">Filtros salvos:</span>
          {salvos.length === 0 && <span className="text-xs text-[var(--fg-muted)]">nenhum ainda</span>}
          {salvos.map((s) => (
            <span key={s.nome} className="inline-flex items-center overflow-hidden rounded-full border border-[var(--border)] text-xs">
              <button type="button" onClick={() => setFiltros(normalizar(s.filtros))} className="cursor-pointer px-2.5 py-1 font-medium text-[var(--fg)] hover:bg-[var(--surface-hover)]">
                {s.nome}
              </button>
              <button
                type="button"
                title={`Excluir "${s.nome}"`}
                aria-label={`Excluir filtro ${s.nome}`}
                onClick={() => excluirFiltro(s.nome)}
                className="cursor-pointer border-l border-[var(--border)] px-1.5 py-1 text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--danger)]"
              >
                <Trash2 className="size-3" />
              </button>
            </span>
          ))}
          {salvando ? (
            <span className="inline-flex items-center gap-1">
              <Input
                autoFocus
                placeholder="Nome do filtro (ex.: AGV TRUCK – MG)"
                className="h-8 w-60 text-xs"
                value={nomeNovoFiltro}
                onChange={(e) => setNomeNovoFiltro(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") salvarFiltro();
                  if (e.key === "Escape") setSalvando(false);
                }}
              />
              <Button size="sm" onClick={salvarFiltro} disabled={!nomeNovoFiltro.trim()}>
                Salvar
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setSalvando(false)}>
                Cancelar
              </Button>
            </span>
          ) : (
            <Button size="sm" variant="secondary" onClick={() => setSalvando(true)}>
              <Save className="size-3.5" /> Salvar filtro atual
            </Button>
          )}
        </div>
      </Card>

      {/* Indicadores */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
        <StatCard titulo="Leads no período" valor={String(ind.totalLeads)} icone={UserPlus} tom="brand" subtitulo={variacao(ind.totalLeads, ind.totalLeadsPeriodoAnterior)} />
        <StatCard titulo="Média por dia" valor={ind.mediaLeadsPorDia.toLocaleString("pt-BR")} icone={CalendarClock} tom="brand" />
        <StatCard titulo="Sem etapa" valor={String(ind.leadsSemEtapa)} icone={Users} tom={ind.leadsSemEtapa > 0 ? "warning" : "neutral"} subtitulo={ind.leadsSemResponsavel > 0 ? `${ind.leadsSemResponsavel} sem responsável` : "ainda não trabalhados"} />
        <StatCard titulo="Em andamento" valor={String(ind.leadsEmAndamento)} icone={Clock} />
        <StatCard titulo="1º contato (média)" valor={horas(ind.tempoMedioPrimeiroContatoHoras)} icone={Timer} tom="warning" subtitulo="da chegada ao 1º contato" />
        <StatCard titulo="Vendas" valor={String(ind.leadsGanhos)} icone={Handshake} tom="success" subtitulo={variacao(ind.leadsGanhos, ind.leadsGanhosPeriodoAnterior)} />
        <StatCard
          titulo="Conversão"
          valor={formatarPercentual(ind.taxaConversao)}
          icone={TrendingUp}
          tom="success"
          subtitulo={`${formatarPercentual(ind.taxaConversaoGeral)} do total de leads`}
        />
        <StatCard titulo="Adesão das vendas" valor={formatarMoeda(ind.valorAdesao)} icone={Wallet} tom="success" subtitulo={ind.mensalidadeMedia > 0 ? `Mensalidade méd. ${formatarMoeda(ind.mensalidadeMedia)}` : undefined} />
        <StatCard titulo="Perdidos" valor={String(ind.leadsPerdidos)} icone={TrendingDown} tom="danger" subtitulo={ind.leadsNaoFazemos > 0 ? `+ ${ind.leadsNaoFazemos} "não fazemos"` : undefined} />
        <StatCard titulo="Sem telefone" valor={String(ind.leadsSemContato)} icone={PhoneMissed} tom={ind.leadsSemContato > 0 ? "warning" : "neutral"} />
      </div>

      {/* Leads por dia + O que */}
      <div className="grid gap-4 lg:grid-cols-3">
        <Secao titulo="Leads por dia (por O que?) e vendas" className="lg:col-span-2">
          <LeadsPorDiaChart evolucao={dados.evolucao} series={dados.evolucaoPorOQue} />
        </Secao>
        <Secao titulo="Leads por O que?">
          <DonutChart itens={dados.porOQue.map((o) => ({ nome: o.nome, valor: o.totalLeads }))} cores={dados.porOQue.map((o, i) => corOQue(o.nome, i))} />
        </Secao>
      </div>

      {/* Desempenho por O que? */}
      <Secao titulo="Desempenho por O que?" icone={<Target className="size-4 text-[var(--fg-muted)]" />}>
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {dados.porOQue.map((o, i) => (
            <button
              key={o.nome}
              type="button"
              onClick={() => o.nome !== "Não informado" && alternarOQue(o.nome)}
              className="cursor-pointer rounded-lg border border-[var(--border)] p-3 text-left transition hover:bg-[var(--surface-hover)]"
              style={{ borderLeft: `4px solid ${corOQue(o.nome, i)}` }}
              title="Clique para filtrar"
            >
              <p className="text-xs font-semibold text-[var(--fg)]">{o.nome}</p>
              <p className="mt-1 text-2xl font-semibold text-[var(--fg)]">{o.totalLeads}</p>
              <p className="text-xs text-[var(--fg-muted)]">
                {o.ganhos} vendas · {formatarPercentual(o.taxaConversao)} conversão · {o.semEtapa} sem etapa
              </p>
            </button>
          ))}
        </div>
      </Secao>

      {/* Funil, canal, origem */}
      <div className="grid gap-4 lg:grid-cols-3">
        <Secao titulo="Onde estão os leads (etapa atual)">
          <BarrasHorizontaisChart
            categorias={dados.funil.map((f) => f.etapa)}
            series={[{ nome: "Leads", valores: dados.funil.map((f) => f.quantidade) }]}
            coresPorBarra={dados.funil.map((f) => f.cor || "#94a3b8")}
            sufixo=" lead(s)"
          />
        </Secao>
        <Secao titulo="Por canal" icone={<Megaphone className="size-4 text-[var(--fg-muted)]" />}>
          <DonutChart itens={dados.porCanal.map((c) => ({ nome: c.nome, valor: c.totalLeads }))} />
        </Secao>
        <Secao titulo="Por origem">
          <DonutChart itens={dados.porOrigem.slice(0, 8).map((o) => ({ nome: o.origem, valor: o.totalLeads }))} />
        </Secao>
      </div>

      {/* Horário + motivos de perda */}
      <div className="grid gap-4 lg:grid-cols-3">
        <Secao titulo="Quando os leads chegam (dia e hora)" icone={<Clock className="size-4 text-[var(--fg-muted)]" />} className="lg:col-span-2">
          <HorarioChart dados={dados.porHorario} />
        </Secao>
        <Secao titulo="Motivos de perda" icone={<XCircle className="size-4 text-[var(--fg-muted)]" />}>
          <BarrasHorizontaisChart
            categorias={dados.motivosPerda.map((m) => m.motivo)}
            series={[{ nome: "Leads", valores: dados.motivosPerda.map((m) => m.quantidade) }]}
            sufixo=" lead(s)"
          />
        </Secao>
      </div>

      {/* Consultores + estados */}
      <div className="grid gap-4 lg:grid-cols-3">
        <Secao
          titulo={`${metricaMensal === "leads" ? "Leads" : "Vendas"} por consultor, mês a mês`}
          className="lg:col-span-2"
          acao={
            <div className="flex gap-1 rounded-lg border border-[var(--border)] p-0.5">
              {(["leads", "ganhos"] as const).map((m) => (
                <button
                  key={m}
                  type="button"
                  onClick={() => setMetricaMensal(m)}
                  className={`cursor-pointer rounded-md px-2 py-0.5 text-xs font-medium ${
                    metricaMensal === m ? "bg-[var(--brand)] text-white" : "text-[var(--fg-muted)] hover:text-[var(--fg)]"
                  }`}
                >
                  {m === "leads" ? "Leads" : "Vendas"}
                </button>
              ))}
            </div>
          }
        >
          <BarrasHorizontaisChart
            categorias={consultoresGrafico.map((c) => c.nome)}
            series={meses.map((mes) => ({
              nome: rotuloMes(mes),
              valores: consultoresGrafico.map((c) => valorMensal(c.id, c.nome, mes)),
            }))}
            sufixo={metricaMensal === "leads" ? " lead(s)" : " venda(s)"}
          />
          <Pagination pagina={paginaGrafico} totalPaginas={totalPaginasGrafico} onChange={setPaginaGrafico} />
        </Secao>
        <Secao titulo="Por estado">
          <BarrasHorizontaisChart
            categorias={estados.map((e) => e.nome)}
            series={[
              { nome: "Leads", valores: estados.map((e) => e.totalLeads) },
              { nome: "Vendas", valores: estados.map((e) => e.ganhos) },
            ]}
          />
        </Secao>
      </div>

      <Secao
        titulo="Desempenho por consultor"
        acao={<span className="text-xs text-[var(--fg-muted)]">{dados.porConsultor.length} consultor(es)</span>}
      >
        <div className="overflow-x-auto">
          <table className="w-full min-w-[820px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 font-medium">Consultor</th>
                <th className="pb-2 text-right font-medium">Leads</th>
                <th className="pb-2 text-right font-medium">Sem etapa</th>
                <th className="pb-2 text-right font-medium">Em andamento</th>
                <th className="pb-2 text-right font-medium">Vendas</th>
                <th className="pb-2 text-right font-medium">Perdidos</th>
                <th className="pb-2 text-right font-medium">Conversão</th>
                <th className="pb-2 text-right font-medium">1º contato</th>
                <th className="pb-2 text-right font-medium">Sem telefone</th>
              </tr>
            </thead>
            <tbody>
              {consultoresDaPagina.map((c) => (
                <tr key={c.id ?? "sem"} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2 font-medium text-[var(--fg)]">{c.nome}</td>
                  <td className="py-2 text-right text-[var(--fg)]">{c.totalLeads}</td>
                  <td className={`py-2 text-right ${c.semEtapa > 0 ? "font-semibold text-[var(--warning)]" : "text-[var(--fg-muted)]"}`}>{c.semEtapa}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{c.emAndamento}</td>
                  <td className="py-2 text-right font-semibold text-[var(--success)]">{c.ganhos}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{c.perdidos}</td>
                  <td className="py-2 text-right text-[var(--fg)]">{formatarPercentual(c.taxaConversao)}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{horas(c.tempoMedioPrimeiroContatoHoras)}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{c.semContato}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination pagina={paginaConsultores} totalPaginas={totalPaginasConsultores} onChange={setPaginaConsultores} />
      </Secao>

      <Secao
        titulo="Por campanha"
        icone={<Target className="size-4 text-[var(--fg-muted)]" />}
        acao={<span className="text-xs text-[var(--fg-muted)]">Clique numa campanha para filtrar</span>}
      >
        {dados.porCampanha.length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhuma campanha no período.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] text-sm">
              <thead>
                <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                  <th className="pb-2 font-medium">Campanha</th>
                  <th className="pb-2 font-medium">Origem</th>
                  <th className="pb-2 text-right font-medium">Leads</th>
                  <th className="pb-2 text-right font-medium">Sem etapa</th>
                  <th className="pb-2 text-right font-medium">Vendas</th>
                  <th className="pb-2 text-right font-medium">Perdidos</th>
                  <th className="pb-2 text-right font-medium">Conversão</th>
                  <th className="pb-2 text-right font-medium">Adesão</th>
                  <th className="pb-2 text-right font-medium">Último lead</th>
                </tr>
              </thead>
              <tbody>
                {dados.porCampanha.map((c) => (
                  <tr
                    key={c.campanha}
                    onClick={() => set("campanha", filtros.campanha.includes(c.campanha) ? filtros.campanha.filter((x) => x !== c.campanha) : [...filtros.campanha, c.campanha])}
                    className={`cursor-pointer border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-hover)] ${
                      filtros.campanha.includes(c.campanha) ? "bg-[var(--brand-soft)]" : ""
                    }`}
                  >
                    <td className="py-2 text-[var(--fg)]">{c.campanha}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{c.origem ?? "—"}</td>
                    <td className="py-2 text-right text-[var(--fg)]">{c.totalLeads}</td>
                    <td className="py-2 text-right text-[var(--fg-muted)]">{c.semEtapa}</td>
                    <td className="py-2 text-right font-semibold text-[var(--success)]">{c.ganhos}</td>
                    <td className="py-2 text-right text-[var(--fg-muted)]">{c.perdidos}</td>
                    <td className="py-2 text-right text-[var(--fg)]">{formatarPercentual(c.taxaConversao)}</td>
                    <td className="py-2 text-right text-[var(--fg-muted)]">{formatarMoeda(c.valorAdesao)}</td>
                    <td className="py-2 text-right text-[var(--fg-muted)]">{formatarDataHora(c.ultimoLeadEm)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Secao>

      <Secao
        titulo="Últimos leads"
        acao={
          <span className="text-xs text-[var(--fg-muted)]">
            {(listaLeads?.totalRegistros ?? dados.totalLeadsLista).toLocaleString("pt-BR")} lead(s) no período
          </span>
        }
        className={carregandoLeads ? "opacity-70" : ""}
      >
        {!listaLeads || listaLeads.itens.length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhum lead no período.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[980px] text-sm">
              <thead>
                <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                  <th className="pb-2 font-medium">Nome</th>
                  <th className="pb-2 font-medium">O que?</th>
                  <th className="pb-2 font-medium">Telefone</th>
                  <th className="pb-2 font-medium">UF</th>
                  <th className="pb-2 font-medium">Canal</th>
                  <th className="pb-2 font-medium">Campanha</th>
                  <th className="pb-2 font-medium">Etapa</th>
                  <th className="pb-2 font-medium">Responsável</th>
                  <th className="pb-2 font-medium">Chegada</th>
                </tr>
              </thead>
              <tbody>
                {listaLeads.itens.map((l) => (
                  <tr key={l.id} className="border-b border-[var(--border)] last:border-0">
                    <td className="py-2 font-medium">
                      <Link to={`/app/crm/leads/${l.id}`} className="text-[var(--fg)] hover:text-[var(--brand)] hover:underline">
                        {l.nomeOuRazaoSocial}
                      </Link>
                    </td>
                    <td className="py-2">{l.oQue && l.oQue !== "Não informado" ? <Badge variant="info">{l.oQue}</Badge> : <span className="text-[var(--fg-muted)]">—</span>}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{formatarTelefone(l.telefone) || "—"}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{l.estado ?? "—"}</td>
                    <td className="py-2 text-xs text-[var(--fg-muted)]">{l.canal ?? "—"}</td>
                    <td className="max-w-48 truncate py-2 text-[var(--fg-muted)]" title={l.campanha ?? undefined}>
                      {l.campanha ?? "—"}
                    </td>
                    <td className="py-2">
                      <Badge variant={l.etapaNome ? (l.etapaNome.startsWith("Venda") ? "success" : "neutral") : "warning"}>{l.etapaNome ?? "Sem etapa"}</Badge>
                    </td>
                    <td className="py-2 text-[var(--fg-muted)]">{l.responsavelNome ?? "Sem responsável"}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{formatarDataHora(l.criadoEm)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {listaLeads && (
          <Pagination pagina={listaLeads.pagina} totalPaginas={listaLeads.totalPaginas} onChange={setPaginaLeads} />
        )}
      </Secao>
    </div>
  );
}
