import { useEffect, useRef } from "react";

/**
 * Assina o stream de eventos do CRM (`/api/crm/eventos`, Server-Sent Events) e chama `aoAtualizar`
 * quando o quadro de leads muda — por outro usuário ou pela sincronização com o Notion.
 * Rajadas de eventos (ex: importação inicial do Notion) são agrupadas em uma única chamada.
 */
export function useCrmEventos(aoAtualizar: () => void, esperaMs = 800, intervaloSegurancaMs = 60_000) {
  const callbackRef = useRef(aoAtualizar);
  callbackRef.current = aoAtualizar;

  useEffect(() => {
    const fonte = new EventSource("/api/crm/eventos", { withCredentials: true });
    let timer: ReturnType<typeof setTimeout> | undefined;

    const agendar = () => {
      clearTimeout(timer);
      timer = setTimeout(() => callbackRef.current(), esperaMs);
    };

    fonte.addEventListener("quadro-atualizado", agendar);

    // Rede de segurança caso o stream não chegue (proxy segurando a conexão, rede instável):
    // recarrega ao voltar para a aba e periodicamente enquanto ela está visível.
    const aoMudarVisibilidade = () => {
      if (document.visibilityState === "visible") agendar();
    };
    document.addEventListener("visibilitychange", aoMudarVisibilidade);
    const intervalo = setInterval(() => {
      if (document.visibilityState === "visible") agendar();
    }, intervaloSegurancaMs);

    return () => {
      clearTimeout(timer);
      clearInterval(intervalo);
      document.removeEventListener("visibilitychange", aoMudarVisibilidade);
      fonte.removeEventListener("quadro-atualizado", agendar);
      fonte.close();
    };
  }, [esperaMs, intervaloSegurancaMs]);
}
