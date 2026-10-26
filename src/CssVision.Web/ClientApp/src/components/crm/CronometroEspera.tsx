import { Timer } from "lucide-react";
import { useSyncExternalStore } from "react";

// Um único relógio de 1 s para todos os cartões: o quadro pode ter centenas de leads em "Sem etapa".
const ouvintes = new Set<() => void>();
let agora = Date.now();
let relogio: ReturnType<typeof setInterval> | null = null;

function assinar(ouvinte: () => void) {
  ouvintes.add(ouvinte);
  if (!relogio) {
    agora = Date.now();
    relogio = setInterval(() => {
      agora = Date.now();
      ouvintes.forEach((o) => o());
    }, 1000);
  }
  return () => {
    ouvintes.delete(ouvinte);
    if (ouvintes.size === 0 && relogio) {
      clearInterval(relogio);
      relogio = null;
    }
  };
}

/** Minutos até o lead estar "frio": a linha esvazia e esfria nesse tempo. */
const MINUTOS_ATE_FRIO = 120;

function formatarEspera(segundos: number) {
  const h = Math.floor(segundos / 3600);
  const m = Math.floor((segundos % 3600) / 60);
  const s = segundos % 60;
  const dois = (n: number) => String(n).padStart(2, "0");
  if (h >= 24) return `${Math.floor(h / 24)}d ${dois(h % 24)}h ${dois(m)}min`;
  return `${dois(h)}:${dois(m)}:${dois(s)}`;
}

function rotuloTemperatura(minutos: number) {
  if (minutos < 15) return "Quente";
  if (minutos < 45) return "Morno";
  if (minutos < MINUTOS_ATE_FRIO) return "Esfriando";
  return "Frio";
}

/**
 * Cronômetro do lead que chegou do tráfego pago e segue em "Sem etapa" (sem atendimento), com uma linha que vai de quente (vermelho)
 * a frio (azul) conforme o tempo passa: a cor muda e a linha encurta até ficar quase vazia.
 */
export function CronometroEspera({ desde }: { desde: string }) {
  const agoraMs = useSyncExternalStore(assinar, () => agora);
  const inicio = new Date(desde).getTime();
  const segundos = Math.max(0, Math.floor((agoraMs - inicio) / 1000));
  const minutos = segundos / 60;
  const frio = Math.min(1, minutos / MINUTOS_ATE_FRIO); // 0 = recém-chegado, 1 = frio
  // Matiz de 5° (vermelho) a 215° (azul), passando por laranja e amarelo; o brilho cai um pouco no frio para não "gritar".
  const matiz = 5 + frio * 210;
  const cor = `hsl(${matiz.toFixed(0)} 82% ${(52 - frio * 6).toFixed(0)}%)`;
  const largura = 100 - frio * 88; // nunca zera: o lead continua lá

  return (
    <div
      className="mt-2 rounded-md px-2 pb-1.5 pt-1"
      style={{ backgroundColor: `color-mix(in srgb, ${cor} 9%, transparent)`, boxShadow: `inset 0 0 0 1px color-mix(in srgb, ${cor} 24%, transparent)` }}
      title="Tempo que o lead está em Sem etapa, sem atendimento"
    >
      <div className="flex items-center justify-between gap-2">
        <p className="flex items-center gap-1 text-[12px] font-bold leading-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">
          <Timer className="size-3 shrink-0" aria-hidden style={{ color: cor }} />
          {formatarEspera(segundos)}
        </p>
        <span
          className="shrink-0 rounded-full px-1.5 py-[2px] text-[9px] font-bold uppercase leading-none tracking-wide"
          style={{ color: `color-mix(in srgb, ${cor} 80%, var(--fg))`, backgroundColor: `color-mix(in srgb, ${cor} 18%, transparent)` }}
        >
          {rotuloTemperatura(minutos)}
        </span>
      </div>
      <div className="relative mt-1" role="img" aria-label={`${rotuloTemperatura(minutos)}: ${formatarEspera(segundos)} sem atendimento`}>
        <div className="h-1 w-full overflow-hidden rounded-full bg-[color-mix(in_srgb,var(--fg)_10%,transparent)]">
          <div className="h-full rounded-full transition-[width,background-color] duration-1000 ease-linear" style={{ width: `${largura}%`, backgroundColor: cor }} />
        </div>
        {/* Marcas dos limites quente / morno / esfriando */}
        {[15, 45].map((m) => (
          <span key={m} aria-hidden className="absolute top-0 h-1 w-px bg-[var(--surface)]" style={{ left: `${100 - (m / MINUTOS_ATE_FRIO) * 88}%` }} />
        ))}
      </div>
    </div>
  );
}
