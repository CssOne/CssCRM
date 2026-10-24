import { useEffect, useRef, useState } from "react";

const reduzido = () => typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/**
 * Número que "conta" até o valor novo: começa em zero ao aparecer e, quando o valor muda (atualização ao vivo), segue do que está na tela até o novo,
 * em vez de saltar. Sem animação para quem pede movimento reduzido.
 */
export function useContagem(alvo: number, duracaoMs = 700): number {
  const [exibido, setExibido] = useState(() => (reduzido() ? alvo : 0));
  const atual = useRef(exibido);

  useEffect(() => {
    if (reduzido() || !Number.isFinite(alvo)) {
      atual.current = alvo;
      setExibido(alvo);
      return;
    }
    const de = atual.current;
    if (de === alvo) return;
    const inicio = performance.now();
    let quadro = 0;
    const passo = (agora: number) => {
      const t = Math.min(1, (agora - inicio) / duracaoMs);
      const suave = 1 - Math.pow(1 - t, 3); // desacelera no fim
      const valor = de + (alvo - de) * suave;
      atual.current = valor;
      setExibido(valor);
      if (t < 1) quadro = requestAnimationFrame(passo);
    };
    quadro = requestAnimationFrame(passo);
    return () => cancelAnimationFrame(quadro);
  }, [alvo, duracaoMs]);

  return exibido;
}

/** Lê "55", "1.234" ou "R$ 6.700,00" (pt-BR) e devolve os pedaços para animar e reescrever; nulo quando o texto não é só um número. */
export function lerNumeroBr(texto: string): { numero: number; prefixo: string; casas: number } | null {
  const m = /^(R\$\s)?(\d{1,3}(?:\.\d{3})*|\d+)(?:,(\d{1,2}))?$/.exec(texto.trim());
  if (!m) return null;
  const inteiro = Number(m[2].replace(/\./g, ""));
  const casas = m[3]?.length ?? 0;
  const numero = inteiro + (m[3] ? Number(m[3]) / 10 ** casas : 0);
  return { numero, prefixo: m[1] ?? "", casas };
}

export function formatarNumeroBr(valor: number, casas: number, prefixo: string): string {
  return prefixo + valor.toLocaleString("pt-BR", { minimumFractionDigits: casas, maximumFractionDigits: casas });
}

/** Texto numérico animado (inteiro ou moeda); outros textos aparecem como vieram. */
export function NumeroAnimado({ valor }: { valor: string }) {
  const lido = lerNumeroBr(valor);
  const animado = useContagem(lido?.numero ?? 0);
  if (!lido) return <>{valor}</>;
  return <>{formatarNumeroBr(lido.casas === 0 ? Math.round(animado) : animado, lido.casas, lido.prefixo)}</>;
}
