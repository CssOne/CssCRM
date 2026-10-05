import { useEffect, useRef } from "react";
import { useCrmEventos } from "./useCrmEventos";

/** Data de hoje (YYYY-MM-DD) no relógio do navegador. */
export function hojeIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** Primeiro dia do mês de hoje (YYYY-MM-01). */
export function mesAtualIso(): string {
  return `${hojeIso().slice(0, 7)}-01`;
}

/**
 * Chama `aoMudar(hoje, anterior)` quando o dia vira com a tela aberta — confere de 30 em 30 segundos e ao
 * voltar para a aba. Serve para trocar de mês sozinho (ranking, metas e painéis mostram o mês atual).
 */
export function useMudancaDeDia(aoMudar: (hoje: string, anterior: string) => void) {
  const diaRef = useRef(hojeIso());
  const callbackRef = useRef(aoMudar);
  callbackRef.current = aoMudar;

  useEffect(() => {
    const conferir = () => {
      const hoje = hojeIso();
      if (hoje === diaRef.current) return;
      const anterior = diaRef.current;
      diaRef.current = hoje;
      callbackRef.current(hoje, anterior);
    };
    const intervalo = setInterval(conferir, 30_000);
    const aoVoltar = () => {
      if (document.visibilityState === "visible") conferir();
    };
    document.addEventListener("visibilitychange", aoVoltar);
    return () => {
      clearInterval(intervalo);
      document.removeEventListener("visibilitychange", aoVoltar);
    };
  }, []);
}

/**
 * Mantém a tela em tempo real: recarrega quando o quadro muda (venda fechada, lead movido, sincronização
 * com o Notion — conexão de eventos compartilhada, ver useCrmEventos) e também quando o dia vira.
 */
export function useAtualizarAoVivo(recarregar: () => void) {
  useCrmEventos(recarregar, 500);
  useMudancaDeDia(recarregar);
}
