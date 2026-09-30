import {
  AlertTriangle,
  CalendarCheck,
  CalendarDays,
  CalendarRange,
  Check,
  ChevronLeft,
  ChevronRight,
  Clock,
  List,
  Plus,
  RotateCcw,
} from "lucide-react";
import { useEffect, useMemo, useState, type DragEvent } from "react";
import { api, ApiRequestError, isAbortError, toQueryString } from "../../lib/api";
import { useAbrirLead } from "../../lib/painelLead";
import {
  StatusAtividade,
  TipoAtividade,
  VisaoAtividade,
  type Activity,
  type ActivityCreateRequest,
  type ActivityUpdateRequest,
  type LeadListItem,
  type PagedResult,
  type VendedorResumo,
} from "../../lib/types";
import { Button, Card, Checkbox, ErrorState, Modal, Select, Skeleton, useToast } from "../../components/ui";
import { ActivityForm, tipoLabel, type ActivityFormValues } from "../../components/crm/ActivityForm";
import { LeadPicker } from "../../components/crm/LeadPicker";
import { avisarAtividadesAlteradas, concluirAtividade, EVENTO_ATIVIDADES_ALTERADAS } from "../../components/crm/NotificacoesAtividades";
import { useAuth } from "../../context/AuthContext";

type Visao = "mes" | "semana" | "lista";

const DIAS_SEMANA = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];
const MESES = ["Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"];

/** Uma cor por tipo de atividade — a mesma no mês, na semana, na lista e na legenda. */
const COR_TIPO: Record<TipoAtividade, string> = {
  [TipoAtividade.Ligacao]: "#2563eb",
  [TipoAtividade.WhatsApp]: "#16a34a",
  [TipoAtividade.Email]: "#0ea5e9",
  [TipoAtividade.Reuniao]: "#8b5cf6",
  [TipoAtividade.Visita]: "#f97316",
  [TipoAtividade.Retorno]: "#d97706",
  [TipoAtividade.Tarefa]: "#475569",
  [TipoAtividade.Observacao]: "#94a3b8",
};

const CHAVE_PREFERENCIAS = "css:agenda:preferencias";

// ---------- datas (sempre no horário local, que é o de Brasília) ----------

