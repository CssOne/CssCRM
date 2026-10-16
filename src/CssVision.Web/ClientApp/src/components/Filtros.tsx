import { ChevronDown, Search, SlidersHorizontal, X } from "lucide-react";
import { useState, type ReactNode } from "react";

export interface ChipDeFiltro {
  rotulo: string;
  valor: string;
  limpar: () => void;
}

/**
 * Barra de filtros: busca em destaque + botão "Filtros" que abre o painel com os demais campos.
 * Os filtros ativos aparecem como "chips" removíveis logo abaixo, então dá para ver (e tirar) cada um sem abrir o painel.
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
  busca: string;
  onBusca: (valor: string) => void;
  placeholder?: string;
  chips: ChipDeFiltro[];
  onLimparTudo: () => void;
  /** Botões à direita da busca (ex.: filtros salvos). */
  acoes?: ReactNode;
  /** Campos do painel (use CampoDeFiltro). */
  children: ReactNode;
  /** Linha fixa no pé do painel (ex.: salvar filtro). */
  rodape?: ReactNode;
}) {
  const [aberto, setAberto] = useState(false);

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative min-w-56 flex-1 sm:max-w-md">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
          <input
            type="search"
            value={busca}
            onChange={(e) => onBusca(e.target.value)}
            placeholder={placeholder}
            aria-label={placeholder}
            className="focus-ring h-9 w-full rounded-full border-0 bg-[var(--surface-hover)] pl-9 pr-9 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)] [&::-webkit-search-cancel-button]:hidden"
          />
          {busca && (
            <button
              type="button"
              aria-label="Limpar busca"
              onClick={() => onBusca("")}
              className="focus-ring absolute right-2.5 top-1/2 flex size-5 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full bg-[var(--fg-muted)]/30 text-[var(--fg)] hover:bg-[var(--fg-muted)]/50"
            >
              <X className="size-3" />
            </button>
          )}
        </div>

        <button
          type="button"
          aria-expanded={aberto}
          onClick={() => setAberto((v) => !v)}
          className={`focus-ring inline-flex h-9 cursor-pointer items-center gap-2 rounded-full px-3.5 text-sm font-medium transition-colors ${
            aberto || chips.length > 0 ? "bg-[var(--brand-soft)] text-[var(--brand)]" : "bg-[var(--surface-hover)] text-[var(--fg)] hover:brightness-95"
          }`}
        >
          <SlidersHorizontal className="size-4" aria-hidden />
          Filtros
          {chips.length > 0 && (
            <span className="flex size-5 items-center justify-center rounded-full bg-[var(--brand)] text-[11px] font-semibold text-[var(--brand-fg)]">{chips.length}</span>
          )}
          <ChevronDown className={`size-3.5 transition-transform ${aberto ? "rotate-180" : ""}`} aria-hidden />
        </button>

        {acoes}
      </div>

      {aberto && (
        <div className="ui-surface relative z-30 rounded-[var(--radius-card)] border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)]">
          <div className="grid gap-x-4 gap-y-3.5 sm:grid-cols-2 lg:grid-cols-4">{children}</div>
          {rodape && <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-[var(--border)] pt-3">{rodape}</div>}
        </div>
      )}

      {chips.length > 0 && (
        <div className="flex flex-wrap items-center gap-1.5">
          {chips.map((c) => (
            <span key={c.rotulo} className="inline-flex h-7 items-center gap-1 rounded-full bg-[var(--brand-soft)] pl-3 pr-1 text-xs text-[var(--brand)]">
              <span className="text-[var(--fg-muted)]">{c.rotulo}</span>
              <span className="max-w-48 truncate font-semibold">{c.valor}</span>
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
      <div className="mb-1.5 flex items-center justify-between gap-2">
        <span className="text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{rotulo}</span>
        {extra}
      </div>
      {children}
    </div>
  );
}
