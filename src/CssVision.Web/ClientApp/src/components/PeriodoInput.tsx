import { CalendarDays, ChevronDown, ChevronUp, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";

const SEMANA = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

/** "2026-10-05" → Date local (sem deslocar por fuso). */
function paraData(iso: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null;
}

function paraIso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function exibir(iso: string): string {
  const d = paraData(iso);
  return d ? `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")}/${d.getFullYear()}` : "";
}

function mesmoDia(a: Date, b: Date) {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

/**
 * Filtro de período: dois campos lado a lado (início e fim) que abrem um mesmo calendário. Os dias entre as duas datas ficam
 * destacados. Os valores são "aaaa-mm-dd" (ou vazio), como nos campos de data nativos que ele substitui.
 */
export function PeriodoInput({
  de,
  ate,
  onChange,
  ariaLabel = "Período",
}: {
  de: string;
  ate: string;
  onChange: (de: string, ate: string) => void;
  ariaLabel?: string;
}) {
  const [aberto, setAberto] = useState(false);
  const [campo, setCampo] = useState<"de" | "ate">("de");
  const [mes, setMes] = useState(() => {
    const base = paraData(de) ?? paraData(ate) ?? new Date();
    return new Date(base.getFullYear(), base.getMonth(), 1);
  });
  const raiz = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!aberto) return;
    const fora = (e: MouseEvent) => {
      if (!raiz.current?.contains(e.target as Node)) setAberto(false);
    };
    const tecla = (e: KeyboardEvent) => e.key === "Escape" && setAberto(false);
    document.addEventListener("mousedown", fora);
    document.addEventListener("keydown", tecla);
    return () => {
      document.removeEventListener("mousedown", fora);
      document.removeEventListener("keydown", tecla);
    };
  }, [aberto]);

  function abrir(qual: "de" | "ate") {
    setCampo(qual);
    const base = paraData(qual === "de" ? de : ate) ?? paraData(qual === "de" ? ate : de);
    if (base) setMes(new Date(base.getFullYear(), base.getMonth(), 1));
    setAberto(true);
  }

  function escolher(dia: Date) {
    const iso = paraIso(dia);
    if (campo === "de") {
      // Início depois do fim: o fim é descartado em vez de ficar um período invertido.
      onChange(iso, ate && iso > ate ? "" : ate);
      setCampo("ate");
    } else {
      if (de && iso < de) onChange(iso, de);
      else onChange(de, iso);
      setAberto(false);
    }
  }

  const inicio = paraData(de);
  const fim = paraData(ate);
  const hoje = new Date();
  const primeiro = new Date(mes.getFullYear(), mes.getMonth(), 1);
  const comeco = new Date(primeiro);
  comeco.setDate(1 - primeiro.getDay());
  const dias = Array.from({ length: 42 }, (_, i) => {
    const d = new Date(comeco);
    d.setDate(comeco.getDate() + i);
    return d;
  });
  const tituloMes = primeiro.toLocaleDateString("pt-BR", { month: "long", year: "numeric" });
  const temValor = !!(de || ate);

  const botaoCampo = (qual: "de" | "ate", valor: string) => (
    <button
      type="button"
      onClick={() => abrir(qual)}
      aria-label={`${ariaLabel}: ${qual === "de" ? "início" : "fim"}`}
      className={`focus-ring h-9 min-w-0 flex-1 cursor-pointer rounded-lg border bg-[var(--field)] px-3 text-left text-sm [font-variant-numeric:tabular-nums] ${
        aberto && campo === qual ? "border-[var(--brand)] ring-2 ring-[var(--brand)]/25" : "border-[var(--border)]"
      } ${valor ? "text-[var(--fg)]" : "text-[var(--fg-muted)]"}`}
    >
      {valor ? exibir(valor) : "dd/mm/aaaa"}
    </button>
  );

  return (
    <div ref={raiz} className="relative">
      <div className="flex items-center gap-2">
        {botaoCampo("de", de)}
        {botaoCampo("ate", ate)}
        {temValor && (
          <button
            type="button"
            aria-label="Limpar período"
            title="Limpar período"
            onClick={() => onChange("", "")}
            className="focus-ring flex size-7 shrink-0 cursor-pointer items-center justify-center rounded-full text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
          >
            <X className="size-3.5" />
          </button>
        )}
      </div>

      {aberto && (
        <div
          role="dialog"
          aria-label="Calendário"
          className="ui-surface absolute left-0 top-full z-50 mt-2 w-[19.5rem] rounded-2xl border border-[var(--border)] bg-[var(--surface-solid)] p-4 shadow-[0_20px_50px_-12px_rgb(0_0_0/0.3)]"
        >
          <div className="mb-3 flex items-center justify-between">
            <span className="text-[15px] font-medium text-[var(--fg)]">{tituloMes}</span>
            <div className="flex items-center">
              <button
                type="button"
                aria-label="Mês anterior"
                onClick={() => setMes(new Date(mes.getFullYear(), mes.getMonth() - 1, 1))}
                className="focus-ring flex size-8 cursor-pointer items-center justify-center rounded-full text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
              >
                <ChevronUp className="size-4" />
              </button>
              <button
                type="button"
                aria-label="Próximo mês"
                onClick={() => setMes(new Date(mes.getFullYear(), mes.getMonth() + 1, 1))}
                className="focus-ring flex size-8 cursor-pointer items-center justify-center rounded-full text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
              >
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
              const doMes = d.getMonth() === mes.getMonth();
              const extremo = (inicio && mesmoDia(d, inicio)) || (fim && mesmoDia(d, fim));
              const noIntervalo = inicio && fim && d > inicio && d < fim;
              return (
                <div key={d.toISOString()} className={`flex justify-center ${noIntervalo ? "bg-[var(--brand-soft)]" : ""}`}>
                  <button
                    type="button"
                    onClick={() => escolher(d)}
                    aria-label={d.toLocaleDateString("pt-BR", { dateStyle: "long" })}
                    aria-pressed={!!extremo}
                    className={`focus-ring flex size-9 cursor-pointer items-center justify-center rounded-full transition-colors ${
                      extremo
                        ? "bg-[var(--fg)] font-semibold text-[var(--surface-solid)]"
                        : `hover:bg-[var(--surface-hover)] ${doMes ? "text-[var(--fg)]" : "text-[var(--fg-muted)]/45"} ${mesmoDia(d, hoje) ? "ring-1 ring-inset ring-[var(--brand)]" : ""}`
                    }`}
                  >
                    {d.getDate()}
                  </button>
                </div>
              );
            })}
          </div>

          <div className="mt-3 flex items-center justify-between border-t border-[var(--border)] pt-3 text-xs">
            <button
              type="button"
              onClick={() => {
                setMes(new Date(hoje.getFullYear(), hoje.getMonth(), 1));
                escolher(new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate()));
              }}
              className="focus-ring inline-flex cursor-pointer items-center gap-1 rounded-md font-medium text-[var(--brand)] hover:opacity-80"
            >
              <CalendarDays className="size-3.5" /> Hoje
            </button>
            <button
              type="button"
              onClick={() => {
                onChange("", "");
                setCampo("de");
              }}
              className="focus-ring cursor-pointer rounded-md font-medium text-[var(--fg-muted)] hover:text-[var(--fg)]"
            >
              Limpar
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
