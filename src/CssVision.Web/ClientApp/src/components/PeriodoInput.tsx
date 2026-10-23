import { CalendarDays, CalendarRange, Clock, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Calendario, Flutuante, GradeDeMeses, exibirData, paraData, paraIso } from "./Calendario";

const campoBase =
  "focus-ring h-9 min-w-0 cursor-pointer rounded-lg border bg-[var(--field)] px-3 text-left text-sm [font-variant-numeric:tabular-nums]";

const dia = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
const somarDias = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);

/** Atalhos de período do topo do seletor. */
const ATALHOS: { rotulo: string; calcular: (hoje: Date) => [Date, Date] }[] = [
  { rotulo: "Hoje", calcular: (h) => [h, h] },
  { rotulo: "Ontem", calcular: (h) => [somarDias(h, -1), somarDias(h, -1)] },
  { rotulo: "Últimos 7 dias", calcular: (h) => [somarDias(h, -6), h] },
  { rotulo: "Últimos 30 dias", calcular: (h) => [somarDias(h, -29), h] },
  { rotulo: "Este mês", calcular: (h) => [new Date(h.getFullYear(), h.getMonth(), 1), new Date(h.getFullYear(), h.getMonth() + 1, 0)] },
  { rotulo: "Mês anterior", calcular: (h) => [new Date(h.getFullYear(), h.getMonth() - 1, 1), new Date(h.getFullYear(), h.getMonth(), 0)] },
  { rotulo: "Este ano", calcular: (h) => [new Date(h.getFullYear(), 0, 1), new Date(h.getFullYear(), 11, 31)] },
];

/** "22102026" → "22/10/2026" enquanto digita; só números e barras. */
function mascararData(texto: string) {
  const n = texto.replace(/\D/g, "").slice(0, 8);
  if (n.length <= 2) return n;
  if (n.length <= 4) return `${n.slice(0, 2)}/${n.slice(2)}`;
  return `${n.slice(0, 2)}/${n.slice(2, 4)}/${n.slice(4)}`;
}

/** "22/10/2026" → "2026-10-22" (ou "" se não for uma data real). */
function lerDataDigitada(texto: string) {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(texto);
  if (!m) return "";
  const d = new Date(Number(m[3]), Number(m[2]) - 1, Number(m[1]));
  return d.getFullYear() === Number(m[3]) && d.getMonth() === Number(m[2]) - 1 && d.getDate() === Number(m[1]) ? paraIso(d) : "";
}

/** Campo de texto dd/mm/aaaa da janela do período: aceita digitação com máscara e só repassa datas completas e válidas. */
function CampoDigitado({ rotulo, valor, onValor }: { rotulo: string; valor: string; onValor: (iso: string) => void }) {
  const [texto, setTexto] = useState(exibirData(valor));
  useEffect(() => setTexto(exibirData(valor)), [valor]);
  return (
    <label className="block min-w-0 flex-1">
      <span className="mb-0.5 block text-[10px] font-medium text-[var(--fg-muted)]">{rotulo}</span>
      <input
        inputMode="numeric"
        autoComplete="off"
        placeholder="dd/mm/aaaa"
        value={texto}
        onChange={(e) => {
          const t = mascararData(e.target.value);
          setTexto(t);
          if (t === "") onValor("");
          else {
            const iso = lerDataDigitada(t);
            if (iso) onValor(iso);
          }
        }}
        className="focus-ring h-8 w-full min-w-0 rounded-lg border border-[var(--border)] bg-[var(--field)] px-2.5 text-[13px] text-[var(--fg)] placeholder:text-[var(--fg-muted)] [font-variant-numeric:tabular-nums]"
      />
    </label>
  );
}

