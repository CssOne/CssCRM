import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Check, ChevronDown, ListFilter, Search, X } from "lucide-react";
import { paraBusca } from "../lib/busca";

export interface OpcaoMultiSelect {
  valor: string;
  rotulo: string;
  /** Texto curto à direita da opção (ex.: a quantidade), em destaque suave. */
  detalhe?: string;
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
  cor,
}: {
  opcoes: OpcaoMultiSelect[];
  valores: string[];
  onChange: (valores: string[]) => void;
  rotuloTodos?: string;
  ariaLabel?: string;
  /**
   * Com a cor (ex.: a da coluna do quadro), o botão vira uma pílula com ícone de filtro, contador de marcados e botão de limpar,
   * e a lista ganha a mesma cor. Sem ela, é o botão do tamanho de um Select de sempre.
   */
  cor?: string;
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
      <div className={cor ? "flex items-center gap-1.5" : undefined}>
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
        className={
          cor
            ? "focus-ring flex h-9 min-w-0 flex-1 cursor-pointer items-center gap-2 rounded-full border px-1.5 text-left text-xs font-semibold text-[var(--fg)] transition-[background-color,border-color,box-shadow] duration-200 hover:shadow-sm"
            : "focus-ring flex h-10 w-full cursor-pointer items-center justify-between gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 text-left text-sm text-[var(--fg)]"
        }
        style={
          cor
            ? {
                borderColor: `color-mix(in srgb, ${cor} ${selecionadas.length > 0 || aberto ? 70 : 32}%, transparent)`,
                backgroundColor: `color-mix(in srgb, ${cor} ${selecionadas.length > 0 ? 18 : 8}%, var(--surface))`,
                boxShadow: aberto ? `0 0 0 3px color-mix(in srgb, ${cor} 22%, transparent)` : undefined,
              }
            : undefined
        }
        title={selecionadas.map((o) => o.rotulo).join(", ") || undefined}
      >
        {cor && (
          <span className="flex size-6 shrink-0 items-center justify-center rounded-full text-white" style={{ backgroundColor: cor }} aria-hidden>
            <ListFilter className="size-3.5" />
          </span>
        )}
        <span className={`truncate ${cor ? "flex-1" : ""} ${selecionadas.length === 0 ? "text-[var(--fg-muted)]" : ""}`}>{resumo}</span>
        {cor && selecionadas.length > 0 && (
          <span className="flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full px-1.5 text-[10px] font-bold text-white [font-variant-numeric:tabular-nums]" style={{ backgroundColor: cor }}>
            {selecionadas.length}
          </span>
        )}
        <ChevronDown className={`size-4 shrink-0 transition-transform duration-300 ease-[cubic-bezier(0.34,1.56,0.64,1)] ${cor ? "mr-1" : ""} ${aberto ? "rotate-180 text-[var(--brand)]" : "text-[var(--fg-muted)]"}`} aria-hidden />
      </button>
      {cor && selecionadas.length > 0 && (
        <button
          type="button"
          aria-label="Limpar filtro"
          title="Limpar filtro"
          onClick={() => onChange([])}
          className="focus-ring flex size-7 shrink-0 cursor-pointer items-center justify-center rounded-full border border-[var(--border)] bg-[var(--surface)] text-[var(--fg-muted)] transition-colors hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
        >
          <X className="size-3.5" aria-hidden />
        </button>
      )}
      </div>
      {aberto && posicao && createPortal(
        <div
          ref={painelRef}
          role="listbox"
          aria-multiselectable
          style={{ position: "fixed", left: posicao.left, width: posicao.width, top: posicao.top, bottom: posicao.bottom, maxHeight: posicao.maxHeight }}
          data-origem={posicao.bottom !== undefined ? "baixo" : "cima"}
          className="lista-flutuante ui-surface z-[70] overflow-y-auto rounded-xl border border-[var(--border)] bg-[var(--surface-solid)] p-1 shadow-xl"
        >
          <div className="sticky top-0 z-10 -mx-1 -mt-1 mb-1 bg-[var(--surface-solid)] p-1">
            <div className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
              <input
                ref={buscaRef}
                value={busca}
                onChange={(e) => setBusca(e.target.value)}
                placeholder="Pesquisar…"
                aria-label="Pesquisar nas opções"
                autoComplete="off"
                className="focus-ring h-8 w-full rounded-md border border-[var(--border)] bg-[var(--bg)] pl-7 pr-7 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
              />
              {busca && (
                <button type="button" aria-label="Limpar pesquisa" onClick={() => { setBusca(""); buscaRef.current?.focus(); }} className="focus-ring absolute right-1.5 top-1/2 flex size-5 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]">
                  <X className="size-3" />
                </button>
              )}
            </div>
            {termo && visiveis.length > 0 && <p className="px-1 pt-1 text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{visiveis.length} de {opcoes.length}</p>}
          </div>
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
                className="flex w-full cursor-pointer items-center gap-2 whitespace-nowrap rounded-md px-2 py-1.5 text-left text-sm text-[var(--fg)] transition-colors duration-150 hover:bg-[var(--surface-hover)]"
              >
                <span
                  className={`flex size-4 shrink-0 items-center justify-center rounded border ${
                    marcada ? (cor ? "text-white" : "border-[var(--brand)] bg-[var(--brand)] text-white") : "border-[var(--border)]"
                  }`}
                  style={cor && marcada ? { backgroundColor: cor, borderColor: cor } : undefined}
                >
                  {marcada && <Check className="opcao-marca size-3" aria-hidden />}
                </span>
                <span className="flex-1">{o.rotulo}</span>
                {o.detalhe && (
                  <span
                    className="rounded-full px-1.5 py-0.5 text-[11px] font-bold text-[var(--fg-muted)] [font-variant-numeric:tabular-nums]"
                    style={{ backgroundColor: cor ? `color-mix(in srgb, ${cor} 14%, transparent)` : "var(--surface-hover)" }}
                  >
                    {o.detalhe}
                  </span>
                )}
              </button>
            );
          })}
          {valores.length > 0 && (
            <button
              type="button"
              onClick={() => onChange([])}
              className="mt-1 w-full cursor-pointer rounded-md border-t border-[var(--border)] px-2 py-1.5 text-left text-xs font-medium text-[var(--brand)] hover:bg-[var(--surface-hover)]"
              style={cor ? { color: cor } : undefined}
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
