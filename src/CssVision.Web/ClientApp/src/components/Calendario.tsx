import { ChevronDown, ChevronUp } from "lucide-react";
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";

const SEMANA = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];
const MESES_CURTOS = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

/** "2026-10-05" → Date local (sem deslocar por fuso). */
export function paraData(iso: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null;
}

export function paraIso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export function exibirData(iso: string): string {
  const d = paraData(iso);
  return d ? `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")}/${d.getFullYear()}` : "";
}

function mesmoDia(a: Date, b: Date) {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

const botaoSeta =
  "focus-ring flex size-8 cursor-pointer items-center justify-center rounded-full text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]";

/** Grade de dias de um mês: os extremos (início/fim) em círculo escuro e o intervalo entre eles com faixa suave. */
export function Calendario({
  mes,
  onMes,
  inicio,
  fim,
  min,
  max,
  onEscolher,
  rodape,
}: {
  mes: Date;
  onMes: (m: Date) => void;
  inicio: Date | null;
  fim?: Date | null;
  /** "aaaa-mm-dd": dias fora do limite ficam desabilitados. */
  min?: string;
  max?: string;
  onEscolher: (d: Date) => void;
  rodape?: ReactNode;
}) {
  const hoje = new Date();
  const primeiro = new Date(mes.getFullYear(), mes.getMonth(), 1);
  const comeco = new Date(primeiro);
  comeco.setDate(1 - primeiro.getDay());
  const dias = Array.from({ length: 42 }, (_, i) => {
    const d = new Date(comeco);
    d.setDate(comeco.getDate() + i);
    return d;
  });
  const titulo = primeiro.toLocaleDateString("pt-BR", { month: "long", year: "numeric" });

  return (
    <div>
      <div className="mb-3 flex items-center justify-between">
        <span className="text-[15px] font-medium text-[var(--fg)]">{titulo}</span>
        <div className="flex items-center">
          <button type="button" aria-label="Mês anterior" onClick={() => onMes(new Date(mes.getFullYear(), mes.getMonth() - 1, 1))} className={botaoSeta}>
            <ChevronUp className="size-4" />
          </button>
          <button type="button" aria-label="Próximo mês" onClick={() => onMes(new Date(mes.getFullYear(), mes.getMonth() + 1, 1))} className={botaoSeta}>
            <ChevronDown className="size-4" />
          </button>
        </div>
      </div>

      <div className="grid grid-cols-7 text-center text-xs text-[var(--fg-muted)]">
        {SEMANA.map((s) => (
          <span key={s} className="pb-2">
            {s}
          </span>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-y-0.5 text-center text-sm">
        {dias.map((d) => {
          const iso = paraIso(d);
          const doMes = d.getMonth() === mes.getMonth();
          const extremo = (inicio && mesmoDia(d, inicio)) || (fim && mesmoDia(d, fim));
          const noIntervalo = inicio && fim && d > inicio && d < fim;
          const bloqueado = (!!min && iso < min) || (!!max && iso > max);
          return (
            <div key={iso} className={`flex justify-center ${noIntervalo ? "bg-[var(--brand-soft)]" : ""}`}>
              <button
                type="button"
                disabled={bloqueado}
                onClick={() => onEscolher(d)}
                aria-label={d.toLocaleDateString("pt-BR", { dateStyle: "long" })}
                aria-pressed={!!extremo}
                className={`focus-ring flex size-9 items-center justify-center rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-30 ${
                  extremo
                    ? "cursor-pointer bg-[var(--fg)] font-semibold text-[var(--surface-solid)]"
                    : `cursor-pointer enabled:hover:bg-[var(--surface-hover)] ${doMes ? "text-[var(--fg)]" : "text-[var(--fg-muted)]/45"} ${mesmoDia(d, hoje) ? "ring-1 ring-inset ring-[var(--brand)]" : ""}`
                }`}
              >
                {d.getDate()}
              </button>
            </div>
          );
        })}
      </div>
      {rodape && <div className="mt-3 flex items-center justify-between border-t border-[var(--border)] pt-3 text-xs">{rodape}</div>}
    </div>
  );
}

/** Grade de meses de um ano (para o seletor de mês). */
export function GradeDeMeses({ ano, onAno, mesEscolhido, onEscolher }: { ano: number; onAno: (a: number) => void; mesEscolhido: string; onEscolher: (iso: string) => void }) {
  const hoje = new Date();
  return (
    <div>
      <div className="mb-3 flex items-center justify-between">
        <span className="text-[15px] font-medium text-[var(--fg)]">{ano}</span>
        <div className="flex items-center">
          <button type="button" aria-label="Ano anterior" onClick={() => onAno(ano - 1)} className={botaoSeta}>
            <ChevronUp className="size-4" />
          </button>
          <button type="button" aria-label="Próximo ano" onClick={() => onAno(ano + 1)} className={botaoSeta}>
            <ChevronDown className="size-4" />
          </button>
        </div>
      </div>
      <div className="grid grid-cols-3 gap-1.5">
        {MESES_CURTOS.map((nome, i) => {
          const iso = `${ano}-${String(i + 1).padStart(2, "0")}`;
          const escolhido = iso === mesEscolhido;
          const atual = hoje.getFullYear() === ano && hoje.getMonth() === i;
          return (
            <button
              key={nome}
              type="button"
              aria-pressed={escolhido}
              onClick={() => onEscolher(iso)}
              className={`focus-ring h-10 cursor-pointer rounded-xl text-sm capitalize transition-colors ${
                escolhido
                  ? "bg-[var(--fg)] font-semibold text-[var(--surface-solid)]"
                  : `text-[var(--fg)] hover:bg-[var(--surface-hover)] ${atual ? "ring-1 ring-inset ring-[var(--brand)]" : ""}`
              }`}
            >
              {nome}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/**
 * Janela flutuante presa ao campo (renderizada no <body>, então não é cortada por modais nem por superfícies com desfoque).
 * Abre para baixo e vira para cima quando falta espaço; fecha ao clicar fora, no Esc ou ao rolar a página.
 */
export function Flutuante({
  aberto,
  ancora,
  onFechar,
  largura = 312,
  rotulo,
  children,
}: {
  aberto: boolean;
  ancora: React.RefObject<HTMLElement | null>;
  onFechar: () => void;
  largura?: number;
  rotulo: string;
  children: ReactNode;
}) {
  const painel = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState<{ left: number; top?: number; bottom?: number } | null>(null);

  useLayoutEffect(() => {
    if (!aberto) return;
    const r = ancora.current?.getBoundingClientRect();
    if (!r) return;
    const abaixo = window.innerHeight - r.bottom;
    const paraCima = abaixo < 400 && r.top > abaixo;
    setPos({
      left: Math.max(8, Math.min(r.left, window.innerWidth - largura - 8)),
      ...(paraCima ? { bottom: window.innerHeight - r.top + 8 } : { top: r.bottom + 8 }),
    });
  }, [aberto, ancora, largura]);

  useEffect(() => {
    if (!aberto) return;
    const aberturaEm = Date.now();
    const fora = (e: MouseEvent) => {
      const alvo = e.target as Node;
      if (!painel.current?.contains(alvo) && !ancora.current?.contains(alvo)) onFechar();
    };
    const tecla = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onFechar();
      }
    };
    const rolar = (e: Event) => {
      if (Date.now() - aberturaEm < 300) return;
      if (e.target instanceof Node && painel.current?.contains(e.target)) return;
      onFechar();
    };
    document.addEventListener("mousedown", fora);
    document.addEventListener("keydown", tecla, true);
    window.addEventListener("scroll", rolar, true);
    window.addEventListener("resize", onFechar);
    return () => {
      document.removeEventListener("mousedown", fora);
      document.removeEventListener("keydown", tecla, true);
      window.removeEventListener("scroll", rolar, true);
      window.removeEventListener("resize", onFechar);
    };
  }, [aberto, ancora, onFechar]);

  if (!aberto || !pos) return null;
  return createPortal(
    <div
      ref={painel}
      role="dialog"
      aria-label={rotulo}
      style={{ position: "fixed", left: pos.left, top: pos.top, bottom: pos.bottom, width: largura }}
      className="z-[80] rounded-2xl border border-[var(--border)] bg-[var(--surface-solid)] p-4 shadow-[0_20px_50px_-12px_rgb(0_0_0/0.3)]"
    >
      {children}
    </div>,
    document.body
  );
}
