import { Search, AlertTriangle, Check, CheckCircle2, ChevronDown, ChevronLeft, ChevronRight, Info, Loader2, X, XCircle } from "lucide-react";
import {
  Children,
  createContext,
  isValidElement,
  forwardRef,
  useCallback,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type ChangeEvent,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from "react";
import { createPortal } from "react-dom";
import { paraBusca } from "../lib/busca";
import { useFotoDoUsuario } from "../lib/useFotosUsuarios";

// --- Botões ---

type ButtonVariant = "primary" | "secondary" | "ghost" | "danger" | "dangerSoft";
type ButtonSize = "sm" | "md";

// Botões em pílula: o principal ganha degradê, brilho interno e sombra na própria cor (sobe um pouco no hover); o secundário é
// um contorno claro que ganha a cor da marca; o fantasma só aparece no hover. Todos encolhem levemente ao clicar.
const variantClasses: Record<ButtonVariant, string> = {
  primary:
    "bg-gradient-to-b from-[color-mix(in_srgb,var(--brand)_88%,white)] to-[var(--brand)] text-[var(--brand-fg)] shadow-[inset_0_1px_0_rgb(255_255_255/0.22),0_1px_2px_rgb(0_0_0/0.14),0_6px_14px_-7px_var(--brand)] hover:-translate-y-px hover:shadow-[inset_0_1px_0_rgb(255_255_255/0.26),0_1px_2px_rgb(0_0_0/0.14),0_10px_20px_-8px_var(--brand)] disabled:translate-y-0 disabled:opacity-50 disabled:shadow-none",
  secondary:
    "border border-[var(--border)] bg-[var(--surface)] text-[var(--fg)] shadow-[0_1px_2px_rgb(0_0_0/0.05)] hover:border-[color-mix(in_srgb,var(--brand)_45%,var(--border))] hover:bg-[var(--brand-soft)] hover:text-[var(--brand)] disabled:opacity-50 disabled:hover:border-[var(--border)] disabled:hover:bg-[var(--surface)] disabled:hover:text-[var(--fg)]",
  ghost: "text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)] disabled:opacity-50 disabled:hover:bg-transparent",
  danger:
    "bg-gradient-to-b from-[color-mix(in_srgb,var(--danger)_88%,white)] to-[var(--danger)] text-white shadow-[inset_0_1px_0_rgb(255_255_255/0.22),0_1px_2px_rgb(0_0_0/0.14),0_6px_14px_-7px_var(--danger)] hover:-translate-y-px disabled:translate-y-0 disabled:opacity-50 disabled:shadow-none",
  /** Ação destrutiva que só abre uma confirmação (listas): fundo suave em vez do vermelho cheio. */
  dangerSoft:
    "border border-[color-mix(in_srgb,var(--danger)_25%,transparent)] bg-[var(--danger-soft)] text-[var(--danger)] hover:border-[var(--danger)] hover:bg-[var(--danger)] hover:text-white disabled:opacity-50",
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: "h-8 px-3.5 text-[13px] gap-1.5",
  md: "h-10 px-5 text-sm gap-2",
};

export const Button = forwardRef<
  HTMLButtonElement,
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant; size?: ButtonSize; loading?: boolean }
>(({ variant = "primary", size = "md", loading, className = "", children, disabled, ...props }, ref) => (
  <button
    ref={ref}
    disabled={disabled || loading}
    className={`focus-ring inline-flex items-center justify-center whitespace-nowrap rounded-full font-semibold transition-all duration-200 active:scale-[0.97] cursor-pointer disabled:cursor-not-allowed disabled:active:scale-100 ${variantClasses[variant]} ${sizeClasses[size]} ${className}`}
    {...props}
  >
    {loading && <Loader2 className="size-4 animate-spin" aria-hidden />}
    {children}
  </button>
));
Button.displayName = "Button";

