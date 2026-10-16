import { ChevronDown, Search, SlidersHorizontal, X } from "lucide-react";
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";

/** Rótulos dos filtros ativos: os campos do painel que combinam com algum deles ganham destaque. */
const AtivosContext = createContext<string[]>([]);

export interface ChipDeFiltro {
  rotulo: string;
  valor: string;
  limpar: () => void;
}

/**
 * Barra de filtros num cartão: busca em destaque e, logo abaixo, todos os campos sempre à vista, em linhas compactas.
 * O botão "Filtros" recolhe/expande os campos (a escolha fica guardada por tela). Recolhido, os filtros ativos aparecem como
 * "chips" removíveis (clicar no chip reabre os campos).
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
  // Aberto por padrão; a escolha de recolher fica guardada por tela, neste navegador.
  const chave = `filtros-aberto:${typeof window === "undefined" ? "" : window.location.pathname}`;
  const [aberto, setAberto] = useState(() => {
    try {
      return localStorage.getItem(chave) !== "0";
    } catch {
      return true;
    }
  });
  // Durante a animação o conteúdo é cortado (overflow escondido); depois, volta a ficar solto para as listas dos campos abrirem por cima.
  const [animando, setAnimando] = useState(false);
  const alternar = () => {
    setAnimando(true);
    setAberto((v) => {
      try {
        localStorage.setItem(chave, v ? "0" : "1");
      } catch {
        /* sem armazenamento local: só não lembra */
      }
      return !v;
    });
  };
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
    <AtivosContext.Provider value={chips.map((c) => c.rotulo.toLowerCase())}>
    <div className="ui-surface rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-3 shadow-[var(--shadow-card)]">
      <div className="flex flex-wrap items-center gap-2">
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

        {acoes}

        <div className="ml-auto flex items-center gap-1">
          {temAtivos && (
            <button type="button" onClick={onLimparTudo} className="focus-ring h-9 cursor-pointer rounded-full px-3 text-sm font-medium text-[var(--fg-muted)] hover:text-[var(--fg)]">
              Limpar tudo
            </button>
          )}
          <button
            type="button"
            aria-expanded={aberto}
            onClick={alternar}
            className={`focus-ring inline-flex h-9 cursor-pointer items-center gap-2 rounded-full px-3.5 text-sm font-medium transition-colors ${
              temAtivos ? "bg-[var(--brand-soft)] text-[var(--brand)]" : "bg-[var(--surface-hover)] text-[var(--fg)] hover:brightness-95"
            }`}
          >
            <SlidersHorizontal className="size-4" aria-hidden />
            Filtros
            {temAtivos && <span className="flex size-5 items-center justify-center rounded-full bg-[var(--brand)] text-[11px] font-semibold text-[var(--brand-fg)]">{chips.length}</span>}
            <ChevronDown className={`size-3.5 transition-transform ${aberto ? "rotate-180" : ""}`} aria-hidden />
          </button>
        </div>
      </div>

      {/* Campos: abrem e fecham deslizando (altura animada) com um fade suave; continuam montados para guardar o que foi digitado. */}
      <div
        onTransitionEnd={(e) => {
          if (e.target === e.currentTarget && e.propertyName === "grid-template-rows") setAnimando(false);
        }}
        className={`grid transition-[grid-template-rows,opacity,margin] duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] motion-reduce:transition-none ${
          aberto ? "mt-3 grid-rows-[1fr] opacity-100" : "mt-0 grid-rows-[0fr] opacity-0"
        }`}
        inert={!aberto}
      >
        <div className={`min-h-0 ${aberto && !animando ? "overflow-visible" : "overflow-hidden"}`}>
          <div className="border-t border-[var(--border)] pt-3">
            <div className="filtros-painel grid gap-x-3 gap-y-2.5 sm:grid-cols-2 lg:grid-cols-6">{children}</div>
            {rodape && <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-[var(--border)] pt-3">{rodape}</div>}
          </div>
        </div>
      </div>

      {/* Chips dos filtros ativos: aparecem com o mesmo deslize quando os campos estão recolhidos. */}
      <div
        className={`grid transition-[grid-template-rows,opacity,margin] duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] motion-reduce:transition-none ${
          !aberto && temAtivos ? "mt-3 grid-rows-[1fr] opacity-100" : "mt-0 grid-rows-[0fr] opacity-0"
        }`}
        inert={aberto || !temAtivos}
      >
        <div className="min-h-0 overflow-hidden">
          <div className="flex flex-wrap items-center gap-1.5 border-t border-[var(--border)] pt-3">
            {chips.map((c) => (
              <span key={c.rotulo} className="inline-flex h-7 items-center gap-1 rounded-full bg-[var(--brand-soft)] pl-1 pr-1 text-xs text-[var(--brand)]">
                <button type="button" tabIndex={!aberto ? 0 : -1} onClick={alternar} title="Editar filtros" className="focus-ring flex h-full max-w-72 cursor-pointer items-center gap-1 rounded-full pl-2 pr-1">
                  <span className="text-[var(--fg-muted)]">{c.rotulo}</span>
                  <span className="truncate font-semibold">{c.valor}</span>
                </button>
                <button
                  type="button"
                  tabIndex={!aberto ? 0 : -1}
                  aria-label={`Remover filtro ${c.rotulo}`}
                  onClick={c.limpar}
                  className="focus-ring flex size-5 cursor-pointer items-center justify-center rounded-full hover:bg-[var(--brand)]/15"
                >
                  <X className="size-3" />
                </button>
              </span>
            ))}
          </div>
        </div>
      </div>
    </div>
    </AtivosContext.Provider>
  );
}

/** Um campo do painel de filtros: rótulo pequeno em cima e o controle ocupando toda a coluna. */
export function CampoDeFiltro({ rotulo, extra, className = "", children }: { rotulo: string; extra?: ReactNode; className?: string; children: ReactNode }) {
  const ativos = useContext(AtivosContext);
  const nome = rotulo.toLowerCase();
  // O rótulo do chip pode ser uma versão curta do rótulo do campo (ex.: "Canal" x "Canal de aquisição").
  const ativo = ativos.some((a) => nome.startsWith(a) || a.startsWith(nome));
  return (
    <div className={`filtro-campo min-w-0 ${className}`} data-ativo={ativo}>
      <div className="mb-1 flex items-center justify-between gap-2 px-1">
        <span className={`flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide transition-colors ${ativo ? "text-[var(--brand)]" : "text-[var(--fg-muted)]"}`}>
          {ativo && <span className="size-1.5 rounded-full bg-[var(--brand)]" aria-hidden />}
          {rotulo}
        </span>
        {extra}
      </div>
      {children}
    </div>
  );
}
