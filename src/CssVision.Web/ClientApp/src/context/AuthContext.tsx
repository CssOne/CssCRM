import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { api, isAbortError, isUnauthorized } from "../lib/api";
import type { LoginResult, Session } from "../lib/types";

interface AuthContextValue {
  sessao: Session | null;
  carregando: boolean;
  login: (email: string, senha: string, manterConectado?: boolean) => Promise<LoginResult>;
  loginDoisFatores: (codigo: string, manterConectado?: boolean, codigoRecuperacao?: boolean) => Promise<Session>;
  logout: () => Promise<void>;
  refetchSessao: () => Promise<void>;
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

  const login = useCallback(async (email: string, senha: string, manterConectado = true) => {
    const resultado = await api.post<LoginResult>("/account/login", { email, senha, manterConectado });
    if (resultado.sessao) setSessao(resultado.sessao);
    return resultado;
  }, []);

  const loginDoisFatores = useCallback(async (codigo: string, manterConectado = true, codigoRecuperacao = false) => {
    const resultado = await api.post<Session>("/account/login/2fa", { codigo, manterConectado, codigoRecuperacao });
    setSessao(resultado);
    return resultado;
  }, []);

  const logout = useCallback(async () => {
    await api.post("/account/logout");
    setSessao(null);
  }, []);

  const refetchSessao = useCallback(async () => {
    setSessao(await api.get<Session>("/account/session"));
  }, []);

  const temPapel = useCallback((...papeis: string[]) => !!sessao && papeis.some((p) => sessao.papeis.includes(p)), [sessao]);

  return (
    <AuthContext.Provider value={{ sessao, carregando, login, loginDoisFatores, logout, refetchSessao, temPapel }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth deve ser usado dentro de AuthProvider");
  return context;
}
