import { AlertTriangle, CheckCircle2, ChevronLeft, ChevronRight, Info, Loader2, X, XCircle } from "lucide-react";
import {
  createContext,
  forwardRef,
  useCallback,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from "react";

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

// --- Cartão ---

export function Card({ className = "", children }: { className?: string; children: ReactNode }) {
  return (
    <div className={`rounded-xl border border-[var(--border)] bg-[var(--surface)] ${className}`}>{children}</div>
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

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ className = "", ...props }, ref) => (
    <textarea ref={ref} className={`${fieldBase} h-auto min-h-24 py-2 ${className}`} {...props} />
  )
);
Textarea.displayName = "Textarea";

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(
  ({ className = "", children, ...props }, ref) => (
    <select ref={ref} className={`${fieldBase} ${className}`} {...props}>
      {children}
    </select>
  )
);
Select.displayName = "Select";

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

  if (!open) return null;

  const widths = { sm: "max-w-sm", md: "max-w-lg", lg: "max-w-2xl" };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" role="presentation" onClick={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`max-h-[85vh] w-full ${widths[size]} overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface)] shadow-2xl flex flex-col`}
        onClick={(e) => e.stopPropagation()}
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

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/40" role="presentation" onClick={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="flex h-full w-full max-w-md flex-col border-l border-[var(--border)] bg-[var(--surface)] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
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
