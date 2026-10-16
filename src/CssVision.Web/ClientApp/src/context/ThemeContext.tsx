import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";

export type Tema = "light" | "dark" | "glass";

export const TEMAS: Tema[] = ["light", "dark", "glass"];

interface ThemeContextValue {
  tema: Tema;
  /** Passa para o próximo tema (claro → escuro → translúcido). */
  alternar: () => void;
  definir: (tema: Tema) => void;
}

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

function temaInicial(): Tema {
  try {
    const salvo = localStorage.getItem("crm-tema");
    if (salvo === "light" || salvo === "dark" || salvo === "glass") return salvo;
  } catch {
    /* localStorage indisponível — segue com o padrão do sistema */
  }
  return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [tema, setTema] = useState<Tema>(temaInicial);
  const ultimoClique = useRef<{ x: number; y: number }>({ x: window.innerWidth / 2, y: 0 });

  useEffect(() => {
    document.documentElement.dataset.theme = tema;
    try {
      localStorage.setItem("crm-tema", tema);
    } catch {
      /* ignorar falha de armazenamento local */
    }
  }, [tema]);

  // De onde a transição nasce: o último clique (o botão de tema).
  useEffect(() => {
    const guardar = (e: PointerEvent) => {
      ultimoClique.current = { x: e.clientX, y: e.clientY };
    };
    document.addEventListener("pointerdown", guardar, true);
    return () => document.removeEventListener("pointerdown", guardar, true);
  }, []);

  /**
   * Troca o tema com uma "revelação" circular que cresce a partir do botão clicado (View Transitions).
   * Sem suporte (ou com "reduzir movimento"), cai num fade suave das cores.
   */
  const trocar = useCallback((novo: Tema) => {
    const raiz = document.documentElement;
    const reduzir = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
    // Só as variáveis de CSS mudam durante a animação (barato, mesmo com milhares de cartões); o estado do React, que
    // refaz gráficos e componentes pesados, é atualizado depois, sem competir com a animação.
    const aplicar = () => {
      raiz.dataset.theme = novo;
    };
    if (reduzir) {
      aplicar();
      setTema(novo);
      return;
    }
    const vt = (document as Document & { startViewTransition?: (cb: () => void) => { ready: Promise<void>; finished: Promise<void> } }).startViewTransition;
    if (!vt) {
      raiz.classList.add("tema-trocando");
      aplicar();
      setTema(novo);
      window.setTimeout(() => raiz.classList.remove("tema-trocando"), 450);
      return;
    }
    const { x, y } = ultimoClique.current;
    const raio = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y));
    const transicao = vt.call(document, aplicar);
    transicao.ready
      .then(() => {
        raiz.animate(
          { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${raio}px at ${x}px ${y}px)`] },
          { duration: 600, easing: "cubic-bezier(0.22, 1, 0.36, 1)", pseudoElement: "::view-transition-new(root)" },
        );
      })
      .catch(() => undefined);
    transicao.finished.finally(() => setTema(novo));
  }, []);

  return (
    <ThemeContext.Provider
      value={{
        tema,
        alternar: () => trocar(TEMAS[(TEMAS.indexOf(tema) + 1) % TEMAS.length]),
        definir: (novo) => {
          if (novo !== tema) trocar(novo);
        },
      }}
    >
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (!context) throw new Error("useTheme deve ser usado dentro de ThemeProvider");
  return context;
}
