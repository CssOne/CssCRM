import { ArrowRightLeft, List, Plus, Save, Trash2, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, ApiRequestError, isAbortError, toQueryString } from "../../lib/api";
import {
  TipoEtapaPipeline,
  type LeadCreateRequest,
  type LeadDuplicateWarning,
  type LeadKanbanBoard,
  type LeadKanbanCard,
  type LeadKanbanColumn,
  type PipelineBoard,
  type Regional,
  type VendedorResumo,
} from "../../lib/types";
import { Button, ConfirmDialog, EmptyState, ErrorState, Input, Modal, Select, Skeleton, useToast } from "../../components/ui";
import { LeadForm, leadFormVazio, paraLeadCreateRequest, type LeadFormValues } from "../../components/crm/LeadForm";
import { VendaConcluidaDialog } from "../../components/crm/VendaConcluidaDialog";
import { StageChangeDialog } from "../../components/crm/StageChangeDialog";
import { VeiculoNaoFazemosDialog } from "../../components/crm/VeiculoNaoFazemosDialog";
import { AdesaoCotacaoDialog } from "../../components/crm/AdesaoCotacaoDialog";
import { AlterarResponsavelDialog } from "../../components/crm/AlterarResponsavelDialog";
import { CartaoLead, classificarCartao } from "../../components/crm/CartaoLead";
import { MultiSelect } from "../../components/MultiSelect";
import { OPCOES_FILTRO_TIPO_INDICACAO } from "../../lib/opcoesLead";
import { useAuth } from "../../context/AuthContext";
import { useCrmEventos } from "../../lib/useCrmEventos";

/** Prefixo comum das duas colunas "Em atendimento (Leads)"/"Em atendimento (Indicação)" — uma só
 * aceita cartões "Lead" e a outra só "Indicação" (ver classificarCartao/colunasEmAtendimento abaixo). */
const ETAPA_EM_ATENDIMENTO = "Em atendimento";
/** Nome da etapa "terminal com sucesso" do quadro de leads — ver CrmSeeder.cs. Ao contrário do
 * Pipeline (TipoEtapaPipeline.Ganho), CrmLeadStage não tem um enum de tipo, só o nome mesmo. */
const ETAPA_VENDA_CONCLUIDA = "Venda concluída";
/** Nome da etapa terminal "perdida" do quadro de leads — mesma lógica de ETAPA_VENDA_CONCLUIDA acima. */
const ETAPA_PERDIDO = "Perdido";
/** Nome da etapa "veículo fora do que a CSS Brasil atende" — mesma lógica de ETAPA_VENDA_CONCLUIDA acima. */
const ETAPA_NAO_FAZEMOS = "Não fazemos";
/** Etapa que exige o valor da adesão (o servidor também recusa sem ele — ver LeadService.MudarEtapaAsync). */
const ETAPA_COTACAO = "Cotação";

/** Quantos cartões cada coluna busca por vez no servidor (carga do quadro e cada "Ver mais") — a base
 * tem dezenas de milhares de leads, então nunca se carrega tudo de uma vez. */
const CARTOES_POR_PAGINA = 30;


/** Filtros do quadro — guardados no navegador para não se perderem ao abrir um card ou recarregar. */
interface FiltrosQuadro {
  busca: string;
  responsavelId: string[];
  regional: string[];
  origem: string[];
  incluirArquivados: boolean;
  categoria: string[];
  fonte: string[];
  tipoIndicacao: string[];
  dataChegadaInicio: string;
  dataChegadaFim: string;
  dataVendaInicio: string;
  dataVendaFim: string;
}

interface FiltroSalvo {
  nome: string;
  filtros: FiltrosQuadro;
}

const FILTROS_VAZIOS: FiltrosQuadro = {
  busca: "",
  responsavelId: [],
  regional: [],
  origem: [],
  incluirArquivados: false,
  categoria: [],
  fonte: [],
  tipoIndicacao: [],
  dataChegadaInicio: "",
  dataChegadaFim: "",
  dataVendaInicio: "",
  dataVendaFim: "",
};

const CAMPOS_MULTIPLOS = ["responsavelId", "regional", "origem", "categoria", "fonte", "tipoIndicacao"] as const;

/** Completa e corrige filtros guardados — os salvos antes da múltipla escolha tinham um valor só (texto). */
function normalizarFiltros(bruto: unknown): FiltrosQuadro {
  const f = { ...FILTROS_VAZIOS, ...(typeof bruto === "object" && bruto ? bruto : {}) } as Record<string, unknown>;
  for (const campo of CAMPOS_MULTIPLOS) {
    const valor = f[campo];
    f[campo] = Array.isArray(valor) ? valor.map(String) : typeof valor === "string" && valor ? [valor] : [];
  }
  return f as unknown as FiltrosQuadro;
}

function lerFiltros(chave: string): FiltrosQuadro {
  try {
    const bruto = localStorage.getItem(chave);
    return bruto ? normalizarFiltros(JSON.parse(bruto)) : FILTROS_VAZIOS;
  } catch {
    return FILTROS_VAZIOS;
  }
}

