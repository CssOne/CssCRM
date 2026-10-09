import { AlertTriangle, Check, CheckCircle2, ChevronDown, ChevronLeft, ChevronRight, Info, Loader2, X, XCircle } from "lucide-react";
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
import { paraBusca } from "../lib/busca";

// --- Botões ---

type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";
type ButtonSize = "sm" | "md";

const variantClasses: Record<ButtonVariant, string> = {
  primary: "bg-[var(--brand)] text-[var(--brand-fg)] hover:opacity-90 disabled:opacity-50",
  secondary:
    "bg-[var(--surface)] text-[var(--fg)] border border-[var(--border)] hover:bg-[var(--surface-hover)] disabled:opacity-50",
  ghost: "text-[var(--fg)] hover:bg-[var(--surface-hover)] disabled:opacity-50",
  danger: "bg-[var(--danger)] text-white hover:opacity-90 disabled:opacity-50",
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: "h-8 px-3 text-sm gap-1.5",
  md: "h-10 px-4 text-sm gap-2",
};

export const Button = forwardRef<
  HTMLButtonElement,
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant; size?: ButtonSize; loading?: boolean }
>(({ variant = "primary", size = "md", loading, className = "", children, disabled, ...props }, ref) => (
  <button
    ref={ref}
    disabled={disabled || loading}
    className={`focus-ring inline-flex items-center justify-center rounded-lg font-medium transition-colors cursor-pointer disabled:cursor-not-allowed ${variantClasses[variant]} ${sizeClasses[size]} ${className}`}
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
      className={`focus-ring inline-flex size-9 items-center justify-center rounded-lg text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)] cursor-pointer ${className}`}
      {...props}
    />
  );
}

// --- Avatar (foto do usuário, com iniciais como fallback) ---

export function Avatar({ nome, fotoUrl, className = "size-9 text-sm" }: { nome: string; fotoUrl?: string | null; className?: string }) {
  const [erroAoCarregar, setErroAoCarregar] = useState(false);
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

export function Card({ className = "", children }: { className?: string; children: ReactNode }) {
  return (
    <div className={`rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-sm ${className}`}>{children}</div>
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
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${badgeClasses[variant]}`}>
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
  "focus-ring w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 h-10 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)] disabled:opacity-50";

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
    buscaRef.current?.focus();
    const fora = (e: MouseEvent) => {
      const alvo = e.target as Node;
      if (!painelRef.current?.contains(alvo) && !botaoRef.current?.contains(alvo)) setAberto(false);
    };
    // A lista é fixa na tela: rolar a página (fora da lista) ou redimensionar a janela fecha, para ela não ficar solta do campo.
    const fechar = (e: Event) => {
      if (e.target instanceof Node && painelRef.current?.contains(e.target)) return;
      setAberto(false);
    };
    document.addEventListener("mousedown", fora);
    window.addEventListener("scroll", fechar, true);
    window.addEventListener("resize", fechar);
    return () => {
      document.removeEventListener("mousedown", fora);
      window.removeEventListener("scroll", fechar, true);
      window.removeEventListener("resize", fechar);
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
        <ChevronDown className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />
      </button>
      {aberto && posicao && (
        <div
          ref={painelRef}
          style={{ position: "fixed", left: posicao.left, width: posicao.width, top: posicao.top, bottom: posicao.bottom, maxHeight: posicao.maxHeight }}
          className="z-[70] flex flex-col overflow-hidden rounded-lg border border-[var(--border)] bg-[var(--surface)] shadow-lg"
          onKeyDown={teclado}
        >
          <div className="border-b border-[var(--border)] p-1.5">
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
              className="focus-ring h-8 w-full rounded-md border border-[var(--border)] bg-[var(--bg)] px-2 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
            />
          </div>
          <div id={listaId} role="listbox" className="overflow-y-auto p-1">
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
                className={`flex w-full cursor-pointer items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm text-[var(--fg)] disabled:cursor-not-allowed disabled:opacity-50 ${
                  i === destaque ? "bg-[var(--surface-hover)]" : ""
                }`}
              >
                <span className="truncate">{o.rotulo || " "}</span>
                {o.valor === atual && <Check className="size-4 shrink-0 text-[var(--brand)]" aria-hidden />}
              </button>
            ))}
          </div>
        </div>
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
  size?: "sm" | "md" | "lg";
}) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const fundo = useFecharPeloFundo(onClose);
  if (!open) return null;

  const widths = { sm: "max-w-sm", md: "max-w-lg", lg: "max-w-2xl" };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" role="presentation" {...fundo}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`max-h-[85vh] w-full ${widths[size]} overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface)] shadow-2xl flex flex-col`}
      >
        <div className="flex items-center justify-between border-b border-[var(--border)] px-5 py-4">
          <h2 className="text-base font-semibold text-[var(--fg)]">{title}</h2>
          <IconButton label="Fechar" onClick={onClose}>
            <X className="size-4" />
          </IconButton>
        </div>
        <div className="overflow-y-auto px-5 py-4">{children}</div>
        {footer && <div className="flex justify-end gap-2 border-t border-[var(--border)] px-5 py-3">{footer}</div>}
      </div>
    </div>
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
  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/40" role="presentation" {...fundo}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="flex h-full w-full max-w-md flex-col border-l border-[var(--border)] bg-[var(--surface)] shadow-2xl"
      >
        <div className="flex items-center justify-between border-b border-[var(--border)] px-5 py-4">
          <h2 className="text-base font-semibold text-[var(--fg)]">{title}</h2>
          <IconButton label="Fechar" onClick={onClose}>
            <X className="size-4" />
          </IconButton>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
      </div>
    </div>
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
  if (totalPaginas <= 1) return null;
  return (
    <div className="flex items-center justify-center gap-2 py-2">
      <IconButton label="Página anterior" disabled={pagina <= 1} onClick={() => onChange(pagina - 1)}>
        <ChevronLeft className="size-4" />
      </IconButton>
      <span className="text-sm text-[var(--fg-muted)]">
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
              className={`flex items-center gap-2 rounded-lg border px-4 py-3 text-sm shadow-lg ${toastClasses[toast.tipo]}`}
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
