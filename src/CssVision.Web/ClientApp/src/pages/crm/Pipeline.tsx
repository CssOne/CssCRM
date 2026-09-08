import { AlertTriangle, ArrowRightLeft, Clock } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiRequestError, isAbortError, toQueryString } from "../../lib/api";
import { formatarMoeda, diasRelativos } from "../../lib/format";
import { TipoEtapaPipeline, type PipelineBoard, type PipelineCard } from "../../lib/types";
import { Badge, EmptyState, ErrorState, Modal, Skeleton, useToast } from "../../components/ui";
import { StageChangeDialog, type DadosFechamento } from "../../components/crm/StageChangeDialog";

export function PipelinePage() {
  const { notificar } = useToast();
  const [board, setBoard] = useState<PipelineBoard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [cartaoArrastando, setCartaoArrastando] = useState<PipelineCard | null>(null);
  const [modalMobile, setModalMobile] = useState<PipelineCard | null>(null);
  const [pendencia, setPendencia] = useState<{ cartao: PipelineCard; etapaId: string; tipo: "ganho" | "perdido"; etapaNome: string } | null>(null);
  const [enviando, setEnviando] = useState(false);

  const boardRef = useRef<PipelineBoard | null>(null);
  boardRef.current = board;

  const carregar = useCallback((signal?: AbortSignal) => {
    setCarregando(true);
    setErro(null);
    api
      .get<PipelineBoard>(`/crm/pipeline${toQueryString({})}`, signal)
      .then(setBoard)
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o pipeline."); })
      .finally(() => setCarregando(false));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

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

  async function executarMudancaEtapa(cartao: PipelineCard, etapaId: string, dados?: DadosFechamento) {
    const boardAnterior = boardRef.current;
    moverCartaoLocal(cartao.opportunityId, etapaId);
    setEnviando(true);
    try {
      await api.post(`/crm/opportunities/${cartao.opportunityId}/change-stage`, {
        novaEtapaId: etapaId,
        rowVersion: cartao.rowVersion,
        motivoPerdaId: dados?.motivoPerdaId ?? null,
        valorFinal: dados?.valorFinal ?? null,
        dataEfetivaFechamento: dados?.dataEfetivaFechamento ?? null,
      });
      notificar("success", "Etapa atualizada.");
      setPendencia(null);
      setModalMobile(null);
      carregar();
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
          <Skeleton key={i} className="h-96 w-72 shrink-0" />
        ))}
      </div>
    );
  }

  if (erro || !board) {
    return <ErrorState message={erro ?? "Não foi possível carregar o pipeline."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Pipeline de vendas</h1>
        <p className="text-sm text-[var(--fg-muted)]">Arraste os cartões entre as etapas (no celular, toque no cartão para mover).</p>
      </div>

      {board.colunas.every((c) => c.cartoes.length === 0) ? (
        <EmptyState title="Nenhuma oportunidade no pipeline" description="Crie uma oportunidade a partir de um lead para começar." />
      ) : (
        <div className="flex gap-4 overflow-x-auto pb-2">
          {board.colunas.map((coluna) => (
            <div
              key={coluna.etapa.id}
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
              <div className="px-2 py-1 text-xs text-[var(--fg-muted)]">{formatarMoeda(coluna.valorTotal)}</div>

              <div className="flex-1 space-y-2 overflow-y-auto p-2">
                {coluna.cartoes.map((cartao) => (
                  <div
                    key={cartao.opportunityId}
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
                        {cartao.leadNome}
                      </Link>
                      {cartao.atrasada && <AlertTriangle className="size-4 shrink-0 text-[var(--danger)]" aria-hidden />}
                    </div>
                    <p className="truncate text-xs text-[var(--fg-muted)]">{cartao.titulo}</p>
                    <div className="mt-2 flex items-center justify-between">
                      <Badge variant="brand">{formatarMoeda(cartao.valorEstimado)}</Badge>
                      <span className="text-xs text-[var(--fg-muted)]">{cartao.responsavelNome}</span>
                    </div>
                    <div className="mt-2 flex items-center gap-1 text-xs text-[var(--fg-muted)]">
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

      <StageChangeDialog
        open={!!pendencia}
        tipo={pendencia?.tipo ?? null}
        etapaNome={pendencia?.etapaNome ?? ""}
        valorEstimado={pendencia?.cartao.valorEstimado ?? 0}
        enviando={enviando}
        onCancel={() => setPendencia(null)}
        onConfirm={(dados) => pendencia && executarMudancaEtapa(pendencia.cartao, pendencia.etapaId, dados)}
      />
    </div>
  );
}
