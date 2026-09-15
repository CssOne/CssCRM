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
      <div className="flex items-start justify-between gap-3">
        <p className="text-sm font-medium text-[var(--fg-muted)]">{titulo}</p>
        <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-[var(--brand-soft)] text-[var(--brand)]">
          <Icone className="size-5" aria-hidden />
        </div>
      </div>
      <p className="mt-2 text-3xl font-bold text-[var(--fg)]">{valor}</p>

      {progresso !== undefined ? (
        <div className="mt-3">
          <div className="h-2 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
            <div className="h-full rounded-full bg-[var(--brand)] transition-all" style={{ width: `${Math.min(100, Math.max(0, progresso))}%` }} />
          </div>
          {subtitulo && <p className="mt-1.5 text-xs text-[var(--fg-muted)]">{subtitulo}</p>}
        </div>
      ) : (
        subtitulo && <p className="mt-1 text-xs text-[var(--success)]">{subtitulo}</p>
      )}
    </Card>
  );
}
