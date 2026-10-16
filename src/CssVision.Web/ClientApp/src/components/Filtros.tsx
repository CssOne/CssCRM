import { ChevronDown, Search, SlidersHorizontal, X } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { Flutuante } from "./Calendario";

export interface ChipDeFiltro {
  rotulo: string;
  valor: string;
  limpar: () => void;
}

/**
 * Barra de filtros: busca em destaque + botão "Filtros" que abre um painel flutuante com os demais campos.
 * O painel não empurra a tela para baixo e tem rodapé com "Limpar tudo" e "Concluir". Os filtros ativos aparecem como
 * "chips" removíveis abaixo da barra (clicar no chip abre o painel), então dá para ver e tirar cada um sem abrir nada.
 * Atalhos: "/" foca a busca; Esc na busca limpa o texto.
 */
export function BarraDeFiltros({
  busca,
  onBusca,
  placeholder = "Buscar",
  chips,
  onLimparTudo,
  acoes,
  children,
  rodape,
}: {
  /** Sem `onBusca` a barra não mostra o campo de busca (só o botão de filtros). */
  busca?: string;
  onBusca?: (valor: string) => void;
  placeholder?: string;
  chips: ChipDeFiltro[];
  onLimparTudo: () => void;
  /** Botões à direita da busca (ex.: atalhos de período). */
  acoes?: ReactNode;
  /** Campos do painel (use CampoDeFiltro). */
  children: ReactNode;
  /** Linha fixa no pé do painel, acima dos botões (ex.: filtros salvos). */
  rodape?: ReactNode;
}) {
  const [aberto, setAberto] = useState(false);
  const barra = useRef<HTMLDivElement>(null);
  const campoBusca = useRef<HTMLInputElement>(null);

  // "/" leva o cursor para a busca (se a pessoa não estiver digitando em outro campo).
  useEffect(() => {
    if (!onBusca) return;
    const tecla = (e: KeyboardEvent) => {
      if (e.key !== "/" || e.ctrlKey || e.metaKey || e.altKey) return;
      const alvo = e.target as HTMLElement | null;
      if (alvo && (alvo.tagName === "INPUT" || alvo.tagName === "TEXTAREA" || alvo.tagName === "SELECT" || alvo.isContentEditable)) return;
      e.preventDefault();
      campoBusca.current?.focus();
    };
    document.addEventListener("keydown", tecla);
    return () => document.removeEventListener("keydown", tecla);
  }, [onBusca]);

  const temAtivos = chips.length > 0;

  return (
    <div className="space-y-2.5">
      <div ref={barra} className="flex flex-wrap items-center gap-2">
        {onBusca && (
          <div className="relative min-w-56 flex-1 sm:max-w-md">
            <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
            <input
              ref={campoBusca}
              type="search"
              value={busca ?? ""}
              onChange={(e) => onBusca(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Escape" && busca) {
                  e.preventDefault();
                  onBusca("");
                }
              }}
              placeholder={placeholder}
              aria-label={placeholder}
              className="focus-ring h-9 w-full rounded-full border-0 bg-[var(--surface-hover)] pl-10 pr-10 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)] [&::-webkit-search-cancel-button]:hidden"
            />
            {busca ? (
              <button
                type="button"
                aria-label="Limpar busca"
                onClick={() => {
                  onBusca("");
                  campoBusca.current?.focus();
                }}
                className="focus-ring absolute right-2.5 top-1/2 flex size-5 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full bg-[var(--fg-muted)]/30 text-[var(--fg)] hover:bg-[var(--fg-muted)]/50"
              >
                <X className="size-3" />
              </button>
            ) : (
              <kbd className="pointer-events-none absolute right-3 top-1/2 hidden -translate-y-1/2 rounded-md border border-[var(--border)] bg-[var(--surface)] px-1.5 text-[10px] font-medium text-[var(--fg-muted)] lg:block" aria-hidden>
                /
              </kbd>
            )}
          </div>
        )}

        <button
          type="button"
          aria-expanded={aberto}
          aria-haspopup="dialog"
          onClick={() => setAberto((v) => !v)}
          className={`focus-ring inline-flex h-9 cursor-pointer items-center gap-2 rounded-full px-3.5 text-sm font-medium transition-colors ${
            aberto || temAtivos ? "bg-[var(--brand-soft)] text-[var(--brand)]" : "bg-[var(--surface-hover)] text-[var(--fg)] hover:brightness-95"
          }`}
        >
          <SlidersHorizontal className="size-4" aria-hidden />
          Filtros
          {temAtivos && <span className="flex size-5 items-center justify-center rounded-full bg-[var(--brand)] text-[11px] font-semibold text-[var(--brand-fg)]">{chips.length}</span>}
          <ChevronDown className={`size-3.5 transition-transform ${aberto ? "rotate-180" : ""}`} aria-hidden />
        </button>

        {acoes}
      </div>

      <Flutuante aberto={aberto} ancora={barra} onFechar={() => setAberto(false)} rotulo="Filtros" larguraDaAncora className="p-0">
        <div className="max-h-[min(70vh,34rem)] overflow-y-auto p-4">
          <div className="filtros-painel grid gap-x-3 gap-y-2.5 sm:grid-cols-2 lg:grid-cols-6">{children}</div>
          {rodape && <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-[var(--border)] pt-3">{rodape}</div>}
        </div>
        <div className="flex items-center justify-between gap-2 rounded-b-2xl border-t border-[var(--border)] bg-[var(--surface-hover)] px-4 py-2.5">
          <span className="text-xs text-[var(--fg-muted)]">{temAtivos ? `${chips.length} filtro(s) ativo(s)` : "Nenhum filtro ativo"}</span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={!temAtivos}
              onClick={onLimparTudo}
              className="focus-ring h-8 cursor-pointer rounded-full px-3 text-sm font-medium text-[var(--fg-muted)] hover:text-[var(--fg)] disabled:cursor-not-allowed disabled:opacity-40"
            >
              Limpar tudo
            </button>
            <button
              type="button"
              onClick={() => setAberto(false)}
              className="focus-ring h-8 cursor-pointer rounded-full bg-[var(--brand)] px-4 text-sm font-medium text-[var(--brand-fg)] hover:brightness-110"
            >
              Concluir
            </button>
          </div>
        </div>
      </Flutuante>

      {temAtivos && (
        <div className="flex flex-wrap items-center gap-1.5">
          {chips.map((c) => (
            <span key={c.rotulo} className="inline-flex h-7 items-center gap-1 rounded-full bg-[var(--brand-soft)] pl-1 pr-1 text-xs text-[var(--brand)]">
              <button type="button" onClick={() => setAberto(true)} title="Editar filtros" className="focus-ring flex h-full max-w-72 cursor-pointer items-center gap-1 rounded-full pl-2 pr-1">
                <span className="text-[var(--fg-muted)]">{c.rotulo}</span>
                <span className="truncate font-semibold">{c.valor}</span>
              </button>
              <button
                type="button"
                aria-label={`Remover filtro ${c.rotulo}`}
                onClick={c.limpar}
                className="focus-ring flex size-5 cursor-pointer items-center justify-center rounded-full hover:bg-[var(--brand)]/15"
              >
                <X className="size-3" />
              </button>
            </span>
          ))}
          <button type="button" onClick={onLimparTudo} className="focus-ring ml-1 cursor-pointer rounded-md px-1.5 text-xs font-medium text-[var(--fg-muted)] hover:text-[var(--fg)]">
            Limpar tudo
          </button>
        </div>
      )}
    </div>
  );
}

/** Um campo do painel de filtros: rótulo pequeno em cima e o controle ocupando toda a coluna. */
export function CampoDeFiltro({ rotulo, extra, className = "", children }: { rotulo: string; extra?: ReactNode; className?: string; children: ReactNode }) {
  return (
    <div className={`min-w-0 ${className}`}>
      <div className="mb-1 flex items-center justify-between gap-2 px-1">
        <span className="text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{rotulo}</span>
        {extra}
      </div>
      {children}
    </div>
  );
}