/**
 * Filtro de período: um campo que abre a janela com atalhos (hoje, últimos 7 dias, este mês…), o calendário (os dias entre as
 * duas datas ficam destacados) e os campos De/Até para digitar. Os valores são "aaaa-mm-dd" (ou vazio), como nos campos de
 * data nativos que ele substitui.
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
  // Próximo clique no calendário define o início ("de") ou o fim ("ate").
  const [campo, setCampo] = useState<"de" | "ate">("de");
  const [mes, setMes] = useState(() => {
    const base = paraData(de) ?? paraData(ate) ?? new Date();
    return new Date(base.getFullYear(), base.getMonth(), 1);
  });
  const raiz = useRef<HTMLDivElement>(null);

  function abrir() {
    const base = paraData(de) ?? paraData(ate);
    if (base) setMes(new Date(base.getFullYear(), base.getMonth(), 1));
    setCampo("de");
    setAberto(true);
  }

  function escolher(d: Date) {
    const iso = paraIso(d);
    if (campo === "de") {
      // Início depois do fim: o fim é descartado em vez de ficar um período invertido.
      onChange(iso, ate && iso > ate ? "" : ate);
      setCampo("ate");
    } else {
      if (de && iso < de) onChange(iso, de);
      else onChange(de, iso);
      setCampo("de");
    }
  }

  function atalho(calcular: (hoje: Date) => [Date, Date]) {
    const [i, f] = calcular(dia(new Date()));
    onChange(paraIso(i), paraIso(f));
    setMes(new Date(i.getFullYear(), i.getMonth(), 1));
    setCampo("de");
  }

  const atalhoAtivo = ATALHOS.find((a) => {
    const [i, f] = a.calcular(dia(new Date()));
    return de === paraIso(i) && ate === paraIso(f);
  })?.rotulo;
  const resumo = de || ate ? `${de ? exibirData(de) : "…"} – ${ate ? exibirData(ate) : "…"}` : "";

  return (
    <div ref={raiz} className="relative">
      <div className="relative">
        <button
          type="button"
          onClick={() => (aberto ? setAberto(false) : abrir())}
          aria-label={ariaLabel}
          aria-haspopup="dialog"
          aria-expanded={aberto}
          className={`${campoBase.replace("rounded-lg", "")} flex h-8 w-full items-center gap-1.5 rounded-full px-2.5 pr-8 text-[13px] ${aberto ? "border-[var(--brand)] ring-2 ring-[var(--brand)]/25" : "border-[var(--border)]"} ${
            resumo ? "text-[var(--fg)]" : "text-[var(--fg-muted)]"
          }`}
        >
          <CalendarDays className="size-3.5 shrink-0 text-[var(--fg-muted)]" aria-hidden />
          <span className="truncate">{resumo || "Selecionar período"}</span>
        </button>
        {resumo && (
          <button
            type="button"
            aria-label="Limpar período"
            title="Limpar período"
            onClick={() => onChange("", "")}
            className="focus-ring absolute right-1.5 top-1/2 flex size-5 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
          >
            <X className="size-3.5" />
          </button>
        )}
      </div>

      <Flutuante aberto={aberto} ancora={raiz} onFechar={() => setAberto(false)} rotulo="Selecionar período" largura={300}>
        <div className="mb-2 flex flex-wrap gap-1">
          {ATALHOS.map((a) => (
            <button
              key={a.rotulo}
              type="button"
              aria-pressed={atalhoAtivo === a.rotulo}
              onClick={() => atalho(a.calcular)}
              className={`focus-ring cursor-pointer rounded-full px-2 py-0.5 text-xs font-medium transition-colors ${
                atalhoAtivo === a.rotulo
                  ? "bg-[var(--brand)] text-white"
                  : "bg-[var(--surface-hover)] text-[var(--fg)] hover:bg-[var(--brand-soft)] hover:text-[var(--brand)]"
              }`}
            >
              {a.rotulo}
            </button>
          ))}
        </div>
        <Calendario mes={mes} onMes={setMes} inicio={paraData(de)} fim={paraData(ate)} onEscolher={escolher} />
        <div className="mt-2 flex gap-2 border-t border-[var(--border)] pt-2">
          <CampoDigitado
            rotulo="De"
            valor={de}
            onValor={(iso) => {
              onChange(iso, iso && ate && iso > ate ? "" : ate);
              const d = paraData(iso);
              if (d) setMes(new Date(d.getFullYear(), d.getMonth(), 1));
            }}
          />
          <CampoDigitado
            rotulo="Até"
            valor={ate}
            onValor={(iso) => {
              onChange(iso && de && iso < de ? iso : de, iso && de && iso < de ? de : iso);
              const d = paraData(iso);
              if (d) setMes(new Date(d.getFullYear(), d.getMonth(), 1));
            }}
          />
        </div>
        <div className="mt-2 flex items-center justify-between">
          <button
            type="button"
            onClick={() => {
              onChange("", "");
              setCampo("de");
            }}
            disabled={!de && !ate}
            className="focus-ring cursor-pointer rounded-md px-1 text-sm font-medium text-[var(--fg-muted)] hover:text-[var(--fg)] disabled:cursor-default disabled:opacity-50"
          >
            Limpar
          </button>
          <button
            type="button"
            onClick={() => setAberto(false)}
            className="focus-ring cursor-pointer rounded-lg bg-[var(--brand)] px-4 py-1.5 text-[13px] font-semibold text-[var(--brand-fg)] transition-[filter] hover:brightness-110"
          >
            Concluir
          </button>
        </div>
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
export function MesInput({
  value,
  onChange,
  className = "",
  ariaLabel = "Mês",
  id,
  disabled,
}: {
  value: string;
  onChange: (valor: string) => void;
  className?: string;
  ariaLabel?: string;
  id?: string;
  disabled?: boolean;
}) {
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
        id={id}
        disabled={disabled}
        aria-label={ariaLabel}
        aria-haspopup="dialog"
        aria-expanded={aberto}
        onClick={() => {
          setAno(Number(value.slice(0, 4)) || new Date().getFullYear());
          setAberto((v) => !v);
        }}
        className={`${campoBase} flex w-full items-center justify-between gap-2 disabled:opacity-50 ${aberto ? "border-[var(--brand)] ring-2 ring-[var(--brand)]/25" : "border-[var(--border)]"} ${
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

/** Campo de horário (substitui <input type="time">): abre duas colunas, horas e minutos. Valor "hh:mm" ou vazio. */
export function HoraInput({
  value,
  onChange,
  className = "",
  ariaLabel = "Horário",
  id,
  disabled,
  compacto = false,
}: {
  value: string;
  onChange: (valor: string) => void;
  className?: string;
  ariaLabel?: string;
  id?: string;
  disabled?: boolean;
  /** Versão pequena (cartões e listas): sem o ícone e com a altura menor. */
  compacto?: boolean;
}) {
  const [aberto, setAberto] = useState(false);
  const ancora = useRef<HTMLButtonElement>(null);
  const colunaHoras = useRef<HTMLDivElement>(null);
  const colunaMinutos = useRef<HTMLDivElement>(null);
  const [hh, mm] = /^\d{2}:\d{2}/.test(value) ? [value.slice(0, 2), value.slice(3, 5)] : ["", ""];
  const horas = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, "0"));
  const minutos = Array.from(new Set([...Array.from({ length: 12 }, (_, i) => String(i * 5).padStart(2, "0")), ...(mm ? [mm] : [])])).sort();

  // Ao abrir, a hora e o minuto escolhidos ficam à vista.
  useEffect(() => {
    if (!aberto) return;
    const t = window.setTimeout(() => {
      colunaHoras.current?.querySelector('[aria-pressed="true"]')?.scrollIntoView({ block: "center" });
      colunaMinutos.current?.querySelector('[aria-pressed="true"]')?.scrollIntoView({ block: "center" });
    }, 0);
    return () => window.clearTimeout(t);
  }, [aberto]);

  const celula = (texto: string, ativo: boolean, aoClicar: () => void) => (
    <button
      key={texto}
      type="button"
      aria-pressed={ativo}
      onClick={aoClicar}
      className={`focus-ring flex h-8 w-full cursor-pointer items-center justify-center rounded-lg text-sm [font-variant-numeric:tabular-nums] transition-colors ${
        ativo ? "bg-[var(--brand)] font-semibold text-white" : "text-[var(--fg)] hover:bg-[var(--brand-soft)] hover:text-[var(--brand)]"
      }`}
    >
      {texto}
    </button>
  );

  return (
    <>
      <button
        ref={ancora}
        type="button"
        id={id}
        disabled={disabled}
        aria-label={ariaLabel}
        aria-haspopup="dialog"
        aria-expanded={aberto}
        onClick={() => setAberto((v) => !v)}
        className={`${campoBase} ${compacto ? "h-7 w-[4.5rem] justify-center rounded-md px-1 text-xs" : "w-full"} flex items-center justify-between gap-2 disabled:opacity-50 ${
          aberto ? "border-[var(--brand)] ring-2 ring-[var(--brand)]/25" : "border-[var(--border)]"
        } ${value ? "text-[var(--fg)]" : "text-[var(--fg-muted)]"} ${className}`}
      >
        <span>{value ? `${hh}:${mm}` : "--:--"}</span>
        {!compacto && <Clock className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />}
      </button>
      <Flutuante aberto={aberto} ancora={ancora} onFechar={() => setAberto(false)} rotulo="Escolher horário" largura={208} className="p-3">
        <div className="grid grid-cols-2 gap-2">
          <div>
            <p className="pb-1 text-center text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Hora</p>
            <div ref={colunaHoras} className="max-h-48 space-y-0.5 overflow-y-auto pr-1">
              {horas.map((h) => celula(h, h === hh, () => onChange(`${h}:${mm || "00"}`)))}
            </div>
          </div>
          <div>
            <p className="pb-1 text-center text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Minuto</p>
            <div ref={colunaMinutos} className="max-h-48 space-y-0.5 overflow-y-auto pr-1">
              {minutos.map((m) =>
                celula(m, m === mm, () => {
                  onChange(`${hh || "09"}:${m}`);
                  setAberto(false);
                })
              )}
            </div>
          </div>
        </div>
        <div className="mt-3 flex items-center justify-between border-t border-[var(--border)] pt-3 text-xs">
          <button
            type="button"
            onClick={() => {
              const h = new Date();
              onChange(`${String(h.getHours()).padStart(2, "0")}:${String(h.getMinutes()).padStart(2, "0")}`);
              setAberto(false);
            }}
            className="focus-ring inline-flex cursor-pointer items-center gap-1 rounded-md font-medium text-[var(--brand)] hover:opacity-80"
          >
            <Clock className="size-3.5" /> Agora
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
        </div>
      </Flutuante>
    </>
  );
}

/** Data e hora lado a lado (substitui <input type="datetime-local">). Valor "aaaa-mm-ddThh:mm" ou vazio. */
export function DataHoraInput({ value, onChange, id, ariaLabel = "Data e hora" }: { value: string; onChange: (valor: string) => void; id?: string; ariaLabel?: string }) {
  const [data, hora] = value.includes("T") ? [value.slice(0, 10), value.slice(11, 16)] : [value.slice(0, 10), ""];
  return (
    <div className="flex items-center gap-2">
      <div className="min-w-0 flex-1">
        <DataInput id={id} ariaLabel={`${ariaLabel}: data`} value={data} onChange={(d) => onChange(d ? `${d}T${hora || "09:00"}` : "")} />
      </div>
      <div className="w-28 shrink-0">
        <HoraInput ariaLabel={`${ariaLabel}: hora`} value={hora} disabled={!data} onChange={(h) => onChange(data ? `${data}T${h || "00:00"}` : "")} />
      </div>
    </div>
  );
}