function chaveDia(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
function somarDias(d: Date, n: number) {
  const c = new Date(d);
  c.setDate(c.getDate() + n);
  return c;
}
function inicioDaSemana(d: Date) {
  const c = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  return somarDias(c, -((c.getDay() + 6) % 7));
}
/** Grade do mês: começa na segunda-feira da semana do dia 1 e termina no domingo da última semana. */
function gradeDoMes(ref: Date) {
  const primeiro = new Date(ref.getFullYear(), ref.getMonth(), 1);
  const ultimo = new Date(ref.getFullYear(), ref.getMonth() + 1, 0);
  const inicio = inicioDaSemana(primeiro);
  const fim = somarDias(inicioDaSemana(ultimo), 6);
  const dias: Date[] = [];
  for (let d = inicio; d <= fim; d = somarDias(d, 1)) dias.push(d);
  return dias;
}
function hora(iso: string) {
  return new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}
function datetimeLocal(dia: Date, horaPadrao = 9) {
  return `${chaveDia(dia)}T${String(horaPadrao).padStart(2, "0")}:00`;
}
/** "Quarta-feira, 30 de setembro" (só a primeira letra maiúscula). */
function tituloDia(d: Date) {
  const texto = d.toLocaleDateString("pt-BR", { weekday: "long", day: "2-digit", month: "long" });
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

function lerPreferencias(): { visao: Visao; mostrarConcluidas: boolean } {
  try {
    const p = JSON.parse(localStorage.getItem(CHAVE_PREFERENCIAS) ?? "{}");
    return { visao: p.visao === "semana" || p.visao === "lista" ? p.visao : "mes", mostrarConcluidas: !!p.mostrarConcluidas };
  } catch {
    return { visao: "mes", mostrarConcluidas: false };
  }
}

/**
 * Agenda: calendário do mês (ou semana/lista) com as atividades de cada dia. Check conclui a
 * atividade (ela sai da agenda), arrastar para outro dia reagenda, clicar no dia mostra o dia ao
 * lado e permite agendar nele, clicar na atividade abre o lead no painel lateral.
 */
export function AgendaPage() {
  const { sessao, temPapel } = useAuth();
  const ehGestao = temPapel("Admin", "GestorMaster", "GestorComercial");
  const abrirLead = useAbrirLead();
  const { notificar } = useToast();

  const preferencias = useMemo(lerPreferencias, []);
  const [visao, setVisao] = useState<Visao>(preferencias.visao);
  const [mostrarConcluidas, setMostrarConcluidas] = useState(preferencias.mostrarConcluidas);
  const [referencia, setReferencia] = useState(() => new Date());
  const [diaSelecionado, setDiaSelecionado] = useState(() => new Date());
  const [tiposOcultos, setTiposOcultos] = useState<TipoAtividade[]>([]);
  const [responsavelId, setResponsavelId] = useState<string>("");
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);

  const [atividades, setAtividades] = useState<Activity[] | null>(null);
  const [atrasadas, setAtrasadas] = useState<Activity[]>([]);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [concluindo, setConcluindo] = useState<string | null>(null);
  const [arrastando, setArrastando] = useState<Activity | null>(null);
  const [diaSobre, setDiaSobre] = useState<string | null>(null);

  const [novaNoDia, setNovaNoDia] = useState<Date | null>(null);
  const [leadNova, setLeadNova] = useState<LeadListItem | null>(null);
  const [salvando, setSalvando] = useState(false);

  useEffect(() => {
    try {
      localStorage.setItem(CHAVE_PREFERENCIAS, JSON.stringify({ visao, mostrarConcluidas }));
    } catch {
      /* sem armazenamento */
    }
  }, [visao, mostrarConcluidas]);

  // Concluída pelo sino (ou em outra tela): recarrega a agenda.
  useEffect(() => {
    const aoAlterar = () => setRecarregar((n) => n + 1);
    window.addEventListener(EVENTO_ATIVIDADES_ALTERADAS, aoAlterar);
    return () => window.removeEventListener(EVENTO_ATIVIDADES_ALTERADAS, aoAlterar);
  }, []);

  useEffect(() => {
    if (!ehGestao) return;
    api.get<VendedorResumo[]>("/crm/management/vendedores").then(setVendedores).catch(() => setVendedores([]));
  }, [ehGestao]);

  // Período carregado conforme a visão.
  const dias = useMemo(() => {
    if (visao === "semana") return Array.from({ length: 7 }, (_, i) => somarDias(inicioDaSemana(referencia), i));
    return gradeDoMes(referencia);
  }, [visao, referencia]);
  const inicio = dias[0];
  const fim = dias[dias.length - 1];

  useEffect(() => {
    const controller = new AbortController();
    setErro(null);
    const filtroBase = { responsavelId: responsavelId || undefined };
    async function carregarTudo() {
      // O período pode ter mais atividades que uma página (máx. 100): busca página por página.
      const todas: Activity[] = [];
      for (let pagina = 1; pagina <= 20; pagina++) {
        const res = await api.get<PagedResult<Activity>>(
          `/crm/activities${toQueryString({
            ...filtroBase,
            visao: VisaoAtividade.Periodo,
            dataReferencia: chaveDia(inicio),
            dataFim: chaveDia(fim),
            tamanhoPagina: 100,
            pagina,
          })}`,
          controller.signal
        );
        todas.push(...res.itens);
        if (pagina >= res.totalPaginas) break;
      }
      const atr = await api.get<PagedResult<Activity>>(
        `/crm/activities${toQueryString({ ...filtroBase, visao: VisaoAtividade.Atrasadas, tamanhoPagina: 100 })}`,
        controller.signal
      );
      setAtividades(todas);
      setAtrasadas(atr.itens);
    }
    carregarTudo().catch((e) => {
      if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar a agenda.");
    });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chaveDia(inicio), chaveDia(fim), responsavelId, recarregar]);

  const visiveis = useMemo(
    () =>
      (atividades ?? []).filter(
        (a) =>
          a.status !== StatusAtividade.Cancelada &&
          (mostrarConcluidas || a.status === StatusAtividade.Pendente) &&
          !tiposOcultos.includes(a.tipo)
      ),
    [atividades, mostrarConcluidas, tiposOcultos]
  );

  const porDia = useMemo(() => {
    const mapa = new Map<string, Activity[]>();
    for (const a of [...visiveis].sort((x, y) => x.dataHoraPrevista.localeCompare(y.dataHoraPrevista))) {
      const chave = chaveDia(new Date(a.dataHoraPrevista));
      mapa.set(chave, [...(mapa.get(chave) ?? []), a]);
    }
    return mapa;
  }, [visiveis]);

  // Indicadores do topo.
  const hojeChave = chaveDia(new Date());
  const pendentes = (atividades ?? []).filter((a) => a.status === StatusAtividade.Pendente);
  const qtdHoje = pendentes.filter((a) => chaveDia(new Date(a.dataHoraPrevista)) === hojeChave).length;
  const inicioSemanaAtual = chaveDia(inicioDaSemana(new Date()));
  const fimSemanaAtual = chaveDia(somarDias(inicioDaSemana(new Date()), 6));
  const qtdSemana = pendentes.filter((a) => {
    const c = chaveDia(new Date(a.dataHoraPrevista));
    return c >= inicioSemanaAtual && c <= fimSemanaAtual;
  }).length;
  const qtdConcluidasNoPeriodo = (atividades ?? []).filter((a) => a.status === StatusAtividade.Concluida).length;

  function navegar(direcao: -1 | 1) {
    setReferencia((r) =>
      visao === "semana" ? somarDias(r, 7 * direcao) : new Date(r.getFullYear(), r.getMonth() + direcao, 1)
    );
  }

  function irParaHoje() {
    setReferencia(new Date());
    setDiaSelecionado(new Date());
  }

  const tituloPeriodo =
    visao === "semana"
      ? `${inicio.toLocaleDateString("pt-BR", { day: "2-digit", month: "short" })} – ${fim.toLocaleDateString("pt-BR", { day: "2-digit", month: "short", year: "numeric" })}`
      : `${MESES[referencia.getMonth()]} de ${referencia.getFullYear()}`;

  async function concluir(a: Activity) {
    setConcluindo(a.id);
    try {
      await concluirAtividade(a);
      setAtividades((l) => l && l.map((x) => (x.id === a.id ? { ...x, status: StatusAtividade.Concluida, atrasada: false } : x)));
      setAtrasadas((l) => l.filter((x) => x.id !== a.id));
      notificar("success", "Atividade concluída.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível concluir a atividade.");
    } finally {
      setConcluindo(null);
    }
  }

  /** Arrastar para outro dia: mesma hora, dia novo. */
  async function reagendar(a: Activity, dia: Date) {
    const antiga = new Date(a.dataHoraPrevista);
    const nova = new Date(dia.getFullYear(), dia.getMonth(), dia.getDate(), antiga.getHours(), antiga.getMinutes());
    if (chaveDia(nova) === chaveDia(antiga)) return;
    const request: ActivityUpdateRequest = {
      tipo: a.tipo,
      assunto: a.assunto,
      descricao: a.descricao ?? null,
      dataHoraPrevista: nova.toISOString(),
      lembreteMinutosAntes: a.lembreteMinutosAntes ?? null,
      rowVersion: a.rowVersion,
    };
    // Mostra já no dia novo; volta se o servidor recusar.
    const anteriores = atividades;
    setAtividades((l) => l && l.map((x) => (x.id === a.id ? { ...x, dataHoraPrevista: nova.toISOString(), atrasada: nova < new Date() } : x)));
    try {
      await api.put(`/crm/activities/${a.id}`, request);
      notificar("success", `Reagendada para ${nova.toLocaleDateString("pt-BR")}.`);
      avisarAtividadesAlteradas();
    } catch (e) {
      setAtividades(anteriores);
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível reagendar.");
    }
  }

  async function criarAtividade(valores: ActivityFormValues) {
    if (!leadNova) return;
    setSalvando(true);
    try {
      const request: ActivityCreateRequest = {
        leadId: leadNova.id,
        tipo: valores.tipo,
        assunto: valores.assunto,
        descricao: valores.descricao || null,
        dataHoraPrevista: new Date(valores.dataHoraPrevista).toISOString(),
        lembreteMinutosAntes: valores.lembreteMinutosAntes ? Number(valores.lembreteMinutosAntes) : null,
      };
      await api.post("/crm/activities", request);
      notificar("success", "Atividade agendada.");
      setNovaNoDia(null);
      setLeadNova(null);
      avisarAtividadesAlteradas();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível agendar a atividade.");
    } finally {
      setSalvando(false);
    }
  }

  function aoSoltar(e: DragEvent, dia: Date) {
    e.preventDefault();
    setDiaSobre(null);
    if (arrastando) reagendar(arrastando, dia);
    setArrastando(null);
  }

  // ---------- pedaços da tela ----------

  const CheckBotao = ({ a, pequeno }: { a: Activity; pequeno?: boolean }) =>
    a.status === StatusAtividade.Concluida ? (
      <span
        className={`flex shrink-0 items-center justify-center rounded-full bg-[var(--success)] text-white ${pequeno ? "size-3.5" : "size-5"}`}
        title="Concluída"
      >
        <Check className={pequeno ? "size-2.5" : "size-3"} strokeWidth={3} />
      </span>
    ) : (
      <button
        type="button"
        title="Marcar como feita"
        aria-label={`Marcar "${a.assunto}" como feita`}
        disabled={concluindo === a.id}
        onClick={(e) => {
          e.stopPropagation();
          concluir(a);
        }}
        className={`focus-ring flex shrink-0 cursor-pointer items-center justify-center rounded-full border-2 bg-[var(--surface)] text-transparent transition hover:border-[var(--success)] hover:text-[var(--success)] disabled:opacity-50 ${
          pequeno ? "size-3.5 border-[1.5px]" : "size-5"
        }`}
        style={{ borderColor: pequeno ? COR_TIPO[a.tipo] : undefined }}
      >
        <Check className={pequeno ? "size-2.5" : "size-3"} strokeWidth={3} />
      </button>
    );

  /** Atividade dentro da célula do mês: bolinha de check, hora e assunto, na cor do tipo. */
  const Pilula = ({ a }: { a: Activity }) => (
    <div
      draggable={a.status === StatusAtividade.Pendente}
      onDragStart={() => setArrastando(a)}
      onDragEnd={() => setArrastando(null)}
      onClick={(e) => {
        e.stopPropagation();
        abrirLead(a.leadId);
      }}
      title={`${tipoLabel[a.tipo]} · ${a.assunto} · ${a.leadNome}${a.status === StatusAtividade.Pendente ? " (arraste para reagendar)" : ""}`}
      className={`group flex cursor-pointer items-center gap-1 truncate rounded-md px-1 py-0.5 text-[11px] leading-tight transition hover:brightness-95 ${
        a.status === StatusAtividade.Concluida ? "opacity-50 line-through" : ""
      }`}
      style={{ backgroundColor: `${COR_TIPO[a.tipo]}1f`, color: a.atrasada ? "var(--danger)" : "var(--fg)" }}
    >
      <CheckBotao a={a} pequeno />
      <span className="shrink-0 font-semibold" style={{ color: COR_TIPO[a.tipo] }}>
        {hora(a.dataHoraPrevista)}
      </span>
      <span className="truncate">{a.assunto}</span>
    </div>
  );

  /** Atividade completa (lista do dia, semana e lista). */
  const Cartao = ({ a }: { a: Activity }) => (
    <div
      draggable={a.status === StatusAtividade.Pendente}
      onDragStart={() => setArrastando(a)}
      onDragEnd={() => setArrastando(null)}
      className={`flex items-start gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-2.5 text-sm transition hover:shadow-sm ${
        a.status === StatusAtividade.Concluida ? "opacity-60" : ""
      }`}
      style={{ borderLeft: `3px solid ${COR_TIPO[a.tipo]}` }}
    >
      <CheckBotao a={a} />
      <button type="button" onClick={() => abrirLead(a.leadId)} className="min-w-0 flex-1 cursor-pointer text-left">
        <div className="flex items-center justify-between gap-2">
          <span className="text-xs font-semibold" style={{ color: COR_TIPO[a.tipo] }}>
            {tipoLabel[a.tipo]}
          </span>
          <span className={`flex items-center gap-1 text-xs ${a.atrasada ? "font-semibold text-[var(--danger)]" : "text-[var(--fg-muted)]"}`}>
            <Clock className="size-3" /> {hora(a.dataHoraPrevista)}
          </span>
        </div>
        <p className={`mt-0.5 truncate font-medium text-[var(--fg)] ${a.status === StatusAtividade.Concluida ? "line-through" : ""}`}>{a.assunto}</p>
        <p className="truncate text-xs text-[var(--fg-muted)]">
          {a.leadNome}
          {ehGestao && ` · ${a.responsavelNome}`}
        </p>
      </button>
    </div>
  );

  const selecionadoChave = chaveDia(diaSelecionado);
  const doDiaSelecionado = porDia.get(selecionadoChave) ?? [];

  return (
    <div className="space-y-4">
      {/* Cabeçalho */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="flex size-11 items-center justify-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand)]">
            <CalendarDays className="size-5" />
          </div>
          <div>
            <h1 className="text-xl font-semibold text-[var(--fg)]">{tituloPeriodo}</h1>
            <p className="text-sm text-[var(--fg-muted)]">Agenda de atividades · arraste uma atividade para outro dia para reagendar</p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex items-center rounded-lg border border-[var(--border)] bg-[var(--surface)]">
            <button type="button" onClick={() => navegar(-1)} className="cursor-pointer rounded-l-lg p-2 hover:bg-[var(--surface-hover)]" aria-label="Anterior">
              <ChevronLeft className="size-4" />
            </button>
            <button type="button" onClick={irParaHoje} className="cursor-pointer border-x border-[var(--border)] px-3 py-1.5 text-sm font-medium hover:bg-[var(--surface-hover)]">
              Hoje
            </button>
            <button type="button" onClick={() => navegar(1)} className="cursor-pointer rounded-r-lg p-2 hover:bg-[var(--surface-hover)]" aria-label="Próximo">
              <ChevronRight className="size-4" />
            </button>
          </div>
          <div className="flex rounded-lg border border-[var(--border)] bg-[var(--surface)] p-0.5">
            {(
              [
                ["mes", "Mês", CalendarDays],
                ["semana", "Semana", CalendarRange],
                ["lista", "Lista", List],
              ] as const
            ).map(([v, rotulo, Icone]) => (
              <button
                key={v}
                type="button"
                onClick={() => setVisao(v)}
                className={`flex cursor-pointer items-center gap-1.5 rounded-md px-2.5 py-1 text-sm font-medium transition ${
                  visao === v ? "bg-[var(--brand)] text-white" : "text-[var(--fg-muted)] hover:text-[var(--fg)]"
                }`}
              >
                <Icone className="size-3.5" /> {rotulo}
              </button>
            ))}
          </div>
          <Button onClick={() => setNovaNoDia(diaSelecionado)}>
            <Plus className="size-4" /> Nova atividade
          </Button>
        </div>
      </div>

      {/* Indicadores */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {[
          { rotulo: "Atrasadas", valor: atrasadas.length, cor: "var(--danger)", Icone: AlertTriangle },
          { rotulo: "Para hoje", valor: qtdHoje, cor: "var(--brand)", Icone: Clock },
          { rotulo: "Esta semana", valor: qtdSemana, cor: "#8b5cf6", Icone: CalendarRange },
          { rotulo: "Concluídas no período", valor: qtdConcluidasNoPeriodo, cor: "var(--success)", Icone: CalendarCheck },
        ].map(({ rotulo, valor, cor, Icone }) => (
          <Card key={rotulo} className="flex items-center gap-3 p-3">
            <div className="flex size-9 items-center justify-center rounded-lg" style={{ backgroundColor: `color-mix(in srgb, ${cor} 15%, transparent)`, color: cor }}>
              <Icone className="size-4" />
            </div>
            <div>
              <p className="text-xs text-[var(--fg-muted)]">{rotulo}</p>
              <p className="text-lg font-semibold text-[var(--fg)]">{atividades ? valor : "—"}</p>
            </div>
          </Card>
        ))}
      </div>

      {/* Filtros e legenda */}
      <Card className="flex flex-wrap items-center gap-2 p-3">
        {(Object.keys(COR_TIPO).map(Number) as TipoAtividade[]).map((t) => {
          const oculto = tiposOcultos.includes(t);
          return (
            <button
              key={t}
              type="button"
              onClick={() => setTiposOcultos((l) => (oculto ? l.filter((x) => x !== t) : [...l, t]))}
              className={`flex cursor-pointer items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium transition ${oculto ? "opacity-40" : ""}`}
              style={{ borderColor: COR_TIPO[t], color: COR_TIPO[t] }}
              title={oculto ? "Mostrar" : "Esconder"}
            >
              <span className="size-2 rounded-full" style={{ backgroundColor: COR_TIPO[t] }} /> {tipoLabel[t]}
            </button>
          );
        })}
        <div className="ml-auto flex flex-wrap items-center gap-3">
          {ehGestao && (
            <div className="w-52">
              <Select value={responsavelId} onChange={(e) => setResponsavelId(e.target.value)} aria-label="Consultor">
                <option value="">Todos os consultores</option>
                {sessao && <option value={sessao.id}>Só as minhas</option>}
                {vendedores
                  .filter((v) => v.id !== sessao?.id)
                  .map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.nome}
                    </option>
                  ))}
              </Select>
            </div>
          )}
          <Checkbox label="Mostrar concluídas" checked={mostrarConcluidas} onChange={(e) => setMostrarConcluidas(e.target.checked)} />
        </div>
      </Card>

      {erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !atividades ? (
        <Skeleton className="h-[32rem]" />
      ) : visao === "lista" ? (
        <Card className="p-4">
          {[...porDia.keys()].filter((k) => k >= chaveDia(new Date(referencia.getFullYear(), referencia.getMonth(), 1)) && k <= chaveDia(new Date(referencia.getFullYear(), referencia.getMonth() + 1, 0))).length === 0 ? (
            <p className="py-10 text-center text-sm text-[var(--fg-muted)]">Nenhuma atividade neste mês.</p>
          ) : (
            <div className="space-y-5">
              {[...porDia.entries()]
                .filter(([k]) => k.slice(0, 7) === chaveDia(referencia).slice(0, 7))
                .map(([k, lista]) => {
                  const [a, m, d] = k.split("-").map(Number);
                  const dia = new Date(a, m - 1, d);
                  return (
                    <div key={k}>
                      <p className={`mb-2 text-sm font-semibold ${k === hojeChave ? "text-[var(--brand)]" : "text-[var(--fg)]"}`}>
                        {tituloDia(dia)}
                        {k === hojeChave && " · hoje"}
                      </p>
                      <div className="grid gap-2 md:grid-cols-2 xl:grid-cols-3">
                        {lista.map((at) => (
                          <Cartao key={at.id} a={at} />
                        ))}
                      </div>
                    </div>
                  );
                })}
            </div>
          )}
        </Card>
      ) : (
        <div className={`grid gap-4 ${visao === "mes" ? "xl:grid-cols-[1fr_20rem]" : ""}`}>
          {/* Calendário */}
          <Card className="overflow-hidden p-0">
            <div className="grid grid-cols-7 border-b border-[var(--border)] bg-[var(--surface-hover)]">
              {DIAS_SEMANA.map((d, i) => (
                <div key={d} className={`px-2 py-2 text-center text-xs font-semibold uppercase ${i >= 5 ? "text-[var(--fg-muted)]" : "text-[var(--fg)]"}`}>
                  {d}
                </div>
              ))}
            </div>
            <div className="grid grid-cols-7">
              {dias.map((dia) => {
                const k = chaveDia(dia);
                const lista = porDia.get(k) ?? [];
                const foraDoMes = visao === "mes" && dia.getMonth() !== referencia.getMonth();
                const ehHoje = k === hojeChave;
                const selecionado = k === selecionadoChave;
                const limite = visao === "mes" ? 3 : 50;
                return (
                  <div
                    key={k}
                    onClick={() => setDiaSelecionado(dia)}
                    onDragOver={(e) => {
                      e.preventDefault();
                      setDiaSobre(k);
                    }}
                    onDragLeave={() => setDiaSobre((atual) => (atual === k ? null : atual))}
                    onDrop={(e) => aoSoltar(e, dia)}
                    className={`group relative cursor-pointer border-b border-r border-[var(--border)] p-1.5 transition-colors [&:nth-child(7n)]:border-r-0 ${
                      visao === "mes" ? "min-h-28" : "min-h-[26rem]"
                    } ${foraDoMes ? "bg-[var(--surface-hover)]/50" : "bg-[var(--surface)]"} ${
                      diaSobre === k ? "!bg-[var(--brand-soft)]" : selecionado && visao === "mes" ? "bg-[var(--brand-soft)]/40" : "hover:bg-[var(--surface-hover)]/60"
                    }`}
                  >
                    <div className="mb-1 flex items-center justify-between">
                      <span
                        className={`flex size-6 items-center justify-center rounded-full text-xs font-semibold ${
                          ehHoje ? "bg-[var(--brand)] text-white" : foraDoMes ? "text-[var(--fg-muted)]/60" : "text-[var(--fg)]"
                        }`}
                      >
                        {dia.getDate()}
                      </span>
                      <button
                        type="button"
                        title="Agendar neste dia"
                        aria-label={`Agendar em ${dia.toLocaleDateString("pt-BR")}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          setDiaSelecionado(dia);
                          setNovaNoDia(dia);
                        }}
                        className="flex size-5 cursor-pointer items-center justify-center rounded text-[var(--fg-muted)] opacity-0 transition hover:bg-[var(--surface-hover)] hover:text-[var(--brand)] group-hover:opacity-100"
                      >
                        <Plus className="size-3.5" />
                      </button>
                    </div>
                    <div className="space-y-0.5">
                      {visao === "mes"
                        ? lista.slice(0, limite).map((a) => <Pilula key={a.id} a={a} />)
                        : lista.map((a) => (
                            <div key={a.id} className="mb-1.5">
                              <Cartao a={a} />
                            </div>
                          ))}
                      {lista.length > limite && (
                        <p className="px-1 text-[11px] font-medium text-[var(--brand)]">+{lista.length - limite} mais</p>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </Card>

          {/* Dia selecionado (visão mês) */}
          {visao === "mes" && (
            <Card className="flex flex-col p-4 xl:sticky xl:top-20 xl:max-h-[calc(100vh-7rem)]">
              <div className="mb-3 flex items-start justify-between gap-2">
                <div>
                  <p className="text-sm font-semibold text-[var(--fg)]">{tituloDia(diaSelecionado)}</p>
                  <p className="text-xs text-[var(--fg-muted)]">
                    {doDiaSelecionado.length} atividade(s){selecionadoChave === hojeChave && " · hoje"}
                  </p>
                </div>
                <Button size="sm" variant="secondary" onClick={() => setNovaNoDia(diaSelecionado)}>
                  <Plus className="size-3.5" /> Agendar
                </Button>
              </div>
              <div className="flex-1 space-y-2 overflow-y-auto">
                {doDiaSelecionado.length === 0 ? (
                  <p className="py-8 text-center text-sm text-[var(--fg-muted)]">Nada agendado neste dia.</p>
                ) : (
                  doDiaSelecionado.map((a) => <Cartao key={a.id} a={a} />)
                )}
              </div>
              {atrasadas.length > 0 && (
                <div className="mt-4 border-t border-[var(--border)] pt-3">
                  <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase text-[var(--danger)]">
                    <RotateCcw className="size-3.5" /> Atrasadas ({atrasadas.length})
                  </p>
                  <div className="max-h-64 space-y-2 overflow-y-auto">
                    {atrasadas.slice(0, 20).map((a) => (
                      <Cartao key={a.id} a={a} />
                    ))}
                  </div>
                </div>
              )}
            </Card>
          )}
        </div>
      )}

      {/* Nova atividade (escolhe o lead, depois o formulário já com o dia) */}
      <Modal
        open={!!novaNoDia}
        onClose={() => {
          setNovaNoDia(null);
          setLeadNova(null);
        }}
        title={novaNoDia ? `Nova atividade · ${novaNoDia.toLocaleDateString("pt-BR")}` : "Nova atividade"}
      >
        {novaNoDia &&
          (!leadNova ? (
            <LeadPicker onSelecionar={setLeadNova} />
          ) : (
            <div>
              <p className="mb-3 text-sm text-[var(--fg-muted)]">
                Lead: <strong className="text-[var(--fg)]">{leadNova.nomeOuRazaoSocial}</strong>
              </p>
              <ActivityForm
                valoresIniciais={{ tipo: TipoAtividade.Ligacao, assunto: "", descricao: "", dataHoraPrevista: datetimeLocal(novaNoDia), lembreteMinutosAntes: "" }}
                salvando={salvando}
                onSubmit={criarAtividade}
                onCancel={() => {
                  setNovaNoDia(null);
                  setLeadNova(null);
                }}
              />
            </div>
          ))}
      </Modal>
    </div>
  );
}
