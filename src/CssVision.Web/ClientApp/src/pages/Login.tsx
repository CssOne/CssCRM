import { useEffect, useRef, useState, type FormEvent } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { AlertTriangle, ArrowLeft, ArrowRight, Eye, EyeOff, KeyRound, Lock, Mail, MailCheck, ShieldCheck } from "lucide-react";
import { ApiRequestError, api, isUnauthorized } from "../lib/api";
import { useAuth } from "../context/AuthContext";
import { Button, Checkbox } from "../components/ui";
import { SeletorDeTema } from "../components/Shell";

/*
 * THESIS: a rota. A CSS Brasil protege veículos na estrada e o consultor leva cada lead por um caminho até a adesão: a tela de login é um
 * mapa de rota (curvas de nível + estrada + paradas), sem foto e sem painel de gráficos. Recusa a tela dividida com mockup de dashboard.
 * OWN-WORLD: azul-marinho da marca (#04264c → #07428b) com traços brancos finos; a estrada é a única linha forte; texto branco; um azul claro (#a6c3e0) só no brilho que percorre a rota.
 * STORY: o visitante entende de relance que é o CRM comercial da CSS Brasil e acompanha a jornada lead → contato → proposta → adesão; a ação (Entrar) fica à direita.
 * FIRST VIEWPORT: à esquerda (55%) o painel azul com a rota subindo da base à direita e o título embaixo; à direita (45%) o formulário limpo, sem cartão extra.
 * FORM: arte autoral em SVG (sem imagem externa), animação de entrada única e um veículo que percorre a rota; parada total com "reduzir movimento".
 */
const PARADAS: { rotulo: string; x: number; y: number; lado: "esq" | "dir" | "abaixo" }[] = [
  { rotulo: "Lead", x: 250, y: 540, lado: "dir" },
  { rotulo: "Contato", x: 430, y: 470, lado: "dir" },
  { rotulo: "Proposta", x: 400, y: 250, lado: "esq" },
  { rotulo: "Adesão", x: 560, y: 110, lado: "esq" },
];

const CAMINHO = "M 40 660 C 140 660 170 560 250 540 C 330 520 400 560 430 470 C 460 380 360 330 400 250 C 430 180 520 190 560 110";
const MANCHA = "M 620 90 C 700 80 790 150 780 230 C 770 320 690 360 610 340 C 520 320 470 270 480 190 C 490 120 550 95 620 90 Z";
const MANCHA_2 = "M 120 560 C 190 540 260 590 250 660 C 240 730 160 760 100 735 C 40 710 20 640 50 595 C 70 570 95 565 120 560 Z";

