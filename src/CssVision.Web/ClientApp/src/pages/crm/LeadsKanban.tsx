import { ArrowRightLeft, List, Plus, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiRequestError, isAbortError, toQueryString } from "../../lib/api";
import { diasRelativos, formatarTelefone } from "../../lib/format";
import {
  TipoEtapaPipeline,
  type LeadCreateRequest,
  type LeadDuplicateWarning,
  type LeadKanbanBoard,
  type LeadKanbanCard,
  type PipelineBoard,
  type Regional,
  type VendedorResumo,
} from "../../lib/types";
import { Badge, Button, EmptyState, ErrorState, Input, Modal, Select, Skeleton, Tabs, useToast } from "../../components/ui";
import { LeadForm, leadFormVazio, paraLeadCreateRequest, type LeadFormValues } from "../../components/crm/LeadForm";
import { VendaConcluidaDialog } from "../../components/crm/VendaConcluidaDialog";
import { StageChangeDialog } from "../../components/crm/StageChangeDialog";
import { VeiculoNaoFazemosDialog } from "../../components/crm/VeiculoNaoFazemosDialog";
import { useAuth } from "../../context/AuthContext";

/** Nome da etapa "terminal com sucesso" do quadro de leads — ver CrmSeeder.cs. Ao contrário do
 * Pipeline (TipoEtapaPipeline.Ganho), CrmLeadStage não tem um enum de tipo, só o nome mesmo. */
const ETAPA_VENDA_CONCLUIDA = "Venda concluída";
/** Nome da etapa terminal "perdida" do quadro de leads — mesma lógica de ETAPA_VENDA_CONCLUIDA acima. */
const ETAPA_PERDIDO = "Perdido";
/** Nome da etapa "veículo fora do que a CSS Brasil atende" — mesma lógica de ETAPA_VENDA_CONCLUIDA acima. */
const ETAPA_NAO_FAZEMOS = "Não fazemos";

