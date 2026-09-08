import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

type Tema = "light" | "dark";

interface ThemeContextValue {
  tema: Tema;
  alternar: () => void;
}

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

function temaInicial(): Tema {
  try {
    const salvo = localStorage.getItem("crm-tema");
    if (salvo === "light" || salvo === "dark") return salvo;
  } catch {
    /* localStorage indisponível — segue com o padrão do sistema */
  }
  return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [tema, setTema] = useState<Tema>(temaInicial);

  useEffect(() => {
    document.documentElement.classList.toggle("dark", tema === "dark");
    try {
      localStorage.setItem("crm-tema", tema);
    } catch {
      /* ignorar falha de armazenamento local */
    }
  }, [tema]);

  return (
    <ThemeContext.Provider value={{ tema, alternar: () => setTema((t) => (t === "dark" ? "light" : "dark")) }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (!context) throw new Error("useTheme deve ser usado dentro de ThemeProvider");
  return context;
}
