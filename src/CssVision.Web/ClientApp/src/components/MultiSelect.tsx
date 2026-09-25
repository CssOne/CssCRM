import { useEffect, useRef, useState } from "react";
import { Check, ChevronDown } from "lucide-react";

export interface OpcaoMultiSelect {
  valor: string;
  rotulo: string;
}

/**
 * Filtro de múltipla escolha: um botão do tamanho de um Select que abre uma lista com caixas de
 * marcar. Nada marcado = "todos" (o filtro não restringe nada).
 */
export function MultiSelect({
  opcoes,
  valores,
  onChange,
  rotuloTodos = "Todos",
  ariaLabel,
}: {
  opcoes: OpcaoMultiSelect[];
  valores: string[];
  onChange: (valores: string[]) => void;
  rotuloTodos?: string;
  ariaLabel?: string;
}) {
  const [aberto, setAberto] = useState(false);
  const raizRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!aberto) return;
    const fecharFora = (e: MouseEvent) => {
      if (!raizRef.current?.contains(e.target as Node)) setAberto(false);
    };
    const fecharEsc = (e: KeyboardEvent) => {
      if (e.key === "Escape") setAberto(false);
    };
    document.addEventListener("mousedown", fecharFora);
    document.addEventListener("keydown", fecharEsc);
    return () => {
      document.removeEventListener("mousedown", fecharFora);
      document.removeEventListener("keydown", fecharEsc);
    };
  }, [aberto]);

  const selecionadas = opcoes.filter((o) => valores.includes(o.valor));
  const resumo =
    selecionadas.length === 0
      ? rotuloTodos
      : selecionadas.length === 1
        ? selecionadas[0].rotulo
        : `${selecionadas[0].rotulo} +${selecionadas.length - 1}`;

  function alternar(valor: string) {
    onChange(valores.includes(valor) ? valores.filter((v) => v !== valor) : [...valores, valor]);
  }

  return (
    <div ref={raizRef} className="relative">
      <button
        type="button"
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-expanded={aberto}
        onClick={() => setAberto((a) => !a)}
        className="focus-ring flex h-10 w-full cursor-pointer items-center justify-between gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 text-left text-sm text-[var(--fg)]"
        title={selecionadas.map((o) => o.rotulo).join(", ") || undefined}
      >
        <span className={`truncate ${selecionadas.length === 0 ? "text-[var(--fg-muted)]" : ""}`}>{resumo}</span>
        <ChevronDown className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />
      </button>
      {aberto && (
        <div
          role="listbox"
          aria-multiselectable
          className="absolute left-0 z-30 mt-1 max-h-72 min-w-full overflow-y-auto rounded-lg border border-[var(--border)] bg-[var(--surface)] p-1 shadow-lg"
        >
          {opcoes.length === 0 && <p className="px-2 py-1.5 text-sm text-[var(--fg-muted)]">Nenhuma opção</p>}
          {opcoes.map((o) => {
            const marcada = valores.includes(o.valor);
            return (
              <button
                key={o.valor}
                type="button"
                role="option"
                aria-selected={marcada}
                onClick={() => alternar(o.valor)}
                className="flex w-full cursor-pointer items-center gap-2 whitespace-nowrap rounded-md px-2 py-1.5 text-left text-sm text-[var(--fg)] hover:bg-[var(--surface-hover)]"
              >
                <span
                  className={`flex size-4 shrink-0 items-center justify-center rounded border ${
                    marcada ? "border-[var(--brand)] bg-[var(--brand)] text-white" : "border-[var(--border)]"
                  }`}
                >
                  {marcada && <Check className="size-3" aria-hidden />}
                </span>
                {o.rotulo}
              </button>
            );
          })}
          {valores.length > 0 && (
            <button
              type="button"
              onClick={() => onChange([])}
              className="mt-1 w-full cursor-pointer rounded-md border-t border-[var(--border)] px-2 py-1.5 text-left text-xs font-medium text-[var(--brand)] hover:bg-[var(--surface-hover)]"
            >
              Limpar seleção
            </button>
          )}
        </div>
      )}
    </div>
  );
}