export function LeadsKanbanPage() {
  const { notificar } = useToast();
  const { temPapel } = useAuth();
  const podeGerir = temPapel("Admin", "GestorMaster", "GestorComercial");

  const [board, setBoard] = useState<LeadKanbanBoard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [criadoManualmente, setCriadoManualmente] = useState(true);
  const [busca, setBusca] = useState("");
  const [responsavelId, setResponsavelId] = useState("");
  const [regional, setRegional] = useState("");
  const [origem, setOrigem] = useState("");
  const [incluirArquivados, setIncluirArquivados] = useState(false);
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);
  const [regionais, setRegionais] = useState<Regional[]>([]);
  const [origens, setOrigens] = useState<string[]>([]);

  const [cartaoArrastando, setCartaoArrastando] = useState<LeadKanbanCard | null>(null);
  const [modalMobile, setModalMobile] = useState<LeadKanbanCard | null>(null);
  const [enviando, setEnviando] = useState(false);

  const [modalNovo, setModalNovo] = useState(false);
  const [salvandoNovo, setSalvandoNovo] = useState(false);
  const [duplicidade, setDuplicidade] = useState<LeadDuplicateWarning | null>(null);

  const [pendenciaVenda, setPendenciaVenda] = useState<{ cartao: LeadKanbanCard; etapaId: string } | null>(null);
  const [pendenciaPerda, setPendenciaPerda] = useState<{ cartao: LeadKanbanCard; etapaId: string } | null>(null);
  const [pendenciaNaoFazemos, setPendenciaNaoFazemos] = useState<{ cartao: LeadKanbanCard; etapaId: string } | null>(null);
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
      criadoManualmente,
      busca: busca || undefined,
      responsavelId: responsavelId || undefined,
      regional: regional || undefined,
      origem: origem || undefined,
      incluirArquivados: incluirArquivados || undefined,
    }),
    [criadoManualmente, busca, responsavelId, regional, origem, incluirArquivados]
  );

  const carregar = useCallback(
    (signal?: AbortSignal, silencioso = false) => {
      if (!silencioso) setCarregando(true);
      setErro(null);
      api
        .get<LeadKanbanBoard>(`/crm/leads/kanban${toQueryString(filtro)}`, signal)
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

  function limparFiltros() {
    setBusca("");
    setResponsavelId("");
    setRegional("");
    setOrigem("");
    setIncluirArquivados(false);
  }

  function moverCartaoLocal(leadId: string, etapaDestinoId: string | null) {
    setBoard((atual) => {
      if (!atual) return atual;
      let cartao: LeadKanbanCard | undefined;
      const colunas = atual.colunas.map((col) => {
        const encontrado = col.cartoes.find((c) => c.leadId === leadId);
        if (encontrado) cartao = encontrado;
        return { ...col, cartoes: col.cartoes.filter((c) => c.leadId !== leadId) };
      });
      if (!cartao) return atual;
      return {
        colunas: colunas.map((col) =>
          col.etapa.id === etapaDestinoId ? { ...col, cartoes: [cartao!, ...col.cartoes] } : col
        ),
      };
    });
  }

  async function moverPara(
    cartao: LeadKanbanCard,
    etapaId: string | null,
    extra?: { motivoPerdaId?: string; motivoPerdaObservacao?: string; veiculoNaoAtendido?: string }
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
      carregar(undefined, true);
    } catch (e) {
      setBoard(boardAnterior);
      const mensagem = e instanceof ApiRequestError ? e.message : "Não foi possível mover o lead. Tente novamente.";
      notificar("error", mensagem);
    } finally {
      setEnviando(false);
    }
  }

  /** Igual ao moverPara, mas primeiro checa se o destino é "Venda concluída", "Perdido" ou "Não
   * fazemos" — nesses casos abre um formulário (venda concluída / motivo da perda / veículo não
   * atendido) em vez de mover direto. */
  function iniciarMudanca(cartao: LeadKanbanCard, etapaId: string | null) {
    const etapa = colunasExibidas?.find((c) => c.etapa.id === etapaId)?.etapa;
    if (etapaId && etapa?.nome === ETAPA_VENDA_CONCLUIDA) {
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
      await api.post("/crm/leads", { ...paraLeadCreateRequest(valores), ignorarDuplicidade } satisfies LeadCreateRequest);
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

  const filtrosAtivos = !!(busca || responsavelId || regional || origem || incluirArquivados);

  // Na aba "Indicações" (cadastro manual) a coluna virtual "Sem etapa" não existe: todo lead
  // manual já entra direto na primeira etapa do funil (ver LeadService.CriarAsync).
  const colunasExibidas = board && criadoManualmente ? board.colunas.filter((c) => c.etapa.id !== null) : board?.colunas;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Quadro de leads</h1>
          <p className="text-sm text-[var(--fg-muted)]">Arraste os cartões entre as etapas (no celular, toque no cartão para mover).</p>
        </div>
        <div className="flex gap-2">
          {criadoManualmente && (
            <Button onClick={() => setModalNovo(true)}>
              <Plus className="size-4" /> Novo cliente
            </Button>
          )}
          <Link to="/app/crm/leads">
            <Button variant="secondary" type="button">
              <List className="size-4" /> Lista
            </Button>
          </Link>
        </div>
      </div>

      <Tabs
        tabs={[
          { chave: "manual", rotulo: "Indicações" },
          { chave: "automatico", rotulo: "Leads" },
        ]}
        ativa={criadoManualmente ? "manual" : "automatico"}
        onChange={(chave) => setCriadoManualmente(chave === "manual")}
      />

      <div className="flex flex-wrap items-end gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
        <div className="w-56">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Buscar</label>
          <Input placeholder="Nome, telefone ou e-mail" value={busca} onChange={(e) => setBusca(e.target.value)} />
        </div>
        <div className="w-44">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Origem</label>
          <Select value={origem} onChange={(e) => setOrigem(e.target.value)}>
            <option value="">Todas</option>
            {origens.map((o) => (
              <option key={o} value={o}>
                {o}
              </option>
            ))}
          </Select>
        </div>
        {podeGerir && (
          <>
            <div className="w-48">
              <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Vendedor</label>
              <Select value={responsavelId} onChange={(e) => setResponsavelId(e.target.value)}>
                <option value="">Todos</option>
                {vendedores.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.nome}
                  </option>
                ))}
              </Select>
            </div>
            <div className="w-44">
              <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Regional</label>
              <Select value={regional} onChange={(e) => setRegional(e.target.value)}>
                <option value="">Todas</option>
                {regionais.map((r) => (
                  <option key={r.id} value={r.nome}>
                    {r.nome}
                  </option>
                ))}
              </Select>
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
      </div>

      {carregando ? (
        <div className="flex gap-4 overflow-x-auto">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-96 w-72 shrink-0" />
          ))}
        </div>
      ) : erro || !board || !colunasExibidas ? (
        <ErrorState message={erro ?? "Não foi possível carregar o quadro de leads."} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : colunasExibidas.every((c) => c.cartoes.length === 0) ? (
        <EmptyState
          title="Nenhum lead no quadro"
          description={filtrosAtivos ? "Ajuste os filtros ou cadastre um novo lead." : "Cadastre um novo lead para começar."}
        />
      ) : (
        <div className="flex gap-4 overflow-x-auto pb-2">
          {colunasExibidas.map((coluna) => (
            <div
              key={coluna.etapa.id ?? "sem-etapa"}
              onDragOver={(e) => e.preventDefault()}
              onDrop={() => handleDrop(coluna.etapa.id)}
              className="flex w-72 shrink-0 flex-col rounded-xl border border-[var(--border)] bg-[var(--surface-hover)]/40"
            >
              <div className="sticky top-0 flex items-center justify-between rounded-t-xl border-b border-[var(--border)] bg-[var(--surface)] px-3 py-2">
                <div className="flex items-center gap-2">
                  <span className="size-2 rounded-full" style={{ backgroundColor: coluna.etapa.cor ?? "#64748b" }} />
                  <span className="text-sm font-medium text-[var(--fg)]">{coluna.etapa.nome}</span>
                  <span className="text-xs text-[var(--fg-muted)]">({coluna.cartoes.length})</span>
                </div>
              </div>

              <div className="max-h-[60vh] space-y-2 overflow-y-auto p-2">
                {coluna.cartoes.map((cartao) => (
                  <div
                    key={cartao.leadId}
                    draggable
                    onDragStart={() => setCartaoArrastando(cartao)}
                    onDragEnd={() => setCartaoArrastando(null)}
                    onClick={() => setModalMobile(cartao)}
                    className="focus-ring cursor-grab rounded-lg border border-[var(--border)] bg-[var(--surface)] p-3 text-sm shadow-sm active:cursor-grabbing"
                  >
                    <div className="mb-1 flex items-start justify-between gap-2">
                      <Link
                        to={`/app/crm/leads/${cartao.leadId}`}
                        onClick={(e) => e.stopPropagation()}
                        className="truncate font-medium text-[var(--fg)] hover:text-[var(--brand)]"
                      >
                        {cartao.nomeOuRazaoSocial}
                      </Link>
                      {cartao.tipoIndicacao && (
                        <Badge variant={cartao.tipoIndicacao === "Lead" ? "info" : "neutral"}>{cartao.tipoIndicacao}</Badge>
                      )}
                      {cartao.semContato && <Badge variant="warning">sem contato</Badge>}
                      {cartao.arquivado && <Badge variant="neutral">arquivado</Badge>}
                    </div>
                    <p className="truncate text-xs text-[var(--fg-muted)]">{formatarTelefone(cartao.telefone) || "—"}</p>
                    {cartao.email && <p className="truncate text-xs text-[var(--fg-muted)]">{cartao.email}</p>}
                    {(cartao.estado || cartao.placa || cartao.utilidadeVeiculo) && (
                      <div className="mt-1 flex flex-wrap gap-x-2 gap-y-0.5 text-xs text-[var(--fg-muted)]">
                        {cartao.estado && <span>{cartao.estado}</span>}
                        {cartao.placa && <span>Placa {cartao.placa}</span>}
                        {cartao.utilidadeVeiculo && <span>{cartao.utilidadeVeiculo}</span>}
                      </div>
                    )}
                    {(cartao.temSeguro === true || cartao.temSeguro === false) && (
                      <div className="mt-1">
                        <Badge variant={cartao.temSeguro ? "warning" : "success"}>
                          {cartao.temSeguro ? "Tem seguro" : "Sem seguro"}
                        </Badge>
                      </div>
                    )}
                    <div className="mt-2 flex items-center justify-between">
                      <span className="text-xs text-[var(--fg-muted)]">{cartao.campanha ?? cartao.origem ?? "—"}</span>
                      <span className="text-xs text-[var(--fg-muted)]">{cartao.responsavelNome ?? "Sem responsável"}</span>
                    </div>
                    {cartao.tags.length > 0 && (
                      <div className="mt-2 flex flex-wrap gap-1">
                        {cartao.tags.map((tag) => (
                          <Badge key={tag} variant="brand">
                            {tag}
                          </Badge>
                        ))}
                      </div>
                    )}
                    <div className="mt-2 text-xs text-[var(--fg-muted)]">{diasRelativos(cartao.criadoEm)}</div>
                  </div>
                ))}
              </div>
            </div>
          ))}
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
        etapaNome={ETAPA_VENDA_CONCLUIDA}
        valorEstimado={0}
        onCancel={() => setPendenciaVenda(null)}
        onConcluido={() => {
          if (pendenciaVenda) moverPara(pendenciaVenda.cartao, pendenciaVenda.etapaId);
          setPendenciaVenda(null);
        }}
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
