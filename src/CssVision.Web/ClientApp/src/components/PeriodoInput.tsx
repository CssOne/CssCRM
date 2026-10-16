import { CalendarDays, CalendarRange, X } from "lucide-react";
import { useRef, useState } from "react";
import { Calendario, Flutuante, GradeDeMeses, exibirData, paraData, paraIso } from "./Calendario";

const campoBase =
  "focus-ring h-9 min-w-0 cursor-pointer rounded-lg border bg-[var(--field)] px-3 text-left text-sm [font-variant-numeric:tabular-nums]";

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

  const botaoCampo = (qual: "de" | "ate", valor: string) => (
    <button
      type="button"
      onClick={() => abrir(qual)}
      aria-label={`${ariaLabel}: ${qual === "de" ? "início" : "fim"}`}
      className={`${campoBase} flex-1 ${aberto && campo === qual ? "border-[var(--brand)] ring-2 ring-[var(--brand)]/25" : "border-[var(--border)]"} ${
        valor ? "text-[var(--fg)]" : "text-[var(--fg-muted)]"
      }`}
    >
      {valor ? exibirData(valor) : "dd/mm/aaaa"}
    </button>
  );

  return (
    <div ref={raiz} className="relative">
      <div className="flex items-center gap-2">
        {botaoCampo("de", de)}
        {botaoCampo("ate", ate)}
        {(de || ate) && (
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

      <Flutuante aberto={aberto} ancora={raiz} onFechar={() => setAberto(false)} rotulo="Calendário">
        <Calendario
          mes={mes}
          onMes={setMes}
          inicio={paraData(de)}
          fim={paraData(ate)}
          onEscolher={escolher}
          rodape={
            <>
              <button
                type="button"
                onClick={() => {
                  const h = new Date();
                  setMes(new Date(h.getFullYear(), h.getMonth(), 1));
                  escolher(new Date(h.getFullYear(), h.getMonth(), h.getDate()));
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
            </>
          }
        />
      </Flutuante>
    </div>
  );
}

/** Campo de uma data só (substitui <input type="date">). Valor "aaaa-mm-dd" ou vazio; `min`/`max` desabilitam dias fora do limite. */
export function DataInput({
  value,
  onChange,
  min,
  max,
  id,
  className = "",
  ariaLabel,
  invalid,
  disabled,
}: {
  value: string;
  onChange: (valor: string) => void;
  min?: string;
  max?: string;
  id?: string;
  className?: string;
  ariaLabel?: string;
  invalid?: boolean;
  disabled?: boolean;
}) {
  const [aberto, setAberto] = useState(false);
  const [mes, setMes] = useState(() => {
    const base = paraData(value) ?? new Date();
    return new Date(base.getFullYear(), base.getMonth(), 1);
  });
  const ancora = useRef<HTMLButtonElement>(null);

  return (
    <>
      <button
        ref={ancora}
        type="button"
        id={id}
        disabled={disabled}
        aria-label={ariaLabel}
        aria-invalid={invalid}
        aria-haspopup="dialog"
        aria-expanded={aberto}
        onClick={() => {
          const base = paraData(value);
          if (base) setMes(new Date(base.getFullYear(), base.getMonth(), 1));
          setAberto((v) => !v);
        }}
        className={`${campoBase} flex w-full items-center justify-between gap-2 disabled:opacity-50 ${
          invalid ? "border-[var(--danger)]" : aberto ? "border-[var(--brand)] ring-2 ring-[var(--brand)]/25" : "border-[var(--border)]"
        } ${value ? "text-[var(--fg)]" : "text-[var(--fg-muted)]"} ${className}`}
      >
        <span>{value ? exibirData(value) : "dd/mm/aaaa"}</span>
        <CalendarRange className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />
      </button>
      <Flutuante aberto={aberto} ancora={ancora} onFechar={() => setAberto(false)} rotulo="Calendário">
        <Calendario
          mes={mes}
          onMes={setMes}
          inicio={paraData(value)}
          min={min}
          max={max}
          onEscolher={(d) => {
            onChange(paraIso(d));
            setAberto(false);
          }}
          rodape={
            <>
              <button
                type="button"
                onClick={() => {
                  const h = new Date();
                  const iso = paraIso(new Date(h.getFullYear(), h.getMonth(), h.getDate()));
                  if ((!min || iso >= min) && (!max || iso <= max)) {
                    onChange(iso);
                    setAberto(false);
                  }
                }}
                className="focus-ring inline-flex cursor-pointer items-center gap-1 rounded-md font-medium text-[var(--brand)] hover:opacity-80"
              >
                <CalendarDays className="size-3.5" /> Hoje
              </button>
              <button
                type="button"
                onClick={() => {
                  onChange("");
                  setAberto(false);
                }}
                className="focus-ring cursor-pointer rounded-md font-medium text-[var(--fg-muted)] hover:text-[var(--fg)]"
              >
                Limpar
              </button>
            </>
          }
        />
      </Flutuante>
    </>
  );
}

/** Seletor de mês (substitui <input type="month">). Valor "aaaa-mm". */
export function MesInput({ value, onChange, className = "", ariaLabel = "Mês" }: { value: string; onChange: (valor: string) => void; className?: string; ariaLabel?: string }) {
  const [aberto, setAberto] = useState(false);
  const [ano, setAno] = useState(() => Number(value.slice(0, 4)) || new Date().getFullYear());
  const ancora = useRef<HTMLButtonElement>(null);
  const rotulo = /^\d{4}-\d{2}/.test(value)
    ? new Date(Number(value.slice(0, 4)), Number(value.slice(5, 7)) - 1, 1).toLocaleDateString("pt-BR", { month: "long", year: "numeric" })
    : "";

  return (
    <>
      <button
        ref={ancora}
        type="button"
        aria-label={ariaLabel}
        aria-haspopup="dialog"
        aria-expanded={aberto}
        onClick={() => {
          setAno(Number(value.slice(0, 4)) || new Date().getFullYear());
          setAberto((v) => !v);
        }}
        className={`${campoBase} flex w-full items-center justify-between gap-2 ${aberto ? "border-[var(--brand)] ring-2 ring-[var(--brand)]/25" : "border-[var(--border)]"} ${
          rotulo ? "text-[var(--fg)]" : "text-[var(--fg-muted)]"
        } ${className}`}
      >
        <span>{rotulo ? rotulo.charAt(0).toUpperCase() + rotulo.slice(1) : "Escolha o mês"}</span>
        <CalendarRange className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />
      </button>
      <Flutuante aberto={aberto} ancora={ancora} onFechar={() => setAberto(false)} rotulo="Escolher mês" largura={264}>
        <GradeDeMeses
          ano={ano}
          onAno={setAno}
          mesEscolhido={value.slice(0, 7)}
          onEscolher={(iso) => {
            onChange(iso);
            setAberto(false);
          }}
        />
      </Flutuante>
    </>
  );
}