function lerFiltrosSalvos(chave: string): FiltroSalvo[] {
  try {
    const lista = JSON.parse(localStorage.getItem(chave) ?? "[]");
    return Array.isArray(lista) ? lista.map((f) => ({ nome: String(f.nome), filtros: normalizarFiltros(f.filtros) })) : [];
  } catch {
    return [];
  }
}

function gravarJson(chave: string, valor: unknown) {
  try {
    localStorage.setItem(chave, JSON.stringify(valor));
  } catch {
    /* sem storage (aba anônima/bloqueada): os filtros só valem até sair da página */
  }
}

export function LeadsKanbanPage() {
  const navigate = useNavigate();
  const { notificar } = useToast();
  const { temPapel, sessao } = useAuth();
  const podeGerir = temPapel("Admin", "GestorMaster", "GestorComercial");
  const chaveFiltros = `quadro-leads-filtros:${sessao?.id ?? ""}`;
  const chaveFiltrosSalvos = `quadro-leads-filtros-salvos:${sessao?.id ?? ""}`;
  const [filtrosIniciais] = useState(() => lerFiltros(chaveFiltros));
  // Origem (filtro e rodapé do cartão) só para administradores — o servidor também não a envia aos demais.
  const podeVerOrigem = temPapel("Admin", "GestorMaster");
  // Excluir lead é só para Admin/GestorMaster (ver LeadService.ExcluirAsync no back-end).
  const podeExcluir = temPapel("Admin", "GestorMaster");

  const [board, setBoard] = useState<LeadKanbanBoard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [carregandoMais, setCarregandoMais] = useState<string | null>(null);

  const [busca, setBusca] = useState(filtrosIniciais.busca);
  const [responsavelId, setResponsavelId] = useState(filtrosIniciais.responsavelId);
  const [regional, setRegional] = useState(filtrosIniciais.regional);
  const [origem, setOrigem] = useState(filtrosIniciais.origem);
  const [incluirArquivados, setIncluirArquivados] = useState(filtrosIniciais.incluirArquivados);
  const [categoria, setCategoria] = useState(filtrosIniciais.categoria);
  const [fonte, setFonte] = useState(filtrosIniciais.fonte);
  const [tipoIndicacao, setTipoIndicacao] = useState(filtrosIniciais.tipoIndicacao);
  const [dataChegadaInicio, setDataChegadaInicio] = useState(filtrosIniciais.dataChegadaInicio);
  const [dataChegadaFim, setDataChegadaFim] = useState(filtrosIniciais.dataChegadaFim);
  const [dataVendaInicio, setDataVendaInicio] = useState(filtrosIniciais.dataVendaInicio);
  const [dataVendaFim, setDataVendaFim] = useState(filtrosIniciais.dataVendaFim);
  const [filtrosSalvos, setFiltrosSalvos] = useState<FiltroSalvo[]>(() => lerFiltrosSalvos(chaveFiltrosSalvos));
  const [filtroSalvoAtual, setFiltroSalvoAtual] = useState("");
  const [salvandoFiltro, setSalvandoFiltro] = useState(false);
  const [nomeFiltro, setNomeFiltro] = useState("");
  const [trocandoResponsavel, setTrocandoResponsavel] = useState<LeadKanbanCard | null>(null);
  const [colunaSobre, setColunaSobre] = useState<string | null>(null);
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);
  const [regionais, setRegionais] = useState<Regional[]>([]);
  const [origens, setOrigens] = useState<string[]>([]);

  const [cartaoArrastando, setCartaoArrastando] = useState<LeadKanbanCard | null>(null);
  const [modalMobile, setModalMobile] = useState<LeadKanbanCard | null>(null);
  const [leadExcluindo, setLeadExcluindo] = useState<LeadKanbanCard | null>(null);
  const [excluindoLead, setExcluindoLead] = useState(false);
  const [enviando, setEnviando] = useState(false);

  const [modalNovo, setModalNovo] = useState(false);
  const [salvandoNovo, setSalvandoNovo] = useState(false);
  const [duplicidade, setDuplicidade] = useState<LeadDuplicateWarning | null>(null);

  const [pendenciaVenda, setPendenciaVenda] = useState<{ cartao: LeadKanbanCard; etapaId: string } | null>(null);
  const [pendenciaPerda, setPendenciaPerda] = useState<{ cartao: LeadKanbanCard; etapaId: string } | null>(null);
  const [pendenciaNaoFazemos, setPendenciaNaoFazemos] = useState<{ cartao: LeadKanbanCard; etapaId: string } | null>(null);
  const [pendenciaCotacao, setPendenciaCotacao] = useState<{ cartao: LeadKanbanCard; etapaId: string } | null>(null);
  const [etapaGanhoPipelineId, setEtapaGanhoPipelineId] = useState<string | null>(null);

  const boardRef = useRef<LeadKanbanBoard | null>(null);
  boardRef.current = board;

  useEffect(() => {
    api.get<string[]>("/crm/settings/origins").then(setOrigens).catch(() => setOrigens([]));
  }, []);

  // Pra saber pra qual etapa do Pipeline mandar a oportunidade quando um lead é concluído por
  // aqui — o quadro de leads não carrega o Pipeline normalmente, então busca só essa vez.
  useEffect(() => {
    api
      .get<PipelineBoard>("/crm/pipeline")
      .then((board) => {
        const ganho = board.colunas.find((c) => c.etapa.tipo === TipoEtapaPipeline.Ganho);
        setEtapaGanhoPipelineId(ganho?.etapa.id ?? null);
      })
      .catch(() => setEtapaGanhoPipelineId(null));
  }, []);

  useEffect(() => {
    if (!podeGerir) return;
    api.get<VendedorResumo[]>("/crm/management/vendedores").then(setVendedores).catch(() => setVendedores([]));
    api.get<Regional[]>("/crm/settings/regionals").then(setRegionais).catch(() => setRegionais([]));
  }, [podeGerir]);

  const filtro = useMemo(
    () => ({
      busca: busca || undefined,
      responsavelId,
      regional,
      origem,
      incluirArquivados: incluirArquivados || undefined,
      categoria,
      fonte,
      tipoIndicacao,
      dataChegadaInicio: dataChegadaInicio || undefined,
      dataChegadaFim: dataChegadaFim || undefined,
      dataVendaInicio: dataVendaInicio || undefined,
      dataVendaFim: dataVendaFim || undefined,
    }),
    [
      busca,
      responsavelId,
      regional,
      origem,
      incluirArquivados,
      categoria,
      fonte,
      tipoIndicacao,
      dataChegadaInicio,
      dataChegadaFim,
      dataVendaInicio,
      dataVendaFim,
    ]
  );

  const filtrosAtuais: FiltrosQuadro = useMemo(
    () => ({
      busca,
      responsavelId,
      regional,
      origem,
      incluirArquivados,
      categoria,
      fonte,
      tipoIndicacao,
      dataChegadaInicio,
      dataChegadaFim,
      dataVendaInicio,
      dataVendaFim,
    }),
    [busca, responsavelId, regional, origem, incluirArquivados, categoria, fonte, tipoIndicacao, dataChegadaInicio, dataChegadaFim, dataVendaInicio, dataVendaFim]
  );

  // Mantém os filtros ao abrir um card e voltar, ou ao recarregar a página.
  useEffect(() => {
    gravarJson(chaveFiltros, filtrosAtuais);
  }, [chaveFiltros, filtrosAtuais]);

  function aplicarFiltros(f: FiltrosQuadro) {
    setBusca(f.busca);
    setResponsavelId(f.responsavelId);
    setRegional(f.regional);
    setOrigem(f.origem);
    setIncluirArquivados(f.incluirArquivados);
    setCategoria(f.categoria);
    setFonte(f.fonte);
    setTipoIndicacao(f.tipoIndicacao);
    setDataChegadaInicio(f.dataChegadaInicio);
    setDataChegadaFim(f.dataChegadaFim);
    setDataVendaInicio(f.dataVendaInicio);
    setDataVendaFim(f.dataVendaFim);
  }

  function escolherFiltroSalvo(nome: string) {
    setFiltroSalvoAtual(nome);
    const salvo = filtrosSalvos.find((f) => f.nome === nome);
    if (salvo) aplicarFiltros(salvo.filtros);
  }

  function salvarFiltro() {
    const nome = nomeFiltro.trim();
    if (!nome) return;
    // Mesmo nome substitui o filtro salvo anterior.
    const lista = [...filtrosSalvos.filter((f) => f.nome !== nome), { nome, filtros: filtrosAtuais }].sort((a, b) =>
      a.nome.localeCompare(b.nome, "pt-BR")
    );
    setFiltrosSalvos(lista);
    gravarJson(chaveFiltrosSalvos, lista);
    setFiltroSalvoAtual(nome);
    setSalvandoFiltro(false);
    setNomeFiltro("");
    notificar("success", `Filtro "${nome}" salvo.`);
  }

  function excluirFiltroSalvo() {
    if (!filtroSalvoAtual) return;
    const lista = filtrosSalvos.filter((f) => f.nome !== filtroSalvoAtual);
    setFiltrosSalvos(lista);
    gravarJson(chaveFiltrosSalvos, lista);
    notificar("success", `Filtro "${filtroSalvoAtual}" excluído.`);
    setFiltroSalvoAtual("");
  }

  const carregar = useCallback(
    (signal?: AbortSignal, silencioso = false) => {
      if (!silencioso) setCarregando(true);
      setErro(null);
      // Recarga silenciosa (tempo real): mantém as páginas que a pessoa já abriu com "Ver mais".
      const jaCarregados = silencioso ? Math.max(0, ...(boardRef.current?.colunas.map((c) => c.cartoes.length) ?? [])) : 0;
      const cartoesPorColuna = Math.min(200, Math.max(CARTOES_POR_PAGINA, jaCarregados));
      api
        .get<LeadKanbanBoard>(`/crm/leads/kanban${toQueryString({ ...filtro, cartoesPorColuna })}`, signal)
        .then(setBoard)
        .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o quadro de leads."); })
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [filtro]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  // Tempo real: quando o quadro muda (sincronização com o Notion ou outro usuário), recarrega sem
  // piscar. Se a pessoa estiver arrastando um cartão ou com um diálogo de etapa aberto, espera ela
  // terminar para não mexer no quadro debaixo dela.
  const ocupado = !!cartaoArrastando || !!pendenciaVenda || !!pendenciaPerda || !!pendenciaNaoFazemos || !!pendenciaCotacao || enviando;
  const ocupadoRef = useRef(ocupado);
  ocupadoRef.current = ocupado;
  const recargaPendenteRef = useRef(false);

  useCrmEventos(() => {
    if (ocupadoRef.current) {
      recargaPendenteRef.current = true;
    } else {
      carregar(undefined, true);
    }
  });

  useEffect(() => {
    if (!ocupado && recargaPendenteRef.current) {
      recargaPendenteRef.current = false;
      carregar(undefined, true);
    }
  }, [ocupado, carregar]);

  async function verMaisCartoes(coluna: LeadKanbanColumn) {
    const chave = coluna.etapa.id ?? "sem-etapa";
    setCarregandoMais(chave);
    try {
      const proximos = await api.get<LeadKanbanCard[]>(
        `/crm/leads/kanban/coluna${toQueryString({
          ...filtro,
          etapaId: coluna.etapa.id ?? undefined,
          pular: coluna.cartoes.length,
          quantidade: CARTOES_POR_PAGINA,
        })}`
      );
      setBoard((atual) =>
        atual && {
          ...atual,
          colunas: atual.colunas.map((c) => {
            if ((c.etapa.id ?? "sem-etapa") !== chave) return c;
            const jaTem = new Set(c.cartoes.map((x) => x.leadId));
            return { ...c, cartoes: [...c.cartoes, ...proximos.filter((x) => !jaTem.has(x.leadId))] };
          }),
        }
      );
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível carregar mais leads.");
    } finally {
      setCarregandoMais(null);
    }
  }

  function limparFiltros() {
    aplicarFiltros(FILTROS_VAZIOS);
    setFiltroSalvoAtual("");
  }

  function moverCartaoLocal(leadId: string, etapaDestinoId: string | null) {
    setBoard((atual) => {
      if (!atual) return atual;
      let cartao: LeadKanbanCard | undefined;
      const colunas = atual.colunas.map((col) => {
        const encontrado = col.cartoes.find((c) => c.leadId === leadId);
        if (!encontrado) return col;
        cartao = encontrado;
        return { ...col, cartoes: col.cartoes.filter((c) => c.leadId !== leadId), total: col.total - 1 };
      });
      if (!cartao) return atual;
      return {
        ...atual,
        colunas: colunas.map((col) =>
          col.etapa.id === etapaDestinoId ? { ...col, cartoes: [cartao!, ...col.cartoes], total: col.total + 1 } : col
        ),
      };
    });
  }

  async function moverPara(
    cartao: LeadKanbanCard,
    etapaId: string | null,
    extra?: { motivoPerdaId?: string; motivoPerdaObservacao?: string; veiculoNaoAtendido?: string; valorAdesao?: number }
  ) {
    const boardAnterior = boardRef.current;
    moverCartaoLocal(cartao.leadId, etapaId);
    setEnviando(true);
    try {
      await api.post(`/crm/leads/${cartao.leadId}/stage`, { novaEtapaId: etapaId, rowVersion: cartao.rowVersion, ...extra });
      notificar("success", "Etapa atualizada.");
      setModalMobile(null);
      setPendenciaPerda(null);
      setPendenciaNaoFazemos(null);
      setPendenciaCotacao(null);
      carregar(undefined, true);
    } catch (e) {
      setBoard(boardAnterior);
      const mensagem = e instanceof ApiRequestError ? e.message : "Não foi possível mover o lead. Tente novamente.";
      notificar("error", mensagem);
    } finally {
      setEnviando(false);
    }
  }

  async function excluirLead() {
    if (!leadExcluindo) return;
    const leadId = leadExcluindo.leadId;
    setExcluindoLead(true);
    try {
      await api.del(`/crm/leads/${leadId}`);
      setBoard((atual) =>
        atual && { ...atual, colunas: atual.colunas.map((c) => ({ ...c, cartoes: c.cartoes.filter((x) => x.leadId !== leadId) })) }
      );
      notificar("success", "Lead excluído.");
      setLeadExcluindo(null);
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível excluir o lead.");
    } finally {
      setExcluindoLead(false);
    }
  }

  /** Igual ao moverPara, mas primeiro checa se o destino é "Venda concluída", "Perdido" ou "Não
   * fazemos" — nesses casos abre um formulário (venda concluída / motivo da perda / veículo não
   * atendido) em vez de mover direto. */
  function iniciarMudanca(cartao: LeadKanbanCard, etapaId: string | null) {
    const etapa = colunasExibidas?.find((c) => c.etapa.id === etapaId)?.etapa;

    // "Em atendimento" e "Venda concluída" têm duas colunas cada, com o sufixo "(Leads)"/"(Indicação)"
    // — em ambos os pares, a primeira só aceita "Lead", a segunda só "Indicação" (ver
    // colunasEmAtendimento/colunasVendaConcluida acima).
    const grupoColunasDuplicadas = etapa?.nome?.startsWith(ETAPA_EM_ATENDIMENTO)
      ? colunasEmAtendimento
      : etapa?.nome?.startsWith(ETAPA_VENDA_CONCLUIDA)
        ? colunasVendaConcluida
        : null;
    if (etapaId && grupoColunasDuplicadas) {
      const indiceColuna = grupoColunasDuplicadas.findIndex((c) => c.etapa.id === etapaId);
      const classificacao = classificarCartao(cartao);
      if (indiceColuna === 0 && classificacao === "indicacao") {
        notificar("error", `Esta coluna "${etapa!.nome}" aceita só cartões com a etiqueta "Lead".`);
        return;
      }
      if (indiceColuna === 1 && classificacao === "lead") {
        notificar("error", `Esta coluna "${etapa!.nome}" aceita só cartões com a etiqueta "Indicação".`);
        return;
      }
    }

    if (etapaId && etapa?.nome?.startsWith(ETAPA_VENDA_CONCLUIDA)) {
      if (!etapaGanhoPipelineId) {
        notificar("error", "Não foi possível carregar as etapas do pipeline. Recarregue a página e tente novamente.");
        return;
      }
      setModalMobile(null);
      setPendenciaVenda({ cartao, etapaId });
    } else if (etapaId && etapa?.nome === ETAPA_PERDIDO) {
      setModalMobile(null);
      setPendenciaPerda({ cartao, etapaId });
    } else if (etapaId && etapa?.nome === ETAPA_NAO_FAZEMOS) {
      setModalMobile(null);
      setPendenciaNaoFazemos({ cartao, etapaId });
    } else if (etapaId && etapa?.nome === ETAPA_COTACAO) {
      setModalMobile(null);
      setPendenciaCotacao({ cartao, etapaId });
    } else {
      moverPara(cartao, etapaId);
    }
  }

  function handleDrop(etapaId: string | null) {
    if (!cartaoArrastando) return;
    iniciarMudanca(cartaoArrastando, etapaId);
    setCartaoArrastando(null);
  }

  function fecharModalNovo() {
    setModalNovo(false);
    setDuplicidade(null);
  }

  async function criarLead(valores: LeadFormValues, ignorarDuplicidade = false) {
    setSalvandoNovo(true);
    setDuplicidade(null);
    try {
      // Lead cadastrado à mão pelo consultor vai direto pra coluna "Em atendimento" que só aceita
      // a etiqueta "Indicação" (a segunda das duas colunas de mesmo nome — ver colunasEmAtendimento).
      const etapaId = colunasEmAtendimento[1]?.etapa.id ?? undefined;
      await api.post("/crm/leads", { ...paraLeadCreateRequest(valores), etapaId, ignorarDuplicidade } satisfies LeadCreateRequest);
      notificar("success", "Cliente cadastrado com sucesso.");
      fecharModalNovo();
      carregar();
    } catch (e) {
      if (e instanceof ApiRequestError && e.codigo === "duplicidade") {
        setDuplicidade(e.detalhes as LeadDuplicateWarning);
      } else {
        notificar("error", e instanceof Error ? e.message : "Não foi possível cadastrar o cliente.");
      }
    } finally {
      setSalvandoNovo(false);
    }
  }

  const filtrosAtivos = !!(
    busca ||
    responsavelId.length ||
    regional.length ||
    origem.length ||
    incluirArquivados ||
    categoria.length ||
    fonte.length ||
    tipoIndicacao.length ||
    dataChegadaInicio ||
    dataChegadaFim ||
    dataVendaInicio ||
    dataVendaFim
  );

  const colunasExibidas = board?.colunas;

  // Mesma regra pras duas colunas "Em atendimento (Leads)"/"Em atendimento (Indicação)".
  const colunasEmAtendimento = useMemo(
    () => colunasExibidas?.filter((c) => c.etapa.nome.startsWith(ETAPA_EM_ATENDIMENTO)) ?? [],
    [colunasExibidas]
  );
  // Mesma regra, agora pras duas colunas "Venda concluída (Leads)"/"Venda concluída (Indicação)".
  const colunasVendaConcluida = useMemo(
    () => colunasExibidas?.filter((c) => c.etapa.nome.startsWith(ETAPA_VENDA_CONCLUIDA)) ?? [],
    [colunasExibidas]
  );

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Quadro de leads</h1>
          <p className="text-sm text-[var(--fg-muted)]">Arraste os cartões entre as etapas, ou toque no ícone <ArrowRightLeft className="inline size-3.5 align-text-bottom" /> do cartão pra mover.</p>
        </div>
        <div className="flex gap-2">
          <Button onClick={() => setModalNovo(true)}>
            <Plus className="size-4" /> Novo cliente
          </Button>
          <Link to="/app/crm/leads">
            <Button variant="secondary" type="button">
              <List className="size-4" /> Lista
            </Button>
          </Link>
        </div>
      </div>

      <div className="flex flex-wrap items-end gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
        <div className="w-56">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Buscar</label>
          <Input placeholder="Nome, telefone ou e-mail" value={busca} onChange={(e) => setBusca(e.target.value)} />
        </div>
        {podeVerOrigem && (
          <div className="w-44">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Origem</label>
            <MultiSelect
              ariaLabel="Origem"
              rotuloTodos="Todas"
              opcoes={origens.map((o) => ({ valor: o, rotulo: o }))}
              valores={origem}
              onChange={setOrigem}
            />
          </div>
        )}
        <div className="w-44">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Categoria</label>
          <MultiSelect
            ariaLabel="Categoria"
            rotuloTodos="Todas"
            opcoes={["Migração", "Indicação", "Lead"].map((c) => ({ valor: c, rotulo: c }))}
            valores={categoria}
            onChange={setCategoria}
          />
        </div>
        <div className="w-48">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Tipo de indicação</label>
          <MultiSelect
            ariaLabel="Tipo de indicação"
            opcoes={OPCOES_FILTRO_TIPO_INDICACAO.map((t) => ({ valor: t, rotulo: t }))}
            valores={tipoIndicacao}
            onChange={setTipoIndicacao}
          />
        </div>
        <div className="w-44">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Fonte</label>
          <MultiSelect
            ariaLabel="Fonte"
            rotuloTodos="Todas"
            opcoes={[
              { valor: "TrafegoPago", rotulo: "Tráfego pago" },
              { valor: "Notion", rotulo: "Notion" },
            ]}
            valores={fonte}
            onChange={setFonte}
          />
        </div>
        <div className="flex items-end gap-1">
          <div className="w-36">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Chegada de</label>
            <Input type="date" value={dataChegadaInicio} onChange={(e) => setDataChegadaInicio(e.target.value)} />
          </div>
          <div className="w-36">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">até</label>
            <Input type="date" value={dataChegadaFim} onChange={(e) => setDataChegadaFim(e.target.value)} />
          </div>
        </div>
        <div className="flex items-end gap-1">
          <div className="w-36">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Venda de</label>
            <Input type="date" value={dataVendaInicio} onChange={(e) => setDataVendaInicio(e.target.value)} />
          </div>
          <div className="w-36">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">até</label>
            <Input type="date" value={dataVendaFim} onChange={(e) => setDataVendaFim(e.target.value)} />
          </div>
        </div>
        {podeGerir && (
          <>
            <div className="w-48">
              <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Vendedor</label>
              <MultiSelect
                ariaLabel="Vendedor"
                opcoes={vendedores.map((v) => ({ valor: v.id, rotulo: v.nome }))}
                valores={responsavelId}
                onChange={setResponsavelId}
              />
            </div>
            <div className="w-44">
              <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Regional</label>
              <MultiSelect
                ariaLabel="Regional"
                rotuloTodos="Todas"
                opcoes={regionais.map((r) => ({ valor: r.nome, rotulo: r.nome }))}
                valores={regional}
                onChange={setRegional}
              />
            </div>
            <div className="w-40">
              <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Status</label>
              <Select value={incluirArquivados ? "todos" : "ativos"} onChange={(e) => setIncluirArquivados(e.target.value === "todos")}>
                <option value="ativos">Ativos</option>
                <option value="todos">Todos (inclui arquivados)</option>
              </Select>
            </div>
          </>
        )}
        {filtrosAtivos && (
          <Button variant="ghost" size="sm" onClick={limparFiltros}>
            <X className="size-4" /> Limpar filtros
          </Button>
        )}
        <div className="flex w-full flex-wrap items-end gap-2 border-t border-[var(--border)] pt-3">
          <div className="w-56">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Filtros salvos</label>
            <Select value={filtroSalvoAtual} onChange={(e) => escolherFiltroSalvo(e.target.value)} disabled={filtrosSalvos.length === 0}>
              <option value="">{filtrosSalvos.length === 0 ? "Nenhum filtro salvo" : "Escolha um filtro..."}</option>
              {filtrosSalvos.map((f) => (
                <option key={f.nome} value={f.nome}>
                  {f.nome}
                </option>
              ))}
            </Select>
          </div>
          {filtroSalvoAtual && (
            <Button variant="ghost" size="sm" onClick={excluirFiltroSalvo}>
              <Trash2 className="size-4" /> Excluir filtro
            </Button>
          )}
          {salvandoFiltro ? (
            <form
              className="flex items-end gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                salvarFiltro();
              }}
            >
              <div className="w-56">
                <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Nome do filtro</label>
                <Input autoFocus value={nomeFiltro} onChange={(e) => setNomeFiltro(e.target.value)} placeholder="Ex.: Tráfego pago da semana" />
              </div>
              <Button size="sm" type="submit" disabled={!nomeFiltro.trim()}>
                Salvar
              </Button>
              <Button variant="ghost" size="sm" type="button" onClick={() => setSalvandoFiltro(false)}>
                Cancelar
              </Button>
            </form>
          ) : (
            <Button
              variant="secondary"
              size="sm"
              disabled={!filtrosAtivos}
              title={filtrosAtivos ? undefined : "Escolha algum filtro para salvar"}
              onClick={() => {
                setNomeFiltro(filtroSalvoAtual);
                setSalvandoFiltro(true);
              }}
            >
              <Save className="size-4" /> Salvar filtro atual
            </Button>
          )}
        </div>
      </div>

      {carregando ? (
        <div className="flex gap-4 overflow-x-auto">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-96 w-80 shrink-0 rounded-2xl" />
          ))}
        </div>
      ) : erro || !board || !colunasExibidas ? (
        <ErrorState message={erro ?? "Não foi possível carregar o quadro de leads."} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : colunasExibidas.every((c) => c.total === 0) ? (
        <EmptyState
          title="Nenhum lead no quadro"
          description={filtrosAtivos ? "Ajuste os filtros ou cadastre um novo lead." : "Cadastre um novo lead para começar."}
        />
      ) : (
        <div className="flex gap-4 overflow-x-auto pb-3">
          {colunasExibidas.map((coluna) => {
            const chaveColuna = coluna.etapa.id ?? "sem-etapa";
            const restantes = Math.max(0, coluna.total - coluna.cartoes.length);
            const cor = coluna.etapa.cor ?? "#64748b";
            const alvo = !!cartaoArrastando && colunaSobre === chaveColuna;
            return (
              <section
                key={chaveColuna}
                aria-label={coluna.etapa.nome}
                onDragOver={(e) => {
                  e.preventDefault();
                  if (colunaSobre !== chaveColuna) setColunaSobre(chaveColuna);
                }}
                onDragLeave={(e) => {
                  if (!e.currentTarget.contains(e.relatedTarget as Node)) setColunaSobre(null);
                }}
                onDrop={() => {
                  setColunaSobre(null);
                  handleDrop(coluna.etapa.id);
                }}
                className={`flex w-80 shrink-0 flex-col overflow-hidden rounded-2xl border bg-[var(--surface-hover)]/60 transition-colors ${
                  alvo ? "border-[var(--brand)] bg-[var(--brand-soft)]/60" : "border-[var(--border)]"
                }`}
              >
                <header className="bg-[var(--surface)]">
                  <div className="h-1" style={{ backgroundColor: cor }} />
                  <div className="flex items-center justify-between gap-2 border-b border-[var(--border)] px-3 py-2.5">
                    <div className="flex min-w-0 items-center gap-2">
                      <span className="size-2.5 shrink-0 rounded-full" style={{ backgroundColor: cor }} />
                      <h2 className="truncate text-sm font-semibold text-[var(--fg)]" title={coluna.etapa.nome}>
                        {coluna.etapa.nome}
                      </h2>
                    </div>
                    <span
                      className="shrink-0 rounded-full px-2 py-0.5 text-xs font-semibold"
                      style={{ backgroundColor: `${cor}1f`, color: cor }}
                      title="Leads nesta coluna"
                    >
                      {coluna.total.toLocaleString("pt-BR")}
                    </span>
                  </div>
                </header>

                <div className="max-h-[calc(100vh-22rem)] min-h-24 space-y-2.5 overflow-y-auto p-2.5">
                  {coluna.cartoes.length === 0 && (
                    <p className="rounded-xl border border-dashed border-[var(--border)] px-3 py-6 text-center text-xs text-[var(--fg-muted)]">
                      Nenhum lead aqui
                    </p>
                  )}
                  {coluna.cartoes.map((cartao) => (
                    <CartaoLead
                      key={cartao.leadId}
                      cartao={cartao}
                      corColuna={cor}
                      podeGerir={podeGerir}
                      podeExcluir={podeExcluir}
                      podeVerOrigem={podeVerOrigem}
                      onAbrir={() => navigate(`/app/crm/leads/${cartao.leadId}`)}
                      onMover={() => setModalMobile(cartao)}
                      onTrocarResponsavel={() => setTrocandoResponsavel(cartao)}
                      onExcluir={() => setLeadExcluindo(cartao)}
                      onDragStart={() => setCartaoArrastando(cartao)}
                      onDragEnd={() => {
                        setCartaoArrastando(null);
                        setColunaSobre(null);
                      }}
                    />
                  ))}
                  {restantes > 0 && (
                    <Button
                      variant="secondary"
                      size="sm"
                      className="w-full"
                      loading={carregandoMais === chaveColuna}
                      onClick={() => verMaisCartoes(coluna)}
                    >
                      Ver mais ({restantes.toLocaleString("pt-BR")})
                    </Button>
                  )}
                </div>
              </section>
            );
          })}
        </div>
      )}

      <Modal open={modalNovo} onClose={fecharModalNovo} title="Novo cliente" size="lg">
        {duplicidade && (
          <div className="mb-4 flex items-center justify-between gap-3 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-sm text-[var(--warning)]">
            <span>
              Já existe um lead com o mesmo {duplicidade.campoDuplicado}: <strong>{duplicidade.nomeExistente}</strong>.
            </span>
            <Link to={`/app/crm/leads/${duplicidade.leadExistenteId}`} className="underline shrink-0">
              Abrir cadastro
            </Link>
          </div>
        )}
        <LeadForm valoresIniciais={leadFormVazio} salvando={salvandoNovo} onSubmit={(v) => criarLead(v)} onCancel={fecharModalNovo} idPrefix="kanban-novo" />
      </Modal>

      {/* Ação de mover em telas sem drag-and-drop (toque no celular) */}
      <Modal open={!!modalMobile} onClose={() => setModalMobile(null)} title="Mover lead" size="sm">
        {modalMobile && colunasExibidas && (
          <div className="space-y-1">
            <p className="mb-3 text-sm text-[var(--fg-muted)]">{modalMobile.nomeOuRazaoSocial}</p>
            {colunasExibidas
              .filter((c) => c.etapa.id !== colunasExibidas.find((col) => col.cartoes.includes(modalMobile))?.etapa.id)
              .map((c) => (
                <button
                  key={c.etapa.id ?? "sem-etapa"}
                  disabled={enviando}
                  onClick={() => iniciarMudanca(modalMobile, c.etapa.id)}
                  className="focus-ring flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)] disabled:opacity-50"
                >
                  <ArrowRightLeft className="size-4 text-[var(--fg-muted)]" />
                  {c.etapa.nome}
                </button>
              ))}
          </div>
        )}
      </Modal>

      <VendaConcluidaDialog
        open={!!pendenciaVenda}
        leadId={pendenciaVenda?.cartao.leadId ?? null}
        opportunityId={null}
        pipelineGanhoEtapaId={etapaGanhoPipelineId ?? undefined}
        etapaNome={colunasExibidas?.find((c) => c.etapa.id === pendenciaVenda?.etapaId)?.etapa.nome ?? ETAPA_VENDA_CONCLUIDA}
        valorEstimado={0}
        onCancel={() => setPendenciaVenda(null)}
        onConcluido={() => {
          if (pendenciaVenda) moverPara(pendenciaVenda.cartao, pendenciaVenda.etapaId);
          setPendenciaVenda(null);
        }}
      />

      <AlterarResponsavelDialog
        open={!!trocandoResponsavel}
        leadId={trocandoResponsavel?.leadId ?? null}
        leadNome={trocandoResponsavel?.nomeOuRazaoSocial}
        responsavelAtualId={trocandoResponsavel?.responsavelId}
        onCancel={() => setTrocandoResponsavel(null)}
        onConcluido={() => {
          setTrocandoResponsavel(null);
          carregar(undefined, true);
        }}
      />

      <ConfirmDialog
        open={!!leadExcluindo}
        title="Excluir lead"
        danger
        confirmLabel="Excluir"
        loading={excluindoLead}
        message={
          <>
            Tem certeza que deseja excluir <strong className="text-[var(--fg)]">{leadExcluindo?.nomeOuRazaoSocial}</strong>? O lead sai
            do quadro de leads, as oportunidades dele saem do Pipeline e ele não pode ser recuperado por aqui.
          </>
        }
        onConfirm={excluirLead}
        onCancel={() => setLeadExcluindo(null)}
      />

      <StageChangeDialog
        open={!!pendenciaPerda}
        tipo={pendenciaPerda ? "perdido" : null}
        etapaNome={ETAPA_PERDIDO}
        enviando={enviando}
        onCancel={() => setPendenciaPerda(null)}
        onConfirm={(dados) => {
          if (pendenciaPerda) moverPara(pendenciaPerda.cartao, pendenciaPerda.etapaId, dados);
        }}
      />

      <AdesaoCotacaoDialog
        open={!!pendenciaCotacao}
        etapaNome={ETAPA_COTACAO}
        valorAtual={pendenciaCotacao?.cartao.valorAdesao}
        enviando={enviando}
        onCancel={() => setPendenciaCotacao(null)}
        onConfirm={(dados) => {
          if (pendenciaCotacao) moverPara(pendenciaCotacao.cartao, pendenciaCotacao.etapaId, dados);
        }}
      />

      <VeiculoNaoFazemosDialog
        open={!!pendenciaNaoFazemos}
        etapaNome={ETAPA_NAO_FAZEMOS}
        enviando={enviando}
        onCancel={() => setPendenciaNaoFazemos(null)}
        onConfirm={(dados) => {
          if (pendenciaNaoFazemos) moverPara(pendenciaNaoFazemos.cartao, pendenciaNaoFazemos.etapaId, dados);
        }}
      />

    </div>
  );
}
