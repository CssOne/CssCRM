import { AlertTriangle, ArrowRightLeft, Clock, Trash2 } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiRequestError, isAbortError, toQueryString } from "../../lib/api";
import { formatarMoeda, diasRelativos } from "../../lib/format";
import { TipoEtapaPipeline, type ChangeStageRequest, type PipelineBoard, type PipelineCard } from "../../lib/types";
import { Avatar, Badge, ConfirmDialog, EmptyState, ErrorState, Modal, Skeleton, useToast } from "../../components/ui";
import { useAuth } from "../../context/AuthContext";
import { StageChangeDialog } from "../../components/crm/StageChangeDialog";
import { VendaConcluidaDialog } from "../../components/crm/VendaConcluidaDialog";
import { useCrmEventos } from "../../lib/useCrmEventos";
import { BarraDeFiltros, CampoDeFiltro, type ChipDeFiltro } from "../../components/Filtros";
import { MultiSelect } from "../../components/MultiSelect";
import { PeriodoInput } from "../../components/PeriodoInput";

const SEM_PRODUTO = "Sem produto";

export function PipelinePage() {
  const { notificar } = useToast();
  const [board, setBoard] = useState<PipelineBoard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [cartaoArrastando, setCartaoArrastando] = useState<PipelineCard | null>(null);
  const [colunaAlvo, setColunaAlvo] = useState<string | null>(null);
  const [busca, setBusca] = useState("");
  const [soAtrasadas, setSoAtrasadas] = useState(false);
  const [consultorIds, setConsultorIds] = useState<string[]>([]);
  const [produtos, setProdutos] = useState<string[]>([]);
  const [origens, setOrigens] = useState<string[]>([]);
  const [criadaDe, setCriadaDe] = useState("");
  const [criadaAte, setCriadaAte] = useState("");
  const [modalMobile, setModalMobile] = useState<PipelineCard | null>(null);
  // Admin/gestores excluem qualquer card que enxergam; consultores, só os próprios (ver OpportunityService.ExcluirAsync).
  const { temPapel, sessao } = useAuth();
  const ehGestao = temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial");
  const podeExcluir = (cartao: PipelineCard) => ehGestao || (!!sessao && cartao.responsavelId === sessao.id);
  const [cartaoExcluindo, setCartaoExcluindo] = useState<PipelineCard | null>(null);
  const [excluindoCartao, setExcluindoCartao] = useState(false);

  async function excluirCartao() {
    if (!cartaoExcluindo) return;
    const opportunityId = cartaoExcluindo.opportunityId;
    setExcluindoCartao(true);
    try {
      await api.del(`/crm/opportunities/${opportunityId}`);
      setBoard((atual) =>
        atual && {
          ...atual,
          colunas: atual.colunas.map((c) => {
            const removido = c.cartoes.find((x) => x.opportunityId === opportunityId);
            return removido
              ? { ...c, cartoes: c.cartoes.filter((x) => x.opportunityId !== opportunityId), valorTotal: c.valorTotal - removido.valorEstimado }
              : c;
          }),
        }
      );
      notificar("success", "Oportunidade excluída.");
      setCartaoExcluindo(null);
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível excluir a oportunidade.");
    } finally {
      setExcluindoCartao(false);
    }
  }
  const [pendencia, setPendencia] = useState<{ cartao: PipelineCard; etapaId: string; tipo: "ganho" | "perdido"; etapaNome: string } | null>(null);
  const [enviando, setEnviando] = useState(false);

  const boardRef = useRef<PipelineBoard | null>(null);
  boardRef.current = board;

  const carregar = useCallback((signal?: AbortSignal, silencioso = false) => {
    if (!silencioso) setCarregando(true);
    setErro(null);
    api
      .get<PipelineBoard>(`/crm/pipeline${toQueryString({ dataInicio: criadaDe || undefined, dataFim: criadaAte || undefined })}`, signal)
      .then(setBoard)
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o pipeline."); })
      .finally(() => { if (!signal?.aborted) setCarregando(false); });
  }, [criadaDe, criadaAte]);

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  // Mudanças de outros usuários (ex.: card excluído pelo administrador) aparecem sem recarregar a página.
  useCrmEventos(() => carregar(undefined, true));

  function moverCartaoLocal(opportunityId: string, etapaDestinoId: string) {
    setBoard((atual) => {
      if (!atual) return atual;
      let cartao: PipelineCard | undefined;
      const colunas = atual.colunas.map((col) => {
        const encontrado = col.cartoes.find((c) => c.opportunityId === opportunityId);
        if (encontrado) cartao = encontrado;
        return { ...col, cartoes: col.cartoes.filter((c) => c.opportunityId !== opportunityId) };
      });
      if (!cartao) return atual;
      return {
        colunas: colunas.map((col) =>
          col.etapa.id === etapaDestinoId ? { ...col, cartoes: [cartao!, ...col.cartoes] } : col
        ),
      };
    });
  }

  async function executarMudancaEtapa(cartao: PipelineCard, etapaId: string, dados?: Partial<ChangeStageRequest>) {
    const boardAnterior = boardRef.current;
    moverCartaoLocal(cartao.opportunityId, etapaId);
    setEnviando(true);
    try {
      await api.post(`/crm/opportunities/${cartao.opportunityId}/change-stage`, {
        novaEtapaId: etapaId,
        rowVersion: cartao.rowVersion,
        ...dados,
      });
      notificar("success", "Etapa atualizada.");
      setPendencia(null);
      setModalMobile(null);
      carregar(undefined, true);
    } catch (e) {
      setBoard(boardAnterior);
      const mensagem = e instanceof ApiRequestError ? e.message : "Não foi possível mover o cartão. Tente novamente.";
      notificar("error", mensagem);
    } finally {
      setEnviando(false);
    }
  }

  function iniciarMudanca(cartao: PipelineCard, etapaId: string) {
    const etapa = board?.colunas.find((c) => c.etapa.id === etapaId)?.etapa;
    if (!etapa) return;
    if (etapa.tipo === TipoEtapaPipeline.Ganho) {
      setPendencia({ cartao, etapaId, tipo: "ganho", etapaNome: etapa.nome });
    } else if (etapa.tipo === TipoEtapaPipeline.Perdido) {
      setPendencia({ cartao, etapaId, tipo: "perdido", etapaNome: etapa.nome });
    } else {
      executarMudancaEtapa(cartao, etapaId);
    }
  }

  function handleDrop(etapaId: string) {
    if (!cartaoArrastando) return;
    iniciarMudanca(cartaoArrastando, etapaId);
    setCartaoArrastando(null);
  }

  if (carregando) {
    return (
      <div className="flex gap-4 overflow-x-auto">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-96 w-[clamp(15rem,calc((100vw-var(--menu-w)-7.9rem)/4),20rem)] shrink-0" />
        ))}
      </div>
    );
  }

  if (erro || !board) {
    return <ErrorState message={erro ?? "Não foi possível carregar o pipeline."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  const termo = busca.trim().toLowerCase();
  const passa = (c: PipelineCard) =>
    (!soAtrasadas || c.atrasada) &&
    (consultorIds.length === 0 || consultorIds.includes(c.responsavelId)) &&
    (produtos.length === 0 || produtos.includes(c.produtoOuServico ?? SEM_PRODUTO)) &&
    (origens.length === 0 || origens.includes(c.origem ?? "")) &&
    (!termo || `${c.leadNome} ${c.titulo} ${c.responsavelNome}`.toLowerCase().includes(termo));
  const todosCartoes = board.colunas.flatMap((c) => c.cartoes);
  const consultoresLista = [...new Map(todosCartoes.map((c) => [c.responsavelId, c.responsavelNome])).entries()]
    .map(([valor, rotulo]) => ({ valor, rotulo }))
    .sort((a, b) => a.rotulo.localeCompare(b.rotulo, "pt-BR"));
  const produtosLista = [...new Set(todosCartoes.map((c) => c.produtoOuServico ?? SEM_PRODUTO))].sort((a, b) => a.localeCompare(b, "pt-BR")).map((v) => ({ valor: v, rotulo: v }));
  const origensLista = [...new Set(todosCartoes.map((c) => c.origem).filter((o): o is string => !!o))].sort((a, b) => a.localeCompare(b, "pt-BR")).map((v) => ({ valor: v, rotulo: v }));
  const dataCurta = (d: string) => (d ? d.split("-").reverse().slice(0, 2).join("/") : "…");
  const chips: ChipDeFiltro[] = [];
  if (soAtrasadas) chips.push({ rotulo: "Situação", valor: "Atrasadas", limpar: () => setSoAtrasadas(false) });
  if (consultorIds.length) chips.push({ rotulo: "Consultor", valor: consultoresLista.filter((c) => consultorIds.includes(c.valor)).map((c) => c.rotulo).join(", ") || `${consultorIds.length} selecionado(s)`, limpar: () => setConsultorIds([]) });
  if (produtos.length) chips.push({ rotulo: "Produto", valor: produtos.join(", "), limpar: () => setProdutos([]) });
  if (origens.length) chips.push({ rotulo: "Origem", valor: origens.join(", "), limpar: () => setOrigens([]) });
  if (criadaDe || criadaAte) chips.push({ rotulo: "Criada em", valor: `${dataCurta(criadaDe)} → ${dataCurta(criadaAte)}`, limpar: () => { setCriadaDe(""); setCriadaAte(""); } });
  function limparFiltros() {
    setBusca("");
    setSoAtrasadas(false);
    setConsultorIds([]);
    setProdutos([]);
    setOrigens([]);
    setCriadaDe("");
    setCriadaAte("");
  }
  const emAberto = board.colunas.filter((c) => c.etapa.tipo === TipoEtapaPipeline.Aberta);
  const resumo = {
    abertas: emAberto.reduce((n, c) => n + c.cartoes.length, 0),
    valorAberto: emAberto.reduce((n, c) => n + c.valorTotal, 0),
    atrasadas: emAberto.reduce((n, c) => n + c.cartoes.filter((x) => x.atrasada).length, 0),
  };
  const filtrando = !!termo || soAtrasadas || consultorIds.length > 0 || produtos.length > 0 || origens.length > 0;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold tracking-tight text-[var(--fg)]">Pipeline de vendas</h1>
          <p className="text-sm text-[var(--fg-muted)]">Arraste os cartões entre as etapas (no celular, toque no cartão para mover).</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {[
            { rotulo: "Em aberto", valor: String(resumo.abertas), cor: "var(--fg)" },
            { rotulo: "Valor em aberto", valor: formatarMoeda(resumo.valorAberto), cor: "var(--brand)" },
            { rotulo: "Atrasadas", valor: String(resumo.atrasadas), cor: resumo.atrasadas > 0 ? "var(--danger)" : "var(--fg)" },
          ].map((m) => (
            <div key={m.rotulo} className="ui-surface rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-2 text-right shadow-[var(--shadow-card)]">
              <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{m.rotulo}</p>
              <p className="text-lg font-bold leading-tight tracking-tight [font-variant-numeric:tabular-nums]" style={{ color: m.cor }}>{m.valor}</p>
            </div>
          ))}
        </div>
      </div>

      <BarraDeFiltros busca={busca} onBusca={setBusca} placeholder="Cliente, proposta ou consultor" chips={chips} onLimparTudo={limparFiltros}>
        <CampoDeFiltro rotulo="Consultor">
          <MultiSelect ariaLabel="Consultor" rotuloTodos="Todos" opcoes={consultoresLista} valores={consultorIds} onChange={setConsultorIds} />
        </CampoDeFiltro>
        <CampoDeFiltro rotulo="Produto">
          <MultiSelect ariaLabel="Produto" rotuloTodos="Todos" opcoes={produtosLista} valores={produtos} onChange={setProdutos} />
        </CampoDeFiltro>
        {origensLista.length > 0 && (
          <CampoDeFiltro rotulo="Origem">
            <MultiSelect ariaLabel="Origem" rotuloTodos="Todas" opcoes={origensLista} valores={origens} onChange={setOrigens} />
          </CampoDeFiltro>
        )}
        <CampoDeFiltro rotulo="Criada em">
          <PeriodoInput ariaLabel="Criada em" de={criadaDe} ate={criadaAte} onChange={(x, y) => { setCriadaDe(x); setCriadaAte(y); }} />
        </CampoDeFiltro>
        <CampoDeFiltro rotulo="Situação">
          <button
            type="button"
            aria-pressed={soAtrasadas}
            onClick={() => setSoAtrasadas((v) => !v)}
            className={`focus-ring inline-flex h-9 w-full cursor-pointer items-center justify-center gap-1.5 rounded-lg border px-3 text-[13px] font-medium transition-colors ${
              soAtrasadas
                ? "border-[var(--danger)] bg-[color-mix(in_srgb,var(--danger)_12%,transparent)] text-[var(--danger)]"
                : "border-[var(--border)] bg-[var(--field)] text-[var(--fg-muted)] hover:text-[var(--fg)]"
            }`}
          >
            <AlertTriangle className="size-3.5" aria-hidden /> Só atrasadas
          </button>
        </CampoDeFiltro>
      </BarraDeFiltros>

      {board.colunas.every((c) => c.cartoes.length === 0) ? (
        <EmptyState title="Nenhuma oportunidade no pipeline" description="Crie uma oportunidade a partir de um lead para começar." />
      ) : (
        <div className="flex gap-4 overflow-x-auto pb-2">
          {board.colunas.map((coluna) => (
            <div
              key={coluna.etapa.id}
              onDragOver={(e) => {
                e.preventDefault();
                if (colunaAlvo !== coluna.etapa.id) setColunaAlvo(coluna.etapa.id);
              }}
              onDragLeave={(e) => {
                if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setColunaAlvo(null);
              }}
              onDrop={() => {
                setColunaAlvo(null);
                handleDrop(coluna.etapa.id);
              }}
              style={{ "--cor": coluna.etapa.cor ?? "#64748b" } as React.CSSProperties}
              className={`ui-surface flex w-[clamp(15rem,calc((100vw-var(--menu-w)-7.9rem)/4),20rem)] shrink-0 flex-col rounded-3xl border bg-[var(--surface-hover)] transition-[border-color,box-shadow] duration-200 ${
                colunaAlvo === coluna.etapa.id && cartaoArrastando
                  ? "border-[var(--cor)] shadow-[0_0_0_3px_color-mix(in_srgb,var(--cor)_22%,transparent)]"
                  : "border-[var(--border)]"
              }`}
            >
              <div className="sticky top-0 flex items-center justify-between px-4 pb-1 pt-3.5">
                <div className="flex items-center gap-2">
                  <span className="size-2 shrink-0 rounded-full" style={{ backgroundColor: coluna.etapa.cor ?? "#64748b" }} />
                  <span className="text-[13px] font-semibold tracking-tight text-[var(--fg)]">{coluna.etapa.nome}</span>
                  <span className="rounded-full bg-[color-mix(in_srgb,var(--cor)_16%,transparent)] px-1.5 text-[11px] font-semibold text-[color-mix(in_srgb,var(--cor)_70%,var(--fg))] [font-variant-numeric:tabular-nums]">
                    {filtrando ? `${coluna.cartoes.filter(passa).length}/${coluna.cartoes.length}` : coluna.cartoes.length}
                  </span>
                </div>
              </div>
              <div className="px-4 pb-1 text-sm font-semibold text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarMoeda(coluna.valorTotal)}</div>

              <div className="max-h-[calc(100vh-22rem)] min-h-24 flex-1 space-y-2.5 overflow-y-auto p-2.5">
                {coluna.cartoes.filter(passa).length === 0 && (
                  <p className="rounded-2xl border border-dashed border-[var(--border)] px-3 py-6 text-center text-xs text-[var(--fg-muted)]">
                    {filtrando ? "Nenhum cartão com esse filtro" : cartaoArrastando ? "Solte aqui" : "Sem oportunidades"}
                  </p>
                )}
                {coluna.cartoes.filter(passa).map((cartao) => (
                  <div
                    key={cartao.opportunityId}
                    draggable
                    onDragStart={() => setCartaoArrastando(cartao)}
                    onDragEnd={() => {
                      setCartaoArrastando(null);
                      setColunaAlvo(null);
                    }}
                    onClick={() => setModalMobile(cartao)}
                    className={`focus-ring cursor-grab rounded-xl border border-l-[3px] border-[var(--border)] border-l-[var(--cor)] bg-[var(--surface)] p-3.5 text-sm shadow-[var(--shadow-card)] ${cartaoArrastando?.opportunityId === cartao.opportunityId ? "opacity-40" : ""} transition-[box-shadow,transform] hover:-translate-y-px hover:shadow-[0_8px_24px_-10px_rgb(0_0_0/0.22)] active:cursor-grabbing`}
                  >
                    <div className="mb-1 flex items-start justify-between gap-2">
                      <Link
                        to={`/app/crm/leads/${cartao.leadId}`}
                        onClick={(e) => e.stopPropagation()}
                        className="truncate font-medium text-[var(--fg)] hover:text-[var(--brand)]"
                      >
                        {cartao.leadNome}
                      </Link>
                      <div className="flex shrink-0 items-center gap-1">
                        {cartao.atrasada && <AlertTriangle className="size-4 text-[var(--danger)]" aria-hidden />}
                        {podeExcluir(cartao) && (
                          <button
                            type="button"
                            title="Excluir oportunidade"
                            onClick={(e) => {
                              e.stopPropagation();
                              setCartaoExcluindo(cartao);
                            }}
                            className="focus-ring rounded p-0.5 text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--danger)]"
                          >
                            <Trash2 className="size-3.5" />
                          </button>
                        )}
                      </div>
                    </div>
                    <p className="truncate text-xs text-[var(--fg-muted)]">{cartao.titulo}</p>
                    <div className="mt-2 flex items-center justify-between">
                      <Badge variant="brand">{formatarMoeda(cartao.valorEstimado)}</Badge>
                      <span className="flex min-w-0 items-center gap-1.5 text-xs text-[var(--fg-muted)]" title={cartao.responsavelNome}>
                        <Avatar nome={cartao.responsavelNome} usuarioId={cartao.responsavelId} className="size-4 shrink-0 text-[8px]" />
                        <span className="truncate">{cartao.responsavelNome}</span>
                      </span>
                    </div>
                    <div className={`mt-2 flex items-center gap-1 text-xs ${cartao.atrasada ? "font-medium text-[var(--danger)]" : "text-[var(--fg-muted)]"}`}>
                      <Clock className="size-3" />
                      {diasRelativos(cartao.etapaDesde)}
                      {cartao.proximaAtividadeEm && ` · próximo contato ${diasRelativos(cartao.proximaAtividadeEm)}`}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Ação de mover em telas sem drag-and-drop (toque no celular) */}
      <Modal open={!!modalMobile} onClose={() => setModalMobile(null)} title="Mover oportunidade" size="sm">
        {modalMobile && (
          <div className="space-y-1">
            <p className="mb-3 text-sm text-[var(--fg-muted)]">{modalMobile.titulo}</p>
            {board.colunas
              .filter((c) => c.etapa.id !== board.colunas.find((col) => col.cartoes.includes(modalMobile))?.etapa.id)
              .map((c) => (
                <button
                  key={c.etapa.id}
                  onClick={() => iniciarMudanca(modalMobile, c.etapa.id)}
                  className="focus-ring flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)]"
                >
                  <ArrowRightLeft className="size-4 text-[var(--fg-muted)]" />
                  {c.etapa.nome}
                </button>
              ))}
          </div>
        )}
      </Modal>

      <ConfirmDialog
        open={!!cartaoExcluindo}
        title="Excluir oportunidade"
        danger
        confirmLabel="Excluir"
        loading={excluindoCartao}
        message={
          <>
            Tem certeza que deseja excluir a oportunidade <strong className="text-[var(--fg)]">{cartaoExcluindo?.titulo}</strong> de{" "}
            <strong className="text-[var(--fg)]">{cartaoExcluindo?.leadNome}</strong>? O card sai do Pipeline; o lead continua no
            quadro de leads.
          </>
        }
        onConfirm={excluirCartao}
        onCancel={() => setCartaoExcluindo(null)}
      />

      <StageChangeDialog
        open={pendencia?.tipo === "perdido"}
        tipo={pendencia?.tipo === "perdido" ? "perdido" : null}
        etapaNome={pendencia?.etapaNome ?? ""}
        enviando={enviando}
        onCancel={() => setPendencia(null)}
        onConfirm={(dados) => pendencia && executarMudancaEtapa(pendencia.cartao, pendencia.etapaId, dados)}
      />

      <VendaConcluidaDialog
        open={pendencia?.tipo === "ganho"}
        opportunityId={pendencia?.cartao.opportunityId ?? null}
        leadId={pendencia?.cartao.leadId ?? null}
        etapaNome={pendencia?.etapaNome ?? ""}
        valorEstimado={pendencia?.cartao.valorEstimado ?? 0}
        rowVersionInicial={pendencia?.cartao.rowVersion ?? 0}
        enviando={enviando}
        onCancel={() => setPendencia(null)}
        onConfirm={(dados) => pendencia && executarMudancaEtapa(pendencia.cartao, pendencia.etapaId, dados)}
      />
    </div>
  );
}
