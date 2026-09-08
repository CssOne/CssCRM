import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { api, isAbortError, isUnauthorized } from "../lib/api";
import type { Session } from "../lib/types";

interface AuthContextValue {
  sessao: Session | null;
  carregando: boolean;
  login: (email: string, senha: string) => Promise<Session>;
  logout: () => Promise<void>;
  temPapel: (...papeis: string[]) => boolean;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [sessao, setSessao] = useState<Session | null>(null);
  const [carregando, setCarregando] = useState(true);

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<Session>("/account/session", controller.signal)
      .then(setSessao)
      .catch((erro) => {
        if (isAbortError(erro)) return;
        if (!isUnauthorized(erro)) console.error(erro);
        setSessao(null);
      })
      .finally(() => {
        if (!controller.signal.aborted) setCarregando(false);
      });
    return () => controller.abort();
  }, []);

  const login = useCallback(async (email: string, senha: string) => {
    const resultado = await api.post<Session>("/account/login", { email, senha });
    setSessao(resultado);
    return resultado;
  }, []);

  const logout = useCallback(async () => {
    await api.post("/account/logout");
    setSessao(null);
  }, []);

  const temPapel = useCallback((...papeis: string[]) => !!sessao && papeis.some((p) => sessao.papeis.includes(p)), [sessao]);

  return <AuthContext.Provider value={{ sessao, carregando, login, logout, temPapel }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth deve ser usado dentro de AuthProvider");
  return context;
}
