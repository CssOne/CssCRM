import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { CheckCircle2, Eye, EyeOff, Lock, XCircle } from "lucide-react";
import { api, ApiRequestError } from "../lib/api";
import { Button } from "../components/ui";

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const email = params.get("email") ?? "";
  const token = params.get("token") ?? "";

  const [novaSenha, setNovaSenha] = useState("");
  const [confirmarSenha, setConfirmarSenha] = useState("");
  const [mostrarSenha, setMostrarSenha] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [concluido, setConcluido] = useState(false);

  const linkInvalido = !email || !token;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (novaSenha !== confirmarSenha) {
      setErro("A confirmação não confere com a nova senha.");
      return;
    }
    setErro(null);
    setEnviando(true);
    try {
      await api.post("/account/reset-password", { email, token, novaSenha });
      setConcluido(true);
    } catch (e) {
      setErro(e instanceof ApiRequestError ? e.message : "Não foi possível redefinir sua senha. Tente novamente.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="flex min-h-screen">
      <div className="login-aurora relative hidden w-1/2 flex-col justify-between overflow-hidden p-12 lg:flex">
        <div className="relative z-10 flex items-center gap-2.5">
          <img src="/logo-css-white.png" alt="CSS Brasil" className="size-10 shrink-0 object-contain" />
          <div className="leading-tight">
            <p className="text-base font-extrabold tracking-tight text-white">CSS Brasil</p>
            <p className="text-[11px] font-semibold uppercase tracking-wider text-white/60">CRM Comercial</p>
          </div>
        </div>
        <div className="relative z-10 max-w-md">
          <h1 className="text-3xl font-extrabold leading-tight text-white">Escolha uma nova senha.</h1>
          <p className="mt-4 text-sm text-white/70">Sua nova senha vale a partir de agora em todos os seus acessos.</p>
        </div>
      </div>

      <div className="flex w-full flex-col items-center justify-center bg-[var(--surface)] px-6 py-12 lg:w-1/2">
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-2.5 lg:hidden">
            <img src="/logo-css.svg" alt="CSS Brasil" className="size-9 shrink-0 object-contain" />
            <p className="text-lg font-extrabold text-[var(--fg)]">CSS Brasil</p>
          </div>

          {linkInvalido ? (
            <>
              <div className="mb-4 flex items-center gap-3">
                <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-[var(--danger-soft)] text-[var(--danger)]">
                  <XCircle className="size-5" />
                </div>
                <h2 className="text-xl font-extrabold text-[var(--fg)]">Link inválido</h2>
              </div>
              <p className="text-sm text-[var(--fg-muted)]">
                Este link de redefinição está incompleto ou já foi usado. Volte ao login e solicite um novo.
              </p>
              <Link to="/login" className="mt-6 inline-block">
                <Button type="button" className="w-full">
                  Voltar para o login
                </Button>
              </Link>
            </>
          ) : concluido ? (
            <>
              <div className="mb-4 flex items-center gap-3">
                <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-[var(--success-soft)] text-[var(--success)]">
                  <CheckCircle2 className="size-5" />
                </div>
                <h2 className="text-xl font-extrabold text-[var(--fg)]">Senha redefinida</h2>
              </div>
              <p className="text-sm text-[var(--fg-muted)]">Sua senha foi alterada com sucesso. Já pode entrar com a nova senha.</p>
              <Link to="/login" className="mt-6 inline-block w-full">
                <Button type="button" className="w-full">
                  Ir para o login
                </Button>
              </Link>
            </>
          ) : (
            <>
              <h2 className="text-2xl font-extrabold text-[var(--fg)]">Escolher nova senha</h2>
              <p className="mt-1 mb-6 text-sm text-[var(--fg-muted)]">
                Definindo uma nova senha para <strong>{email}</strong>.
              </p>

              <form onSubmit={handleSubmit} className="space-y-4" noValidate>
                <div>
                  <label htmlFor="nova-senha" className="mb-1 block text-sm font-medium text-[var(--fg)]">
                    Nova senha
                  </label>
                  <div className="relative">
                    <Lock className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                    <input
                      id="nova-senha"
                      type={mostrarSenha ? "text" : "password"}
                      autoComplete="new-password"
                      required
                      minLength={8}
                      autoFocus
                      value={novaSenha}
                      onChange={(e) => setNovaSenha(e.target.value)}
                      className="focus-ring w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] py-2.5 pl-10 pr-10 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
                      placeholder="••••••••"
                    />
                    <button
                      type="button"
                      onClick={() => setMostrarSenha((v) => !v)}
                      aria-label={mostrarSenha ? "Ocultar senha" : "Mostrar senha"}
                      className="focus-ring absolute right-3 top-1/2 -translate-y-1/2 text-[var(--fg-muted)] hover:text-[var(--fg)] cursor-pointer"
                    >
                      {mostrarSenha ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
                    </button>
                  </div>
                </div>

                <div>
                  <label htmlFor="confirmar-senha" className="mb-1 block text-sm font-medium text-[var(--fg)]">
                    Confirmar nova senha
                  </label>
                  <div className="relative">
                    <Lock className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                    <input
                      id="confirmar-senha"
                      type={mostrarSenha ? "text" : "password"}
                      autoComplete="new-password"
                      required
                      minLength={8}
                      value={confirmarSenha}
                      onChange={(e) => setConfirmarSenha(e.target.value)}
                      className="focus-ring w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] py-2.5 pl-10 pr-3 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
                      placeholder="••••••••"
                    />
                  </div>
                </div>

                {erro && (
                  <p role="alert" className="rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)]">
                    {erro}
                  </p>
                )}

                <Button type="submit" className="w-full" loading={enviando}>
                  Redefinir senha
                </Button>
              </form>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
