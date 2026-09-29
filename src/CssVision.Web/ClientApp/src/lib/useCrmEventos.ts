import { useEffect, useRef } from "react";

/*
 * Uma única conexão de eventos (Server-Sent Events em /api/crm/eventos) por aba, compartilhada por
 * todas as telas e avisos que querem saber quando o quadro muda. Antes cada uso abria a própria
 * conexão (quadro + aviso de novos leads = 2 por pessoa).
 */
type Ouvinte = () => void;
const ouvintes = new Set<Ouvinte>();
let fonte: EventSource | null = null;

function notificarTodos() {
  ouvintes.forEach((ouvinte) => ouvinte());
}

function conectar() {
  if (fonte) return;
  fonte = new EventSource("/api/crm/eventos", { withCredentials: true });
  fonte.addEventListener("quadro-atualizado", notificarTodos);
}

function desconectarSeNinguemOuvindo() {
  if (ouvintes.size > 0 || !fonte) return;
  fonte.removeEventListener("quadro-atualizado", notificarTodos);
  fonte.close();
  fonte = null;
}

function conectado() {
  return fonte?.readyState === EventSource.OPEN;
}

/**
 * Chama `aoAtualizar` quando o quadro de leads muda — por outro usuário ou pela sincronização com o
 * Notion. Rajadas de eventos são agrupadas numa chamada só, e cada aba espera um tempinho aleatório
 * antes de recarregar: com muita gente usando, uma mudança não faz todas as telas consultarem o
 * servidor no mesmo instante.
 *
 * Rede de segurança: se a conexão em tempo real cair (proxy, rede instável), recarrega de tempos em
 * tempos; e ao voltar para uma aba que ficou muito tempo escondida.
 */
export function useCrmEventos(aoAtualizar: () => void, esperaMs = 800, intervaloSegurancaMs = 60_000) {
  const callbackRef = useRef(aoAtualizar);
  callbackRef.current = aoAtualizar;

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    let escondidaDesde: number | null = null;

    const agendar = () => {
      clearTimeout(timer);
      timer = setTimeout(() => callbackRef.current(), esperaMs + Math.random() * 2000);
    };

    ouvintes.add(agendar);
    conectar();

    const aoMudarVisibilidade = () => {
      if (document.visibilityState === "hidden") {
        escondidaDesde = Date.now();
      } else if (escondidaDesde !== null) {
        // Escondida por mais de 1 minuto: pode ter perdido eventos (o navegador pausa abas em segundo plano).
        if (Date.now() - escondidaDesde > 60_000 || !conectado()) agendar();
        escondidaDesde = null;
      }
    };
    document.addEventListener("visibilitychange", aoMudarVisibilidade);

    // Só recarrega por tempo quando o tempo real não está conectado.
    const intervalo = setInterval(() => {
      if (document.visibilityState === "visible" && !conectado()) agendar();
    }, intervaloSegurancaMs);

    return () => {
      clearTimeout(timer);
      clearInterval(intervalo);
      document.removeEventListener("visibilitychange", aoMudarVisibilidade);
      ouvintes.delete(agendar);
      desconectarSeNinguemOuvindo();
    };
  }, [esperaMs, intervaloSegurancaMs]);
}
