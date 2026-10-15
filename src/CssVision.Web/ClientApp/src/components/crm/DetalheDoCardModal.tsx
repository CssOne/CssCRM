import { useEffect, useRef, useState } from "react";
import { api, isAbortError } from "../../lib/api";
import { formatarData, formatarMoeda } from "../../lib/format";
import type { RelatorioDetalhe } from "../../lib/types";
import { useAbrirLead } from "../../lib/painelLead";
import { useCrmEventos } from "../../lib/useCrmEventos";
import { ErrorState, Modal, Skeleton } from "../ui";

/** Uma aba do detalhe: o rótulo e o endereço da lista (com os mesmos filtros do card). */
export interface AbaDoDetalhe {
  rotulo: string;
  url: string;
}

/**
 * Os clientes por trás de um card: abre ao clicar no card (Relatório comercial) ou numa fatia do gráfico (Visão geral). Mostra a lista que gerou o número, com
 * a mesma consulta do card, e atualiza sozinha quando o quadro muda. Clicar numa linha abre o cliente.
 */
export function DetalheDoCardModal({ aberto, titulo, abas, onFechar }: { aberto: boolean; titulo: string; abas: AbaDoDetalhe[]; onFechar: () => void }) {
  const abrirLead = useAbrirLead();
  const [abaAtiva, setAbaAtiva] = useState(0);
  const [dados, setDados] = useState<RelatorioDetalhe | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [recarregar, setRecarregar] = useState(0);
  const abertoRef = useRef(aberto);
  abertoRef.current = aberto;

  // Nova abertura: volta para a primeira aba e esquece a lista anterior.
  useEffect(() => {
    if (aberto) {
      setAbaAtiva(0);
      setDados(null);
      setErro(null);
    }
  }, [aberto, titulo]);

  const url = abas[Math.min(abaAtiva, abas.length - 1)]?.url;
  useEffect(() => {
    if (!aberto || !url) return;
    const controller = new AbortController();
    setCarregando(true);
    api
      .get<RelatorioDetalhe>(url, controller.signal)
      .then((d) => {
        setDados(d);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar a lista.");
      })
      .finally(() => {
        if (!controller.signal.aborted) setCarregando(false);
      });
    return () => controller.abort();
  }, [aberto, url, recarregar]);

  // Tempo real: enquanto a lista está aberta, ela acompanha o que é cadastrado.
  useCrmEventos(() => {
    if (abertoRef.current) setRecarregar((n) => n + 1);
  }, 500);

  return (
    <Modal open={aberto} onClose={onFechar} title={titulo} size="xl">
      {abas.length > 1 && (
        <div role="tablist" className="mb-3 flex flex-wrap gap-1.5 border-b border-[var(--border)] pb-2">
          {abas.map((a, i) => (
            <button
              key={a.rotulo}
              type="button"
              role="tab"
              aria-selected={abaAtiva === i}
              onClick={() => setAbaAtiva(i)}
              className={`focus-ring cursor-pointer rounded-lg px-3 py-1 text-sm font-medium ${
                abaAtiva === i ? "bg-[var(--brand)] text-white" : "text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
              }`}
            >
              {a.rotulo}
            </button>
          ))}
        </div>
      )}

      {erro && !dados ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados ? (
        <Skeleton className="h-40" />
      ) : (
        <div className={carregando ? "opacity-70" : ""}>
          <p className="mb-2 text-sm text-[var(--fg-muted)]">
            <strong className="text-[var(--fg)]">{dados.titulo}</strong> — {dados.total.toLocaleString("pt-BR")} no total
            {dados.total > dados.itens.length && ` (mostrando os ${dados.itens.length.toLocaleString("pt-BR")} mais recentes)`}
          </p>
          {dados.itens.length === 0 ? (
            <p className="py-6 text-center text-sm text-[var(--fg-muted)]">Nada neste período.</p>
          ) : (
            <div className="max-h-[72vh] overflow-y-auto rounded-lg border border-[var(--border)]">
              <table className="w-full text-sm">
                <thead className="sticky top-0 bg-[var(--surface)]">
                  <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                    <th className="px-3 py-2 font-medium">Cliente</th>
                    <th className="px-3 py-2 font-medium">Placa</th>
                    <th className="px-3 py-2 font-medium">Telefone</th>
                    <th className="px-3 py-2 font-medium">Consultor</th>
                    <th className="px-3 py-2 font-medium">{dados.rotuloData}</th>
                    <th className="px-3 py-2 font-medium">Coluna</th>
                    <th className="px-3 py-2 font-medium">Canal</th>
                    {dados.rotuloValor && <th className="px-3 py-2 text-right font-medium">{dados.rotuloValor}</th>}
                  </tr>
                </thead>
                <tbody>
                  {dados.itens.map((i, n) => (
                    <tr
                      key={`${i.leadId}-${n}`}
                      onClick={() => {
                        onFechar();
                        abrirLead(i.leadId);
                      }}
                      className="cursor-pointer border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-hover)]"
                    >
                      <td className="px-3 py-2 font-medium text-[var(--brand)]">{i.cliente}</td>
                      <td className="whitespace-nowrap px-3 py-2 text-[var(--fg-muted)]">{i.placa ?? "—"}</td>
                      <td className="whitespace-nowrap px-3 py-2 text-[var(--fg-muted)]">{i.telefone ?? "—"}</td>
                      <td className="px-3 py-2 text-[var(--fg-muted)]">{i.consultor ?? "—"}</td>
                      <td className="whitespace-nowrap px-3 py-2 text-[var(--fg-muted)]">{formatarData(i.data)}</td>
                      <td className="px-3 py-2 text-[var(--fg-muted)]">{i.etapa ?? "Sem etapa"}</td>
                      <td className="px-3 py-2 text-[var(--fg-muted)]">{i.canal ?? "—"}</td>
                      {dados.rotuloValor && <td className="whitespace-nowrap px-3 py-2 text-right text-[var(--fg-muted)]">{i.valor != null ? formatarMoeda(i.valor) : "—"}</td>}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}
