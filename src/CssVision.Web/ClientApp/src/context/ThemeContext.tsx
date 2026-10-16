import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

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

  useEffect(() => {
    document.documentElement.dataset.theme = tema;
    try {
      localStorage.setItem("crm-tema", tema);
    } catch {
      /* ignorar falha de armazenamento local */
    }
  }, [tema]);

  return (
    <ThemeContext.Provider value={{ tema, alternar: () => setTema((t) => TEMAS[(TEMAS.indexOf(t) + 1) % TEMAS.length]), definir: setTema }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (!context) throw new Error("useTheme deve ser usado dentro de ThemeProvider");
  return context;
}
