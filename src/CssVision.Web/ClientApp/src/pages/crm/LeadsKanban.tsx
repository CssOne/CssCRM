import { ArrowRightLeft, List } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiRequestError, isAbortError, toQueryString } from "../../lib/api";
import { diasRelativos, formatarTelefone } from "../../lib/format";
import type { LeadKanbanBoard, LeadKanbanCard } from "../../lib/types";
import { Badge, Button, EmptyState, ErrorState, Modal, Skeleton, useToast } from "../../components/ui";

export function LeadsKanbanPage() {
  const { notificar } = useToast();
  const [board, setBoard] = useState<LeadKanbanBoard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [cartaoArrastando, setCartaoArrastando] = useState<LeadKanbanCard | null>(null);
  const [modalMobile, setModalMobile] = useState<LeadKanbanCard | null>(null);
  const [enviando, setEnviando] = useState(false);

  const boardRef = useRef<LeadKanbanBoard | null>(null);
  boardRef.current = board;

  const carregar = useCallback((signal?: AbortSignal) => {
    setCarregando(true);
    setErro(null);
    api
      .get<LeadKanbanBoard>(`/crm/leads/kanban${toQueryString({})}`, signal)
      .then(setBoard)
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o quadro de leads."); })
      .finally(() => setCarregando(false));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

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

  async function moverPara(cartao: LeadKanbanCard, etapaId: string | null) {
    const boardAnterior = boardRef.current;
    moverCartaoLocal(cartao.leadId, etapaId);
    setEnviando(true);
    try {
      await api.post(`/crm/leads/${cartao.leadId}/stage`, { novaEtapaId: etapaId, rowVersion: cartao.rowVersion });
      notificar("success", "Etapa atualizada.");
      setModalMobile(null);
      carregar();
    } catch (e) {
      setBoard(boardAnterior);
      const mensagem = e instanceof ApiRequestError ? e.message : "Não foi possível mover o lead. Tente novamente.";
      notificar("error", mensagem);
    } finally {
      setEnviando(false);
    }
  }

  function handleDrop(etapaId: string | null) {
    if (!cartaoArrastando) return;
    moverPara(cartaoArrastando, etapaId);
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
    return <ErrorState message={erro ?? "Não foi possível carregar o quadro de leads."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Quadro de leads</h1>
          <p className="text-sm text-[var(--fg-muted)]">Arraste os cartões entre as etapas (no celular, toque no cartão para mover).</p>
        </div>
        <Link to="/app/crm/leads">
          <Button variant="secondary" type="button">
            <List className="size-4" /> Lista
          </Button>
        </Link>
      </div>

      {board.colunas.every((c) => c.cartoes.length === 0) ? (
        <EmptyState title="Nenhum lead no quadro" description="Cadastre um novo lead para começar." />
      ) : (
        <div className="flex gap-4 overflow-x-auto pb-2">
          {board.colunas.map((coluna) => (
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

              <div className="flex-1 space-y-2 overflow-y-auto p-2">
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
                      {cartao.semContato && <Badge variant="warning">sem contato</Badge>}
                    </div>
                    <p className="truncate text-xs text-[var(--fg-muted)]">{formatarTelefone(cartao.telefone) || cartao.email || "—"}</p>
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

      {/* Ação de mover em telas sem drag-and-drop (toque no celular) */}
      <Modal open={!!modalMobile} onClose={() => setModalMobile(null)} title="Mover lead" size="sm">
        {modalMobile && (
          <div className="space-y-1">
            <p className="mb-3 text-sm text-[var(--fg-muted)]">{modalMobile.nomeOuRazaoSocial}</p>
            {board.colunas
              .filter((c) => c.etapa.id !== board.colunas.find((col) => col.cartoes.includes(modalMobile))?.etapa.id)
              .map((c) => (
                <button
                  key={c.etapa.id ?? "sem-etapa"}
                  disabled={enviando}
                  onClick={() => moverPara(modalMobile, c.etapa.id)}
                  className="focus-ring flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)] disabled:opacity-50"
                >
                  <ArrowRightLeft className="size-4 text-[var(--fg-muted)]" />
                  {c.etapa.nome}
                </button>
              ))}
          </div>
        )}
      </Modal>
    </div>
  );
}
