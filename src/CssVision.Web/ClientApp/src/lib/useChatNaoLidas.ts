import { useCallback, useEffect, useState } from "react";
import { api } from "./api";
import type { DiscordChatNaoLidas } from "./types";

const VAZIO: DiscordChatNaoLidas = { total: 0, porConversa: {} };

/**
 * Mensagens não lidas do chat (grupos e conversas 1:1). Consulta a cada <paramref name="intervaloMs"/> enquanto a aba está visível e
 * de novo ao voltar para a aba. Falha de rede ou Discord desligado = zero, sem incomodar a pessoa.
 */
export function useChatNaoLidas(ativo: boolean, intervaloMs: number) {
  const [dados, setDados] = useState<DiscordChatNaoLidas>(VAZIO);

  const atualizar = useCallback(() => {
    if (!ativo) return;
    api
      .get<DiscordChatNaoLidas>("/crm/discord/chat/nao-lidas")
      .then(setDados)
      .catch(() => undefined);
  }, [ativo]);

  useEffect(() => {
    if (!ativo) {
      setDados(VAZIO);
      return;
    }
    atualizar();
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") atualizar();
    }, intervaloMs);
    const aoVoltar = () => {
      if (document.visibilityState === "visible") atualizar();
    };
    document.addEventListener("visibilitychange", aoVoltar);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", aoVoltar);
    };
  }, [ativo, intervaloMs, atualizar]);

  return { ...dados, atualizar };
}

/** "50" quer dizer "50 ou mais". */
export function rotuloNaoLidas(n: number): string {
  return n >= 50 ? "50+" : String(n);
}
