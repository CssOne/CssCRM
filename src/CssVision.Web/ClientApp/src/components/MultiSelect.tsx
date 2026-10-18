import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Check, ChevronDown } from "lucide-react";
import { paraBusca } from "../lib/busca";

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
  const [busca, setBusca] = useState("");
  const [posicao, setPosicao] = useState<{ left: number; width: number; top?: number; bottom?: number; maxHeight: number } | null>(null);
  const raizRef = useRef<HTMLDivElement>(null);
  const painelRef = useRef<HTMLDivElement>(null);
  const buscaRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!aberto) return;
    // No celular/tablet, focar o campo ao abrir levanta o teclado e a lista some; lá o campo só recebe foco quando a pessoa toca nele.
    if (!window.matchMedia?.("(pointer: coarse)").matches) buscaRef.current?.focus();
    const abertoEm = Date.now();
    const fecharFora = (e: Event) => {
      const alvo = e.target as Node;
      if (!raizRef.current?.contains(alvo) && !painelRef.current?.contains(alvo)) setAberto(false);
    };
    // A lista é fixa na tela (fora do painel de filtros, que corta e empilha por baixo): rolar a página ou redimensionar fecha.
    const fecharRolagem = (e: Event) => {
      if (Date.now() - abertoEm < 600) return;
      if (e.target instanceof Node && painelRef.current?.contains(e.target)) return;
      setAberto(false);
    };
    const fecharEsc = (e: KeyboardEvent) => {
      if (e.key === "Escape") setAberto(false);
    };
    document.addEventListener("mousedown", fecharFora);
    document.addEventListener("touchstart", fecharFora, { passive: true });
    document.addEventListener("keydown", fecharEsc);
    window.addEventListener("scroll", fecharRolagem, true);
    window.addEventListener("resize", fecharRolagem);
    return () => {
      window.removeEventListener("scroll", fecharRolagem, true);
      window.removeEventListener("resize", fecharRolagem);
      document.removeEventListener("mousedown", fecharFora);
      document.removeEventListener("touchstart", fecharFora);
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

  const termo = paraBusca(busca);
  const visiveis = termo ? opcoes.filter((o) => paraBusca(o.rotulo).includes(termo)) : opcoes;

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
        onClick={() => {
          setBusca("");
          if (aberto) {
            setAberto(false);
            return;
          }
          const ret = raizRef.current?.getBoundingClientRect();
          if (!ret) return;
          const abaixo = window.innerHeight - ret.bottom - 8;
          const acima = ret.top - 8;
          const paraCima = abaixo < 240 && acima > abaixo;
          setPosicao({
            left: ret.left,
            width: Math.max(ret.width, 200),
            ...(paraCima ? { bottom: window.innerHeight - ret.top + 4 } : { top: ret.bottom + 4 }),
            maxHeight: Math.min(320, Math.max(160, paraCima ? acima : abaixo)),
          });
          setAberto(true);
        }}
        className="focus-ring flex h-10 w-full cursor-pointer items-center justify-between gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 text-left text-sm text-[var(--fg)]"
        title={selecionadas.map((o) => o.rotulo).join(", ") || undefined}
      >
        <span className={`truncate ${selecionadas.length === 0 ? "text-[var(--fg-muted)]" : ""}`}>{resumo}</span>
        <ChevronDown className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />
      </button>
      {aberto && posicao && createPortal(
        <div
          ref={painelRef}
          role="listbox"
          aria-multiselectable
          style={{ position: "fixed", left: posicao.left, width: posicao.width, top: posicao.top, bottom: posicao.bottom, maxHeight: posicao.maxHeight }}
          className="ui-surface z-[70] overflow-y-auto rounded-xl border border-[var(--border)] bg-[var(--surface-solid)] p-1 shadow-xl"
        >
          <input
            ref={buscaRef}
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            placeholder="Pesquisar…"
            aria-label="Pesquisar nas opções"
            autoComplete="off"
            className="focus-ring mb-1 h-8 w-full rounded-md border border-[var(--border)] bg-[var(--bg)] px-2 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
          />
          {opcoes.length === 0 && <p className="px-2 py-1.5 text-sm text-[var(--fg-muted)]">Nenhuma opção</p>}
          {opcoes.length > 0 && visiveis.length === 0 && <p className="px-2 py-1.5 text-sm text-[var(--fg-muted)]">Nada encontrado</p>}
          {visiveis.map((o) => {
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
        </div>,
        document.body
      )}
    </div>
  );
}
