import type { LucideIcon } from "lucide-react";
import { Card } from "../ui";

export function PortalStatCard({
  titulo,
  valor,
  icone: Icone,
  subtitulo,
  progresso,
}: {
  titulo: string;
  valor: string;
  icone: LucideIcon;
  subtitulo?: string;
  /** 0-100: quando informado, renderiza uma barra de progresso abaixo do valor (ex: meta do mês). */
  progresso?: number;
}) {
  return (
    <Card className="p-5">
      <p className="flex items-center gap-1.5 text-xs font-medium text-[var(--fg-muted)]">
        <Icone className="size-3.5 shrink-0 text-[var(--brand)]" aria-hidden />
        {titulo}
      </p>
      <p className="mt-2 text-[1.75rem] font-semibold leading-none tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{valor}</p>

      {progresso !== undefined ? (
        <div className="mt-4">
          <div className="h-1.5 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
            <div className="h-full rounded-full bg-[var(--brand)] transition-all" style={{ width: `${Math.min(100, Math.max(0, progresso))}%` }} />
          </div>
          {subtitulo && <p className="mt-2 text-xs text-[var(--fg-muted)]">{subtitulo}</p>}
        </div>
      ) : (
        subtitulo && <p className="mt-2 text-xs text-[var(--success)]">{subtitulo}</p>
      )}
    </Card>
  );
}