export function IconButton({
  label,
  className = "",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { label: string }) {
  return (
    <button
      aria-label={label}
      title={label}
      className={`focus-ring inline-flex size-9 items-center justify-center rounded-full text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)] cursor-pointer ${className}`}
      {...props}
    />
  );
}

// --- Avatar (foto do usuário, com iniciais como fallback) ---

/** Círculo com a foto de perfil ou, sem foto, as iniciais. Com `usuarioId`, a foto é buscada pelo id quando a tela só tem o nome. */
export function Avatar({ nome, fotoUrl, usuarioId, className = "size-9 text-sm" }: { nome: string; fotoUrl?: string | null; usuarioId?: string | null; className?: string }) {
  const [erroAoCarregar, setErroAoCarregar] = useState(false);
  const fotoDoUsuario = useFotoDoUsuario(fotoUrl ? null : usuarioId);
  fotoUrl = fotoUrl ?? fotoDoUsuario;
  const iniciais = nome
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join("");

  if (fotoUrl && !erroAoCarregar) {
    return <img src={fotoUrl} alt={nome} onError={() => setErroAoCarregar(true)} className={`shrink-0 rounded-full object-cover ${className}`} />;
  }

  return (
    <div className={`flex shrink-0 items-center justify-center rounded-full bg-[var(--brand)] font-semibold text-white ${className}`}>
      {iniciais || "?"}
    </div>
  );
}

// --- Cartão ---

export function Card({ className = "", children, style, ...resto }: { className?: string; children: ReactNode; style?: React.CSSProperties } & Omit<React.HTMLAttributes<HTMLDivElement>, "className" | "style" | "children">) {
  return (
    <div {...resto} style={style} className={`card-anim ui-surface rounded-[var(--radius-card)] border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)] ${className}`}>{children}</div>
  );
}

// --- Badge ---

type BadgeVariant = "neutral" | "success" | "warning" | "danger" | "info" | "brand";

const badgeClasses: Record<BadgeVariant, string> = {
  neutral: "bg-[var(--surface-hover)] text-[var(--fg-muted)]",
  success: "bg-[var(--success-soft)] text-[var(--success)]",
  warning: "bg-[var(--warning-soft)] text-[var(--warning)]",
  danger: "bg-[var(--danger-soft)] text-[var(--danger)]",
  info: "bg-[var(--brand-soft)] text-[var(--info)]",
  brand: "bg-[var(--brand-soft)] text-[var(--brand)]",
};

export function Badge({ variant = "neutral", children }: { variant?: BadgeVariant; children: ReactNode }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[11px] font-medium ${badgeClasses[variant]}`}>
      {children}
    </span>
  );
}

// --- Campos de formulário ---

export function Label({ children, htmlFor, required }: { children: ReactNode; htmlFor?: string; required?: boolean }) {
  return (
    <label htmlFor={htmlFor} className="mb-1 block text-sm font-medium text-[var(--fg)]">
      {children} {required && <span className="text-[var(--danger)]">*</span>}
    </label>
  );
}

export function FieldError({ children }: { children?: ReactNode }) {
  if (!children) return null;
  return <p className="mt-1 text-xs text-[var(--danger)]">{children}</p>;
}

const fieldBase =
  "focus-ring w-full rounded-lg border border-[var(--border)] bg-[var(--field)] px-3 h-9 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)] disabled:opacity-50";

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(({ className = "", ...props }, ref) => (
  <input ref={ref} className={`${fieldBase} ${className}`} {...props} />
));
Input.displayName = "Input";

const formatoMoedaInput = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });

export const MoneyInput = forwardRef<
  HTMLInputElement,
  Omit<InputHTMLAttributes<HTMLInputElement>, "value" | "onChange" | "type"> & {
    value: number | null | undefined;
    onChange: (valor: number | null) => void;
  }
>(({ value, onChange, className = "", ...props }, ref) => {
  const [texto, setTexto] = useState(() => (value != null ? formatoMoedaInput.format(value) : ""));

  useEffect(() => {
    setTexto((atual) => {
      const digitosAtuais = atual.replace(/\D/g, "");
      const numeroAtual = digitosAtuais ? Number(digitosAtuais) / 100 : null;
      if (numeroAtual === (value ?? null)) return atual;
      return value != null ? formatoMoedaInput.format(value) : "";
    });
  }, [value]);

  function handleChange(e: ChangeEvent<HTMLInputElement>) {
    const digitos = e.target.value.replace(/\D/g, "");
    if (!digitos) {
      setTexto("");
      onChange(null);
      return;
    }
    const numero = Number(digitos) / 100;
    setTexto(formatoMoedaInput.format(numero));
    onChange(numero);
  }

  return (
    <input
      ref={ref}
      inputMode="numeric"
      className={`${fieldBase} ${className}`}
      value={texto}
      onChange={handleChange}
      {...props}
    />
  );
});
MoneyInput.displayName = "MoneyInput";

function mascararCpf(digitos: string) {
  const d = digitos.slice(0, 11);
  const p1 = d.slice(0, 3);
  const p2 = d.slice(3, 6);
  const p3 = d.slice(6, 9);
  const p4 = d.slice(9, 11);
  let texto = p1;
  if (p2) texto += `.${p2}`;
  if (p3) texto += `.${p3}`;
  if (p4) texto += `-${p4}`;
  return texto;
}

function mascararCnpj(digitos: string) {
  const d = digitos.slice(0, 14);
  const p1 = d.slice(0, 2);
  const p2 = d.slice(2, 5);
  const p3 = d.slice(5, 8);
  const p4 = d.slice(8, 12);
  const p5 = d.slice(12, 14);
  let texto = p1;
  if (p2) texto += `.${p2}`;
  if (p3) texto += `.${p3}`;
  if (p4) texto += `/${p4}`;
  if (p5) texto += `-${p5}`;
  return texto;
}

/** Máscara de CPF (000.000.000-00), aplicada durante a digitação. Valor mantido como dígitos puros. */
export const CpfInput = forwardRef<HTMLInputElement, Omit<InputHTMLAttributes<HTMLInputElement>, "type">>(
  ({ className = "", onChange, value, ...props }, ref) => {
    function handleChange(e: ChangeEvent<HTMLInputElement>) {
      const digitos = e.target.value.replace(/\D/g, "").slice(0, 11);
      e.target.value = digitos;
      onChange?.(e);
    }

    return (
      <input
        ref={ref}
        inputMode="numeric"
        className={`${fieldBase} ${className}`}
        value={mascararCpf(String(value ?? "").replace(/\D/g, ""))}
        onChange={handleChange}
        {...props}
      />
    );
  }
);
CpfInput.displayName = "CpfInput";

/**
 * Máscara de documento (CPF ou CNPJ), aplicada conforme a quantidade de dígitos digitados:
 * até 11 dígitos usa o padrão de CPF, mais que isso passa a usar o padrão de CNPJ.
 * Valor mantido como dígitos puros.
 */
export const DocumentoInput = forwardRef<HTMLInputElement, Omit<InputHTMLAttributes<HTMLInputElement>, "type">>(
  ({ className = "", onChange, value, ...props }, ref) => {
    function handleChange(e: ChangeEvent<HTMLInputElement>) {
      const digitos = e.target.value.replace(/\D/g, "").slice(0, 14);
      e.target.value = digitos;
      onChange?.(e);
    }

    const digitosAtuais = String(value ?? "").replace(/\D/g, "");

    return (
      <input
        ref={ref}
        inputMode="numeric"
        className={`${fieldBase} ${className}`}
        value={digitosAtuais.length > 11 ? mascararCnpj(digitosAtuais) : mascararCpf(digitosAtuais)}
        onChange={handleChange}
        {...props}
      />
    );
  }
);
DocumentoInput.displayName = "DocumentoInput";

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ className = "", ...props }, ref) => (
    <textarea ref={ref} className={`${fieldBase} h-auto min-h-24 py-2 ${className}`} {...props} />
  )
);
Textarea.displayName = "Textarea";

/** Celular e tablet (ponteiro por toque): aí o teclado virtual aparece ao focar um campo de texto. */
function toqueFino(): boolean {
  return typeof window !== "undefined" && window.matchMedia?.("(pointer: coarse)").matches === true;
}

type OpcaoDoSelect = { valor: string; rotulo: string; desabilitada: boolean };

/** Lê as `<option>` (também dentro de fragmentos, listas e `<optgroup>`) dos filhos do Select. */
function lerOpcoes(filhos: ReactNode): OpcaoDoSelect[] {
  const opcoes: OpcaoDoSelect[] = [];
  Children.forEach(filhos, (filho) => {
    if (!isValidElement(filho)) return;
    const props = filho.props as { value?: string | number; disabled?: boolean; children?: ReactNode };
    if (filho.type === "option") {
      const rotulo = Children.toArray(props.children).join("");
      opcoes.push({ valor: String(props.value ?? rotulo), rotulo, desabilitada: !!props.disabled });
    } else if (props.children !== undefined) {
      opcoes.push(...lerOpcoes(props.children));
    }
  });
  return opcoes;
}

/**
 * Lista de escolha com pesquisa: abre uma lista com um campo para digitar e filtrar as opções (sem diferenciar acento nem maiúscula).
 * Usa as mesmas `<option>` e o mesmo `onChange` do `<select>` nativo, então os formulários e filtros não mudam.
 */
export function Select({
  className = "",
  children,
  value,
  onChange,
  disabled,
  id,
  name,
  title,
  ...resto
}: SelectHTMLAttributes<HTMLSelectElement>) {
  const [aberto, setAberto] = useState(false);
  const [busca, setBusca] = useState("");
  const [destaque, setDestaque] = useState(0);
  const [posicao, setPosicao] = useState<{ left: number; width: number; top?: number; bottom?: number; maxHeight: number } | null>(null);
  const botaoRef = useRef<HTMLButtonElement>(null);
  const painelRef = useRef<HTMLDivElement>(null);
  const buscaRef = useRef<HTMLInputElement>(null);
  const listaId = useId();

  const opcoes = lerOpcoes(children);
  const atual = String(value ?? "");
  const escolhida = opcoes.find((o) => o.valor === atual);
  const termo = paraBusca(busca);
  const visiveis = termo ? opcoes.filter((o) => paraBusca(o.rotulo).includes(termo)) : opcoes;

  function abrir() {
    const retangulo = botaoRef.current?.getBoundingClientRect();
    if (!retangulo) return;
    const abaixo = window.innerHeight - retangulo.bottom - 8;
    const acima = retangulo.top - 8;
    // Sem espaço embaixo (e mais espaço em cima): a lista abre para cima.
    const paraCima = abaixo < 240 && acima > abaixo;
    setPosicao({
      left: retangulo.left,
      width: Math.max(retangulo.width, 180),
      ...(paraCima ? { bottom: window.innerHeight - retangulo.top + 4 } : { top: retangulo.bottom + 4 }),
      maxHeight: Math.min(320, Math.max(160, paraCima ? acima : abaixo)),
    });
    setBusca("");
    setDestaque(Math.max(0, opcoes.findIndex((o) => o.valor === atual)));
    setAberto(true);
  }

  useEffect(() => {
    if (!aberto) return;
    // Celular/tablet: focar o campo de pesquisa ao abrir levanta o teclado, que redimensiona a janela e rola a página — e a lista fechava sozinha logo
    // ao abrir. No toque, o campo só recebe foco quando a pessoa toca nele.
    const toque = toqueFino();
    if (!toque) buscaRef.current?.focus();
    const larguraAoAbrir = window.innerWidth;
    const abertoEm = Date.now();
    const fora = (e: Event) => {
      const alvo = e.target as Node;
      if (!painelRef.current?.contains(alvo) && !botaoRef.current?.contains(alvo)) setAberto(false);
    };
    // A lista é fixa na tela: rolar a página (fora da lista) ou virar a tela fecha, para ela não ficar solta do campo. O teclado virtual muda só a ALTURA da
    // janela e rola a página para mostrar o campo: isso não fecha. No toque a rolagem não fecha (o teclado e o próprio toque a provocam).
    const fecharRolagem = (e: Event) => {
      if (toque || Date.now() - abertoEm < 600) return;
      if (e.target instanceof Node && painelRef.current?.contains(e.target)) return;
      setAberto(false);
    };
    const fecharRedimensionar = () => {
      if (window.innerWidth !== larguraAoAbrir) setAberto(false);
    };
    document.addEventListener("mousedown", fora);
    document.addEventListener("touchstart", fora, { passive: true });
    window.addEventListener("scroll", fecharRolagem, true);
    window.addEventListener("resize", fecharRedimensionar);
    return () => {
      document.removeEventListener("mousedown", fora);
      document.removeEventListener("touchstart", fora);
      window.removeEventListener("scroll", fecharRolagem, true);
      window.removeEventListener("resize", fecharRedimensionar);
    };
  }, [aberto]);

  function escolher(opcao: OpcaoDoSelect) {
    if (opcao.desabilitada) return;
    setAberto(false);
    botaoRef.current?.focus();
    if (opcao.valor === atual) return;
    const alvo = { value: opcao.valor, name: name ?? "", id: id ?? "" };
    onChange?.({ target: alvo, currentTarget: alvo } as unknown as ChangeEvent<HTMLSelectElement>);
  }

  function teclado(e: React.KeyboardEvent) {
    if (e.key === "Escape") {
      e.stopPropagation();
      setAberto(false);
      botaoRef.current?.focus();
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      setDestaque((d) => Math.min(d + 1, visiveis.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setDestaque((d) => Math.max(d - 1, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      const opcao = visiveis[Math.min(destaque, visiveis.length - 1)];
      if (opcao) escolher(opcao);
    }
  }

  return (
    <>
      <button
        ref={botaoRef}
        type="button"
        id={id}
        title={title}
        disabled={disabled}
        aria-label={resto["aria-label"]}
        aria-haspopup="listbox"
        aria-expanded={aberto}
        onClick={() => (aberto ? setAberto(false) : abrir())}
        onKeyDown={(e) => {
          if (!aberto && (e.key === "ArrowDown" || e.key === "ArrowUp")) {
            e.preventDefault();
            abrir();
          }
        }}
        className={`${fieldBase} flex cursor-pointer items-center justify-between gap-2 text-left ${className}`}
      >
        <span className={`truncate ${escolhida && escolhida.valor !== "" ? "" : "text-[var(--fg-muted)]"}`}>{escolhida?.rotulo || " "}</span>
        <ChevronDown className={`size-4 shrink-0 transition-transform duration-300 ease-[cubic-bezier(0.34,1.56,0.64,1)] ${aberto ? "rotate-180 text-[var(--brand)]" : "text-[var(--fg-muted)]"}`} aria-hidden />
      </button>
      {aberto && posicao && createPortal(
        <div
          ref={painelRef}
          style={{ position: "fixed", left: posicao.left, width: posicao.width, top: posicao.top, bottom: posicao.bottom, maxHeight: posicao.maxHeight }}
          data-origem={posicao.bottom !== undefined ? "baixo" : "cima"}
          className="lista-flutuante ui-surface z-[70] flex flex-col overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface-solid)] shadow-xl"
          onKeyDown={teclado}
        >
          <div className="relative border-b border-[var(--border)] p-1.5">
            <Search className="pointer-events-none absolute left-3.5 top-1/2 size-3.5 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
            <input
              ref={buscaRef}
              value={busca}
              onChange={(e) => {
                setBusca(e.target.value);
                setDestaque(0);
              }}
              placeholder="Pesquisar…"
              aria-label="Pesquisar nas opções"
              aria-controls={listaId}
              autoComplete="off"
              className="focus-ring h-8 w-full rounded-md border border-[var(--border)] bg-[var(--field)] pl-7 pr-7 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
            />
            {busca && (
              <button type="button" aria-label="Limpar pesquisa" onClick={() => { setBusca(""); buscaRef.current?.focus(); }} className="focus-ring absolute right-3 top-1/2 flex size-5 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]">
                <X className="size-3" />
              </button>
            )}
          </div>
          <div id={listaId} role="listbox" className="overflow-y-auto p-1">
            {termo && visiveis.length > 0 && <p className="px-2 pb-1 pt-0.5 text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{visiveis.length} de {opcoes.length}</p>}
            {visiveis.length === 0 && <p className="px-2 py-1.5 text-sm text-[var(--fg-muted)]">Nada encontrado</p>}
            {visiveis.map((o, i) => (
              <button
                key={o.valor}
                type="button"
                role="option"
                aria-selected={o.valor === atual}
                disabled={o.desabilitada}
                onClick={() => escolher(o)}
                onMouseEnter={() => setDestaque(i)}
                className={`flex w-full cursor-pointer items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm text-[var(--fg)] transition-[background-color] duration-150 disabled:cursor-not-allowed disabled:opacity-50 ${
                  i === destaque ? "bg-[var(--surface-hover)]" : ""
                }`}
              >
                <span className="truncate">{o.rotulo || " "}</span>
                {o.valor === atual && <Check className="opcao-marca size-4 shrink-0 text-[var(--brand)]" aria-hidden />}
              </button>
            ))}
          </div>
        </div>,
        document.body
      )}
    </>
  );
}

export function Checkbox({ label, ...props }: InputHTMLAttributes<HTMLInputElement> & { label: ReactNode }) {
  const id = useId();
  return (
    <label htmlFor={id} className="flex items-center gap-2 text-sm text-[var(--fg)] cursor-pointer">
      <input id={id} type="checkbox" className="focus-ring size-4 rounded border-[var(--border)]" {...props} />
      {label}
    </label>
  );
}

// --- Estados de tela ---

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`animate-pulse rounded-md bg-[var(--surface-hover)] ${className}`} />;
}

export function Spinner({ className = "size-5" }: { className?: string }) {
  return <Loader2 className={`animate-spin text-[var(--fg-muted)] ${className}`} aria-hidden />;
}

export function EmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-[var(--border)] px-6 py-14 text-center">
      <p className="text-sm font-medium text-[var(--fg)]">{title}</p>
      {description && <p className="max-w-sm text-sm text-[var(--fg-muted)]">{description}</p>}
      {action}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 rounded-xl border border-[var(--danger)]/30 bg-[var(--danger-soft)] px-6 py-10 text-center">
      <XCircle className="size-8 text-[var(--danger)]" aria-hidden />
      <p className="text-sm text-[var(--fg)]">{message}</p>
      {onRetry && (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          Tentar novamente
        </Button>
      )}
    </div>
  );
}

// --- Fundo de modal/gaveta ---

/**
 * Fecha ao clicar no fundo, mas só quando o clique COMEÇOU no fundo. Selecionar um texto dentro do formulário arrastando o mouse até fora da
 * caixa termina o clique no fundo; sem esta checagem o formulário fechava no meio da seleção.
 */
function useFecharPeloFundo(onClose: () => void) {
  const comecouNoFundo = useRef(false);
  return {
    onMouseDown: (e: React.MouseEvent<HTMLElement>) => {
      comecouNoFundo.current = e.target === e.currentTarget;
    },
    onClick: (e: React.MouseEvent<HTMLElement>) => {
      const fechar = comecouNoFundo.current && e.target === e.currentTarget;
      comecouNoFundo.current = false;
      if (fechar) onClose();
    },
  };
}

// --- Modal ---

/**
 * Mantém uma janela na tela por um instante depois de "fechar", para a animação de saída rodar. Devolve se ela ainda deve ser desenhada, se está saindo e
 * o conteúdo mais recente de quando estava aberta (o pai costuma esvaziar o conteúdo ao fechar, e a janela não pode encolher no meio da saída).
 */
function useSaidaAnimada<T>(aberta: boolean, conteudo: T, duracaoMs = 190) {
  const [visivel, setVisivel] = useState(aberta);
  const [saindo, setSaindo] = useState(false);
  const ultimo = useRef(conteudo);
  if (aberta) ultimo.current = conteudo;

  useEffect(() => {
    if (aberta) {
      setVisivel(true);
      setSaindo(false);
      return;
    }
    if (!visivel) return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      setVisivel(false);
      return;
    }
    setSaindo(true);
    const t = window.setTimeout(() => {
      setVisivel(false);
      setSaindo(false);
    }, duracaoMs);
    return () => window.clearTimeout(t);
  }, [aberta, visivel, duracaoMs]);

  return { visivel: aberta || visivel, saindo: !aberta && saindo, conteudo: aberta ? conteudo : ultimo.current };
}

/**
 * Desenha o conteúdo direto no <body>. Janelas, painéis e alertas usam position: fixed, que só vale em relação à TELA se nenhum elemento acima tiver
 * transform, filter, backdrop-filter ou perspective — e as telas do CRM têm: animações de entrada (.entra, .card-anim, .pagina-anim) e o efeito de vidro
 * (.ui-surface). Dentro de um desses, a janela passava a se posicionar em relação ao cartão e aparecia fora da tela, principalmente no celular.
 * Sempre que algo precisar cobrir a tela, renderize por aqui.
 */
export function Portal({ children }: { children: ReactNode }) {
  if (typeof document === "undefined") return null;
  return createPortal(children, document.body);
}

export function Modal({
  open,
  onClose,
  title,
  children,
  footer,
  size = "md",
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  size?: "sm" | "md" | "lg" | "xl";
}) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const fundo = useFecharPeloFundo(onClose);
  const saida = useSaidaAnimada(open, { title, children, footer });
  if (!saida.visivel) return null;
  const { title: tituloExibido, children: filhos, footer: rodape } = saida.conteudo;

  const widths = { sm: "max-w-sm", md: "max-w-lg", lg: "max-w-2xl", xl: "max-w-[min(96vw,80rem)]" };

  return (
    <Portal>
    <div className={`modal-fundo ${saida.saindo ? "saindo" : ""} fixed inset-0 z-50 flex items-center justify-center bg-[var(--overlay)] p-2 backdrop-blur-[2px] sm:p-4`} role="presentation" {...fundo}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={tituloExibido}
        className={`modal-painel ${saida.saindo ? "saindo" : ""} ui-surface max-h-[85vh] w-full ${widths[size]} overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-solid)] shadow-2xl flex flex-col`}
      >
        <div className="flex items-center justify-between border-b border-[var(--border)] px-5 py-4">
          <h2 className="text-base font-semibold text-[var(--fg)]">{tituloExibido}</h2>
          <IconButton label="Fechar" onClick={onClose}>
            <X className="size-4" />
          </IconButton>
        </div>
        <div className="overflow-y-auto px-5 py-4">{filhos}</div>
        {rodape && <div className="flex flex-wrap justify-end gap-2 border-t border-[var(--border)] px-5 py-3">{rodape}</div>}
      </div>
    </div>
    </Portal>
  );
}

// --- Drawer lateral ---

export function Drawer({ open, onClose, title, children }: { open: boolean; onClose: () => void; title: string; children: ReactNode }) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const fundo = useFecharPeloFundo(onClose);
  const saida = useSaidaAnimada(open, { title, children });
  if (!saida.visivel) return null;
  const { title: tituloExibido, children: filhos } = saida.conteudo;

  return (
    <Portal>
    <div className={`modal-fundo ${saida.saindo ? "saindo" : ""} fixed inset-0 z-50 flex justify-end bg-[var(--overlay)]`} role="presentation" {...fundo}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={tituloExibido}
        className={`drawer-painel ${saida.saindo ? "saindo" : ""} ui-surface flex h-full w-full max-w-md flex-col border-l border-[var(--border)] bg-[var(--surface-solid)] shadow-2xl`}
      >
        <div className="flex items-center justify-between border-b border-[var(--border)] px-5 py-4">
          <h2 className="text-base font-semibold text-[var(--fg)]">{tituloExibido}</h2>
          <IconButton label="Fechar" onClick={onClose}>
            <X className="size-4" />
          </IconButton>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">{filhos}</div>
      </div>
    </div>
    </Portal>
  );
}

// --- Confirmação de ação crítica ---

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = "Confirmar",
  danger,
  loading,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  title: string;
  message: ReactNode;
  confirmLabel?: string;
  danger?: boolean;
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={loading}>
            Cancelar
          </Button>
          <Button variant={danger ? "danger" : "primary"} onClick={onConfirm} loading={loading}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      <div className="text-sm text-[var(--fg-muted)]">{message}</div>
    </Modal>
  );
}

// --- Paginação ---

export function Pagination({
  pagina,
  totalPaginas,
  onChange,
}: {
  pagina: number;
  totalPaginas: number;
  onChange: (pagina: number) => void;
}) {
  const raiz = useRef<HTMLDivElement>(null);
  const anterior = useRef(pagina);

  // Ao trocar de página, a lista logo acima (o elemento irmão anterior à paginação) desliza do lado para onde se foi: da direita ao avançar, da esquerda ao voltar.
  // Usa a Web Animations API em vez de uma classe CSS: assim não briga com a animação de entrada que o próprio bloco possa ter (ao tirar a classe, aquela
  // reiniciaria e a lista animaria duas vezes) e uma troca rápida cancela a anterior em vez de empilhar.
  useEffect(() => {
    const antes = anterior.current;
    anterior.current = pagina;
    if (antes === pagina) return;
    const alvo = raiz.current?.previousElementSibling as (HTMLElement & { __animacaoDePagina?: Animation }) | null;
    if (!alvo || typeof alvo.animate !== "function" || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    alvo.__animacaoDePagina?.cancel();
    const lado = pagina > antes ? 28 : -28;
    alvo.__animacaoDePagina = alvo.animate(
      [{ opacity: 0, transform: `translateX(${lado}px)` }, { opacity: 1, transform: "translateX(0)" }],
      { duration: 420, easing: "cubic-bezier(0.22, 1, 0.36, 1)" }
    );
  }, [pagina]);

  if (totalPaginas <= 1) return null;
  return (
    <div ref={raiz} className="flex items-center justify-center gap-2 py-2">
      <IconButton label="Página anterior" disabled={pagina <= 1} onClick={() => onChange(pagina - 1)}>
        <ChevronLeft className="size-4" />
      </IconButton>
      <span key={pagina} className="pagina-numero text-sm text-[var(--fg-muted)]">
        Página {pagina} de {totalPaginas}
      </span>
      <IconButton label="Próxima página" disabled={pagina >= totalPaginas} onClick={() => onChange(pagina + 1)}>
        <ChevronRight className="size-4" />
      </IconButton>
    </div>
  );
}

// --- Abas ---

export function Tabs({
  tabs,
  ativa,
  onChange,
}: {
  tabs: { chave: string; rotulo: string; contagem?: number }[];
  ativa: string;
  onChange: (chave: string) => void;
}) {
  return (
    <div className="flex gap-1 overflow-x-auto border-b border-[var(--border)]" role="tablist">
      {tabs.map((tab) => (
        <button
          key={tab.chave}
          role="tab"
          aria-selected={tab.chave === ativa}
          onClick={() => onChange(tab.chave)}
          className={`focus-ring whitespace-nowrap border-b-2 px-3 py-2 text-sm font-medium cursor-pointer ${
            tab.chave === ativa
              ? "border-[var(--brand)] text-[var(--brand)]"
              : "border-transparent text-[var(--fg-muted)] hover:text-[var(--fg)]"
          }`}
        >
          {tab.rotulo}
          {tab.contagem !== undefined && <span className="ml-1.5 text-xs opacity-70">({tab.contagem})</span>}
        </button>
      ))}
    </div>
  );
}

// --- Toasts ---

interface Toast {
  id: number;
  tipo: "success" | "error" | "info";
  mensagem: string;
}

interface ToastContextValue {
  notificar: (tipo: Toast["tipo"], mensagem: string) => void;
}

const ToastContext = createContext<ToastContextValue | undefined>(undefined);

const toastIcon = { success: CheckCircle2, error: AlertTriangle, info: Info };
const toastClasses: Record<Toast["tipo"], string> = {
  success: "border-[var(--success)]/30 bg-[var(--success-soft)] text-[var(--success)]",
  error: "border-[var(--danger)]/30 bg-[var(--danger-soft)] text-[var(--danger)]",
  info: "border-[var(--brand)]/30 bg-[var(--brand-soft)] text-[var(--brand)]",
};

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  const notificar = useCallback((tipo: Toast["tipo"], mensagem: string) => {
    const id = nextId.current++;
    setToasts((atual) => [...atual, { id, tipo, mensagem }]);
    setTimeout(() => setToasts((atual) => atual.filter((t) => t.id !== id)), 5000);
  }, []);

  return (
    <ToastContext.Provider value={{ notificar }}>
      {children}
      <div className="fixed bottom-4 right-4 z-[100] flex flex-col gap-2">
        {toasts.map((toast) => {
          const Icon = toastIcon[toast.tipo];
          return (
            <div
              key={toast.id}
              role="status"
              className={`flex items-center gap-2 rounded-xl border px-4 py-3 text-sm shadow-lg backdrop-blur ${toastClasses[toast.tipo]}`}
            >
              <Icon className="size-4 shrink-0" aria-hidden />
              {toast.mensagem}
            </div>
          );
        })}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) throw new Error("useToast deve ser usado dentro de ToastProvider");
  return context;
}