function PainelDaMarca() {
  const reduzir = typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

  return (
    <div className="login-rota relative hidden w-[55%] flex-col overflow-hidden rounded-3xl p-10 lg:flex">
      {/* Curvas de nível: o "terreno" por onde a rota passa. */}
      <svg aria-hidden className="pointer-events-none absolute inset-0 size-full text-white" viewBox="0 0 800 800" preserveAspectRatio="xMidYMid slice" fill="none">
        {[0.3, 0.5, 0.72, 1, 1.3, 1.65, 2.1, 2.7].map((e) => (
          <path key={`a${e}`} d={MANCHA} transform={`translate(630 215) scale(${e}) translate(-630 -215)`} stroke="currentColor" strokeOpacity={0.12 - e * 0.02 > 0.03 ? 0.12 - e * 0.02 : 0.035} strokeWidth="1" vectorEffect="non-scaling-stroke" />
        ))}
        {[0.4, 0.75, 1.1, 1.5, 2].map((e) => (
          <path key={`b${e}`} d={MANCHA_2} transform={`translate(150 650) scale(${e}) translate(-150 -650)`} stroke="currentColor" strokeOpacity={0.09 - e * 0.02 > 0.03 ? 0.09 - e * 0.02 : 0.03} strokeWidth="1" vectorEffect="non-scaling-stroke" />
        ))}
      </svg>

      {/* Marca */}
      <div className="relative z-10 flex items-center gap-2.5">
        <img src="/logo-css-white.png" alt="CSS Brasil" className="size-10 shrink-0 object-contain" />
        <div className="leading-tight">
          <p className="text-base font-extrabold tracking-tight text-white">CSS Brasil</p>
          <p className="text-[11px] font-semibold uppercase tracking-wider text-white/65">CRM Comercial</p>
        </div>
      </div>

      {/* A rota: estrada, brilho que percorre, veículo e as paradas da jornada do lead. */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 bottom-0 top-[26%] flex items-center justify-center px-6 pb-4">
        <div className="relative h-full max-w-full" style={{ aspectRatio: "600 / 760" }}>
          <svg viewBox="0 0 600 760" className="absolute inset-0 size-full overflow-visible" fill="none">
            <defs>
              <linearGradient id="rota-fade" x1="0" y1="1" x2="1" y2="0">
                <stop offset="0" stopColor="#fff" stopOpacity="0.25" />
                <stop offset="1" stopColor="#fff" stopOpacity="1" />
              </linearGradient>
            </defs>
            <path d={CAMINHO} stroke="#fff" strokeOpacity="0.1" strokeWidth="22" strokeLinecap="round" />
            <path id="rota-caminho" className="rota-traco" d={CAMINHO} pathLength={1000} stroke="url(#rota-fade)" strokeWidth="2.5" strokeLinecap="round" />
            <path className="rota-brilho" d={CAMINHO} pathLength={1000} stroke="#a6c3e0" strokeWidth="5" strokeLinecap="round" strokeDasharray="70 930" />
            <circle cx="40" cy="660" r="5" fill="#fff" fillOpacity="0.55" />
            {PARADAS.map((pa, i) => (
              <g key={pa.rotulo}>
                <circle className="rota-pulso" cx={pa.x} cy={pa.y} r="11" stroke="#a6c3e0" strokeWidth="1.5" style={{ animationDelay: `${i * 0.9}s` }} />
                <circle cx={pa.x} cy={pa.y} r="7" fill="#04264c" stroke="#fff" strokeWidth="2.5" />
              </g>
            ))}
            {/* Veículo percorrendo a rota (parado na última parada com "reduzir movimento"). */}
            <g>
              <rect x="-9" y="-9" width="18" height="18" rx="5" fill="#fff" />
              <path d="M -2.5 -3.5 L 3 0 L -2.5 3.5" stroke="#07428b" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
              {reduzir ? (
                <animateTransform attributeName="transform" type="translate" values="400 250" dur="1s" fill="freeze" />
              ) : (
                <animateMotion dur="16s" repeatCount="indefinite" rotate="auto" keyPoints="0;1" keyTimes="0;1" calcMode="spline" keySplines="0.45 0 0.2 1">
                  <mpath href="#rota-caminho" />
                </animateMotion>
              )}
            </g>
          </svg>
          {PARADAS.map((pa) => (
            <span
              key={pa.rotulo}
              className="absolute whitespace-nowrap rounded-full border border-white/25 bg-white/10 px-3 py-1 text-xs font-semibold text-white"
              style={{
                left: `${(pa.x / 600) * 100}%`,
                top: `${(pa.y / 760) * 100}%`,
                transform: pa.lado === "dir" ? "translate(18px, -50%)" : pa.lado === "esq" ? "translate(calc(-100% - 18px), -50%)" : "translate(-50%, 18px)",
              }}
            >
              {pa.rotulo}
            </span>
          ))}
        </div>
      </div>

      {/* Mensagem */}
      <div className="relative z-10 mt-12 max-w-sm">
        <h1 className="text-[2rem] font-semibold leading-[1.1] tracking-tight text-white [text-wrap:balance]">
          Gestão comercial completa para sua consultoria.
        </h1>
        <p className="mt-4 max-w-xs text-sm leading-relaxed text-white/75">
          Acompanhe leads, propostas e o desempenho da sua equipe em um só lugar.
        </p>
      </div>
    </div>
  );
}

/** Só o e-mail fica guardado (nunca a senha), para a pessoa não digitar de novo no mesmo aparelho. */
const CHAVE_ULTIMO_EMAIL = "login:ultimo-email";

function lerUltimoEmail(): string {
  try {
    return localStorage.getItem(CHAVE_ULTIMO_EMAIL) ?? "";
  } catch {
    return "";
  }
}

function gravarUltimoEmail(email: string) {
  try {
    localStorage.setItem(CHAVE_ULTIMO_EMAIL, email);
  } catch {
    /* sem storage (aba anônima/bloqueada): só não lembra o e-mail */
  }
}

/** Mensagem do erro de login: específica quando o servidor/rede explica, genérica nos demais casos (nunca revela se o e-mail existe). */
export function mensagemDeErroDoLogin(e: unknown): string {
  if (e instanceof ApiRequestError && e.codigo === "senha_inicial") return e.message;
  if (isUnauthorized(e)) return "E-mail ou senha inválidos.";
  if (e instanceof ApiRequestError && e.status === 0) return e.message;
  if (e instanceof ApiRequestError && e.status === 429) return "Muitas tentativas seguidas. Aguarde um minuto e tente de novo.";
  return "Não foi possível entrar. Tente novamente.";
}

export function LoginPage() {
  const { sessao, login, loginDoisFatores } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState(lerUltimoEmail);
  const [senha, setSenha] = useState("");
  const [capsLigado, setCapsLigado] = useState(false);
  const [campoInvalido, setCampoInvalido] = useState<"email" | "senha" | null>(null);
  const emailRef = useRef<HTMLInputElement>(null);
  const senhaRef = useRef<HTMLInputElement>(null);
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

  // Já com o cursor no campo certo (com e-mail lembrado, direto na senha). No celular não: o teclado abriria sozinho e cobriria a tela.
  useEffect(() => {
    if (window.matchMedia?.("(pointer: coarse)").matches) return;
    (email ? senhaRef : emailRef).current?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (sessao) return <Navigate to={sessao.areaInicial} replace />;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    setCampoInvalido(null);

    // Conferência antes de ir ao servidor: diz o que falta e já leva o cursor para lá.
    const emailLimpo = email.trim();
    if (!emailLimpo || !senha) {
      const faltaEmail = !emailLimpo;
      setErro(faltaEmail ? "Informe o seu e-mail." : "Informe a sua senha.");
      setCampoInvalido(faltaEmail ? "email" : "senha");
      (faltaEmail ? emailRef : senhaRef).current?.focus();
      return;
    }

    setEnviando(true);
    try {
      const resultado = await login(emailLimpo, senha, manterConectado);
      gravarUltimoEmail(emailLimpo);
      if (resultado.requerDoisFatores) {
        setAguardando2fa(true);
      } else {
        navigate(resultado.sessao!.areaInicial, { replace: true });
      }
    } catch (e) {
      setErro(mensagemDeErroDoLogin(e));
      // Senha errada: limpa e devolve o cursor para digitar de novo (o e-mail fica).
      if (isUnauthorized(e) || (e instanceof ApiRequestError && e.codigo === "senha_inicial")) {
        setSenha("");
        senhaRef.current?.focus();
      }
    } finally {
      setEnviando(false);
    }
  }

  async function enviarCodigo2fa(codigo: string) {
    if (enviando || !codigo.trim()) return;
    setErro(null);
    setEnviando(true);
    try {
      const resultado = await loginDoisFatores(codigo.trim(), manterConectado, usarCodigoRecuperacao);
      navigate(resultado.areaInicial, { replace: true });
    } catch (e) {
      setErro(isUnauthorized(e) ? "Código inválido." : e instanceof ApiRequestError && e.status === 0 ? e.message : "Não foi possível entrar. Tente novamente.");
      setCodigo2fa("");
    } finally {
      setEnviando(false);
    }
  }

  function handleSubmit2fa(e: FormEvent) {
    e.preventDefault();
    void enviarCodigo2fa(codigo2fa);
  }

  /** Código do aplicativo: só dígitos (cola "123 456" e vira 123456) e, com os 6 preenchidos, entra sem precisar clicar. */
  function aoDigitarCodigo(valor: string) {
    if (usarCodigoRecuperacao) {
      setCodigo2fa(valor);
      return;
    }
    const digitos = valor.replace(/\D/g, "").slice(0, 6);
    setCodigo2fa(digitos);
    if (digitos.length === 6) void enviarCodigo2fa(digitos);
  }

  function voltarParaCredenciais() {
    setAguardando2fa(false);
    setCodigo2fa("");
    setUsarCodigoRecuperacao(false);
    setErro(null);
  }

  function abrirEsqueciSenha(e: React.MouseEvent) {
    e.preventDefault();
    setEmailRecuperacao(email.trim());
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
    <div className="flex min-h-screen items-center justify-center p-0 lg:p-8">
    <div className="ui-surface flex w-full max-w-[72rem] overflow-hidden bg-[var(--surface)] lg:min-h-[44rem] lg:rounded-[2rem] lg:border lg:border-[var(--border)] lg:p-3 lg:shadow-[var(--shadow-card)]">
      <PainelDaMarca />

      {/* Painel direito: formulário */}
      <div className="relative flex w-full flex-col items-center justify-center px-6 py-12 lg:w-[45%] lg:px-12">
        <div className="absolute right-4 top-4 lg:right-6 lg:top-6"><SeletorDeTema /></div>
        <div className="w-full max-w-[22rem]">
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
                      <label htmlFor="email-recuperacao" className="mb-1.5 block text-sm font-medium text-[var(--fg)]">
                        E-mail
                      </label>
                      <div className="relative">
                        <Mail className="pointer-events-none absolute left-4 top-1/2 size-[1.125rem] -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                        <input
                          id="email-recuperacao"
                          type="email"
                          autoComplete="username"
                          autoFocus
                          required
                          value={emailRecuperacao}
                          onChange={(e) => setEmailRecuperacao(e.target.value)}
                          className="focus-ring w-full rounded-xl border border-[var(--border)] aria-invalid:border-[var(--danger)] bg-[var(--field)] h-12 pl-11 pr-3 text-[15px] text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
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
                  <label htmlFor="codigo2fa" className="mb-1.5 block text-sm font-medium text-[var(--fg)]">
                    {usarCodigoRecuperacao ? "Código de recuperação" : "Código de 6 dígitos"}
                  </label>
                  <div className="relative">
                    <KeyRound className="pointer-events-none absolute left-4 top-1/2 size-[1.125rem] -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                    <input
                      id="codigo2fa"
                      type="text"
                      inputMode={usarCodigoRecuperacao ? "text" : "numeric"}
                      autoComplete="one-time-code"
                      autoFocus
                      required
                      value={codigo2fa}
                      onChange={(e) => aoDigitarCodigo(e.target.value)}
                      maxLength={usarCodigoRecuperacao ? 32 : 7}
                      className="focus-ring w-full rounded-xl border border-[var(--border)] aria-invalid:border-[var(--danger)] bg-[var(--field)] h-12 pl-11 pr-3 text-[15px] tracking-widest text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
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
              <h2 className="text-[2rem] font-extrabold leading-tight tracking-tight text-[var(--fg)]">Entrar</h2>
              <p className="mt-2 mb-8 text-[15px] text-[var(--fg-muted)]">Entre com sua conta para acessar o CRM comercial.</p>

              <form onSubmit={handleSubmit} className="space-y-4" noValidate>
                <div>
                  <label htmlFor="email" className="mb-1.5 block text-sm font-medium text-[var(--fg)]">
                    E-mail
                  </label>
                  <div className="relative">
                    <Mail className="pointer-events-none absolute left-4 top-1/2 size-[1.125rem] -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                    <input
                      id="email"
                      ref={emailRef}
                      type="email"
                      inputMode="email"
                      autoComplete="username"
                      autoCapitalize="none"
                      autoCorrect="off"
                      spellCheck={false}
                      required
                      aria-invalid={campoInvalido === "email"}
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      className="focus-ring w-full rounded-xl border border-[var(--border)] aria-invalid:border-[var(--danger)] bg-[var(--field)] h-12 pl-11 pr-3 text-[15px] text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
                      placeholder="voce@empresa.com"
                    />
                  </div>
                </div>

                <div>
                  <label htmlFor="senha" className="mb-1.5 block text-sm font-medium text-[var(--fg)]">
                    Senha
                  </label>
                  <div className="relative">
                    <Lock className="pointer-events-none absolute left-4 top-1/2 size-[1.125rem] -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
                    <input
                      id="senha"
                      ref={senhaRef}
                      type={mostrarSenha ? "text" : "password"}
                      autoComplete="current-password"
                      required
                      aria-invalid={campoInvalido === "senha"}
                      aria-describedby={capsLigado ? "aviso-caps" : undefined}
                      value={senha}
                      onChange={(e) => setSenha(e.target.value)}
                      onKeyDown={(e) => setCapsLigado(e.getModifierState("CapsLock"))}
                      onKeyUp={(e) => setCapsLigado(e.getModifierState("CapsLock"))}
                      onBlur={() => setCapsLigado(false)}
                      className="focus-ring w-full rounded-xl border border-[var(--border)] aria-invalid:border-[var(--danger)] bg-[var(--field)] h-12 pl-11 pr-11 text-[15px] text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
                      placeholder="••••••••"
                    />
                    <button
                      type="button"
                      onClick={() => setMostrarSenha((v) => !v)}
                      aria-label={mostrarSenha ? "Ocultar senha" : "Mostrar senha"}
                      className="focus-ring absolute right-4 top-1/2 -translate-y-1/2 rounded text-[var(--fg-muted)] hover:text-[var(--fg)] cursor-pointer"
                    >
                      {mostrarSenha ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
                    </button>
                  </div>
                  {capsLigado && (
                    <p id="aviso-caps" role="status" className="mt-1.5 flex items-center gap-1.5 text-xs font-medium text-[var(--warning)]">
                      <AlertTriangle className="size-3.5 shrink-0" aria-hidden /> Caps Lock está ligado
                    </p>
                  )}
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

                <Button type="submit" className="group !h-12 w-full !text-[15px]" loading={enviando}>
                  Entrar
                  <ArrowRight className="size-4 transition-transform duration-200 group-hover:translate-x-0.5" aria-hidden />
                </Button>
              </form>
              <p className="mt-6 text-center text-xs text-[var(--fg-muted)]">Problemas para entrar? Fale com o administrador do sistema.</p>
            </>
          )}
        </div>
      </div>
    </div>
    </div>
  );
}
