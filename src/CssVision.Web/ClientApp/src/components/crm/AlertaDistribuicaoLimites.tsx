import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle } from "lucide-react";
import { api } from "../../lib/api";
import { useCrmEventos } from "../../lib/useCrmEventos";
import type { AlertaDistribuicao } from "../../lib/types";
import { useToast } from "../ui";

/**
 * Aviso para administradores, gestores e supervisores: todos os consultores aptos bateram o limite
 * diário/mensal e ainda há leads do tráfego pago sem responsável. O gestor decide continuar a
 * distribuição mesmo com o limite (vale até o fim do dia) ou deixar os leads parados. O servidor
 * também manda a mesma notificação por push (AlertaDistribuicaoService).
 */
export function AlertaDistribuicaoLimites() {
  const { notificar } = useToast();
  const [estado, setEstado] = useState<AlertaDistribuicao | null>(null);
  const [dispensadoEm, setDispensadoEm] = useState<number | null>(null);
  const [enviando, setEnviando] = useState(false);

  const verificar = useCallback(() => {
    api.get<AlertaDistribuicao>("/crm/management/alerta-distribuicao").then(setEstado).catch(() => {});
  }, []);

  useEffect(() => {
    verificar();
    // A conexão em tempo real só avisa mudanças no quadro: confere também de minuto em minuto.
    const intervalo = setInterval(() => {
      if (document.visibilityState === "visible") verificar();
    }, 60_000);
    return () => clearInterval(intervalo);
  }, [verificar]);
  useCrmEventos(verificar, 500);

  async function continuar(valor: boolean) {
    setEnviando(true);
    try {
      const novo = await api.put<AlertaDistribuicao>("/crm/management/continuar-distribuicao", { continuar: valor });
      setEstado(novo);
      notificar(
        "success",
        valor ? "Distribuição liberada até o fim do dia, ignorando os limites." : "Os limites voltaram a valer na distribuição."
      );
    } catch {
      notificar("error", "Não foi possível alterar a distribuição.");
    } finally {
      setEnviando(false);
    }
  }

  if (!estado) return null;

  if (estado.continuarAteEm) {
    return (
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm">
        <span className="text-[var(--fg-muted)]">
          A distribuição automática está ignorando os limites diário e mensal até o fim de hoje.
        </span>
        <button
          type="button"
          disabled={enviando}
          onClick={() => continuar(false)}
          className="focus-ring cursor-pointer font-medium text-[var(--brand)] hover:underline disabled:opacity-50"
        >
          Voltar a respeitar os limites
        </button>
      </div>
    );
  }

  if (!estado.bloqueada || (dispensadoEm !== null && dispensadoEm === estado.leadsBloqueadosPorLimite)) return null;

  return (
    <div role="alert" className="mb-4 rounded-lg border border-[var(--warning)] bg-[var(--surface)] p-3 text-sm">
      <div className="flex items-start gap-2">
        <AlertTriangle className="mt-0.5 size-4 shrink-0 text-[var(--warning)]" aria-hidden />
        <div className="min-w-0 flex-1">
          <p className="font-semibold text-[var(--fg)]">Todos os consultores atingiram o limite de leads</p>
          <p className="text-[var(--fg-muted)]">
            {estado.leadsBloqueadosPorLimite} lead(s) do tráfego pago estão sem responsável. {estado.noLimiteDiario} de {estado.consultores}{" "}
            consultor(es) no limite do dia e {estado.noLimiteMensal} no do mês. Deseja continuar a distribuição automática mesmo com o limite
            atingido?
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-3 text-xs font-medium">
            <button
              type="button"
              disabled={enviando}
              onClick={() => continuar(true)}
              className="focus-ring cursor-pointer rounded-md bg-[var(--brand)] px-3 py-1.5 text-white disabled:opacity-50"
            >
              Continuar distribuindo hoje
            </button>
            <button
              type="button"
              onClick={() => setDispensadoEm(estado.leadsBloqueadosPorLimite)}
              className="focus-ring cursor-pointer text-[var(--fg-muted)] hover:underline"
            >
              Manter parado
            </button>
            <Link to="/app/crm/gestao" className="text-[var(--brand)] hover:underline">
              Ajustar limites na Gestão comercial
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
