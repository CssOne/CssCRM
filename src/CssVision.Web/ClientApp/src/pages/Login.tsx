import { useState, type FormEvent } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { isUnauthorized } from "../lib/api";
import { useAuth } from "../context/AuthContext";
import { Button, Input, Label } from "../components/ui";

export function LoginPage() {
  const { sessao, login } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  if (sessao) return <Navigate to={sessao.areaInicial} replace />;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      const resultado = await login(email, senha);
      navigate(resultado.areaInicial, { replace: true });
    } catch (e) {
      setErro(isUnauthorized(e) ? "E-mail ou senha inválidos." : "Não foi possível entrar. Tente novamente.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-[var(--bg)] px-4">
      <div className="w-full max-w-sm rounded-xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-sm">
        <h1 className="mb-1 text-lg font-bold text-[var(--fg)]">CSS Vision CRM</h1>
        <p className="mb-6 text-sm text-[var(--fg-muted)]">Entre com sua conta para acessar o CRM comercial.</p>

        <form onSubmit={handleSubmit} className="space-y-4" noValidate>
          <div>
            <Label htmlFor="email" required>
              E-mail
            </Label>
            <Input
              id="email"
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div>
            <Label htmlFor="senha" required>
              Senha
            </Label>
            <Input
              id="senha"
              type="password"
              autoComplete="current-password"
              required
              value={senha}
              onChange={(e) => setSenha(e.target.value)}
            />
          </div>

          {erro && (
            <p role="alert" className="rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)]">
              {erro}
            </p>
          )}

          <Button type="submit" className="w-full" loading={enviando}>
            Entrar
          </Button>
        </form>
      </div>
    </div>
  );
}
