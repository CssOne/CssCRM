import { useEffect } from "react";
import { api } from "./api";

const INTERVALO_MS = 60_000;

/**
 * Avisa o servidor, a cada minuto e só com a aba visível, que esta pessoa está com o CRM aberto: é o que faz o chat mostrar "online".
 * Falha de rede é ignorada (na próxima batida volta).
 */
export function usePresenca(ativo: boolean) {
  useEffect(() => {
    if (!ativo) return;
    const avisar = () => {
      if (document.visibilityState === "visible") api.post("/crm/presenca").catch(() => undefined);
    };
    avisar();
    const timer = window.setInterval(avisar, INTERVALO_MS);
    document.addEventListener("visibilitychange", avisar);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", avisar);
    };
  }, [ativo]);
}
