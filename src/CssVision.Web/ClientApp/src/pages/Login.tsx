import { useState, type FormEvent } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { ArrowLeft, Eye, EyeOff, KeyRound, Lock, Mail, MailCheck, ShieldCheck } from "lucide-react";
import { api, isUnauthorized } from "../lib/api";
import { useAuth } from "../context/AuthContext";
import { Button, Checkbox } from "../components/ui";

export function LoginPage() {
  const { sessao, login, loginDoisFatores } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [manterConectado, setManterConectado] = useState(true);
  const [mostrarSenha, setMostrarSenha] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  const [aguardando2fa, setAguardando2fa] = useState(false);
  const [codigo2fa, setCodigo2fa] = useState("");
  const [usarCodigoRecuperacao, setUsarCodigoRecuperacao] = useState(false);

  const [mostrarEsqueciSenha, setMostrarEsqueciSenha] = useState(false);
  const [emailRecuperacao, setEmailRecuperacao] = useState("");
  const [emailRecuperacaoEnviado, setEmailRecuperacaoEnviado] = useState(false);

  if (sessao) return <Navigate to={sessao.areaInicial} replace />;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      const resultado = await login(email, senha, manterConectado);
      if (resultado.requerDoisFatores) {
        setAguardando2fa(true);
      } else {
        navigate(resultado.sessao!.areaInicial, { replace: true });
      }
    } catch (e) {
      setErro(isUnauthorized(e) ? "E-mail ou senha inválidos." : "Não foi possível entrar. Tente novamente.");
    } finally {
      setEnviando(false);
    }
  }

  async function handleSubmit2fa(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      const resultado = await loginDoisFatores(codigo2fa.trim(), manterConectado, usarCodigoRecuperacao);
      navigate(resultado.areaInicial, { replace: true });
    } catch (e) {
      setErro(isUnauthorized(e) ? "Código inválido." : "Não foi possível entrar. Tente novamente.");
    } finally {
      setEnviando(false);
    }
  }

  function voltarParaCredenciais() {
    setAguardando2fa(false);
    setCodigo2fa("");
    setUsarCodigoRecuperacao(false);
    setErro(null);
  }

  function abrirEsqueciSenha(e: React.MouseEvent) {
    e.preventDefault();
    setEmailRecuperacao(email);
    setEmailRecuperacaoEnviado(false);
    setErro(null);
    setMostrarEsqueciSenha(true);
  }

  function voltarDoEsqueciSenha() {
    setMostrarEsqueciSenha(false);
    setEmailRecuperacaoEnviado(false);
    setErro(null);
  }

  async function handleSubmitEsqueciSenha(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await api.post("/account/forgot-password", { email: emailRecuperacao.trim() });
      setEmailRecuperacaoEnviado(true);
    } catch {
      setErro("Não foi possível enviar o e-mail agora. Tente novamente em instantes.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="flex min-h-screen">
      {/* Painel esquerdo: marca, com fundo animado */}
      <div className="login-aurora relative hidden w-1/2 flex-col justify-between overflow-hidden p-12 lg:flex">
        <div className="relative z-10 flex items-center gap-2.5">
          <img src="/logo-css-white.png" alt="CSS Brasil" className="size-10 shrink-0 object-contain" />
          <div className="leading-tight">
            <p className="text-base font-extrabold tracking-tight text-white">CSS Brasil</p>
            <p className="text-[11px] font-semibold uppercase tracking-wider text-white/60">CRM Comercial</p>
          </div>
        </div>

        <div className="relative z-10 max-w-md">
          <h1 className="text-3xl font-extrabold leading-tight text-white">
            Gestão comercial completa para sua consultoria.
          </h1>
          <p className="mt-4 text-sm text-white/70">
            Acompanhe leads, propostas e o desempenho da sua equipe em um só lugar.
          </p>
        </div>
      </div>

      {/* Painel direito: formulário */}
      <div className="flex w-full flex-col items-center justify-center bg-[var(--surface)] px-6 py-12 lg:w-1/2">
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-2.5 lg:hidden">
            <img src="/logo-css.svg" alt="CSS Brasil" className="size-9 shrink-0 object-contain" />
            <p className="text-lg font-extrabold text-[var(--fg)]">CSS Brasil</p>
          </div>

          {mostrarEsqueciSenha ? (
            <>
              <button
                type="button"
                onClick={voltarDoEsqueciSenha}
                className="focus-ring mb-4 flex items-center gap-1.5 text-sm font-medium text-[var(--fg-muted)] hover:text-[var(--fg)] cursor-pointer"
              >
                <ArrowLeft className="size-4" /> Voltar
              </button>

              {emailRecuperacaoEnviado ? (
                <>
                  <div className="mb-4 flex items-center gap-3">
                    <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-[var(--success-soft)] text-[var(--success)]">
                      <MailCheck className="size-5" />
                    </div>
                    <div>
                      <h2 className="text-xl font-extrabold text-[var(--fg)]">Verifique seu e-mail</h2>
                    </div>
                  </div>
                  <p className="text-sm text-[var(--fg-muted)]">
                    Se houver uma conta com o e-mail <strong>{emailRecuperacao}</strong>, enviamos um link para você escolher uma nova
                    senha. Confira também a caixa de spam.
                  </p>
                  <Button type="button" variant="secondary" className="mt-6 w-full" onClick={voltarDoEsqueciSenha}>
                    Voltar para o login
                  </Button>
                </>
              ) : (
                <>
                  <h2 className="text-xl font-extrabold text-[var(--fg)]">Redefinir senha</h2>
                  <p className="mt-1 mb-6 text-sm text-[var(--fg-muted)]">
                    Informe seu e-mail. Se houver uma conta cadastrada, enviaremos um link para você escolher uma nova senha.
                  </p>

                  <form onSubmit={handleSubmitEsqueciSenha} className="space-y-4" noValidate>
                    <div>
                      <label htmlFor="email-recuperacao" className="mb-1 block text-sm font-medium text-[var(--fg)]">
                        E-mail
                      </label>
                      <div className="relative">
                        <Mail className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                        <input
                          id="email-recuperacao"
                          type="email"
                          autoComplete="username"
                          autoFocus
                          required
                          value={emailRecuperacao}
                          onChange={(e) => setEmailRecuperacao(e.target.value)}
                          className="focus-ring w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] py-2.5 pl-10 pr-3 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
                          placeholder="voce@empresa.com"
                        />
                      </div>
                    </div>

                    {erro && (
                      <p role="alert" className="rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)]">
                        {erro}
                      </p>
                    )}

                    <Button type="submit" className="w-full" loading={enviando}>
                      Enviar link de redefinição
                    </Button>
                  </form>
                </>
              )}
            </>
          ) : aguardando2fa ? (
            <>
              <button
                type="button"
                onClick={voltarParaCredenciais}
                className="focus-ring mb-4 flex items-center gap-1.5 text-sm font-medium text-[var(--fg-muted)] hover:text-[var(--fg)] cursor-pointer"
              >
                <ArrowLeft className="size-4" /> Voltar
              </button>
              <div className="mb-4 flex items-center gap-3">
                <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-[var(--brand-soft)] text-[var(--brand)]">
                  <ShieldCheck className="size-5" />
                </div>
                <div>
                  <h2 className="text-xl font-extrabold text-[var(--fg)]">Verificação em duas etapas</h2>
                  <p className="text-sm text-[var(--fg-muted)]">
                    {usarCodigoRecuperacao ? "Informe um código de recuperação." : "Digite o código do seu aplicativo autenticador."}
                  </p>
                </div>
              </div>

              <form onSubmit={handleSubmit2fa} className="space-y-4" noValidate>
                <div>
                  <label htmlFor="codigo2fa" className="mb-1 block text-sm font-medium text-[var(--fg)]">
                    {usarCodigoRecuperacao ? "Código de recuperação" : "Código de 6 dígitos"}
                  </label>
                  <div className="relative">
                    <KeyRound className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                    <input
                      id="codigo2fa"
                      type="text"
                      inputMode={usarCodigoRecuperacao ? "text" : "numeric"}
                      autoComplete="one-time-code"
                      autoFocus
                      required
                      value={codigo2fa}
                      onChange={(e) => setCodigo2fa(e.target.value)}
                      className="focus-ring w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] py-2.5 pl-10 pr-3 text-sm tracking-widest text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
                      placeholder={usarCodigoRecuperacao ? "xxxxx-xxxxx" : "000000"}
                    />
                  </div>
                </div>

                {erro && (
                  <p role="alert" className="rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)]">
                    {erro}
                  </p>
                )}

                <Button type="submit" className="w-full" loading={enviando}>
                  Confirmar
                </Button>

                <button
                  type="button"
                  onClick={() => {
                    setUsarCodigoRecuperacao((v) => !v);
                    setCodigo2fa("");
                    setErro(null);
                  }}
                  className="focus-ring w-full text-center text-sm font-medium text-[var(--brand)] hover:opacity-80 cursor-pointer"
                >
                  {usarCodigoRecuperacao ? "Usar código do aplicativo autenticador" : "Perdeu o acesso? Usar código de recuperação"}
                </button>
              </form>
            </>
          ) : (
            <>
              <h2 className="text-2xl font-extrabold text-[var(--fg)]">Entrar</h2>
              <p className="mt-1 mb-6 text-sm text-[var(--fg-muted)]">Entre com sua conta para acessar o CRM comercial.</p>

              <form onSubmit={handleSubmit} className="space-y-4" noValidate>
                <div>
                  <label htmlFor="email" className="mb-1 block text-sm font-medium text-[var(--fg)]">
                    E-mail
                  </label>
                  <div className="relative">
                    <Mail className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                    <input
                      id="email"
                      type="email"
                      autoComplete="username"
                      required
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      className="focus-ring w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] py-2.5 pl-10 pr-3 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
                      placeholder="voce@empresa.com"
                    />
                  </div>
                </div>

                <div>
                  <label htmlFor="senha" className="mb-1 block text-sm font-medium text-[var(--fg)]">
                    Senha
                  </label>
                  <div className="relative">
                    <Lock className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                    <input
                      id="senha"
                      type={mostrarSenha ? "text" : "password"}
                      autoComplete="current-password"
                      required
                      value={senha}
                      onChange={(e) => setSenha(e.target.value)}
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

                <div className="flex items-center justify-between">
                  <Checkbox
                    label="Manter conectado"
                    checked={manterConectado}
                    onChange={(e) => setManterConectado(e.target.checked)}
                  />
                  <button
                    type="button"
                    onClick={abrirEsqueciSenha}
                    className="focus-ring text-sm font-medium text-[var(--brand)] hover:opacity-80 cursor-pointer"
                  >
                    Esqueceu a senha?
                  </button>
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
            </>
          )}
        </div>
      </div>
    </div>
  );
}
