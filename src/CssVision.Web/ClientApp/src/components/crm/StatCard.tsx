import type { LucideIcon } from "lucide-react";
import { Card } from "../ui";

export function StatCard({
  titulo,
  valor,
  icone: Icone,
  tom = "neutral",
  subtitulo,
}: {
  titulo: string;
  valor: string;
  icone: LucideIcon;
  tom?: "neutral" | "success" | "warning" | "danger" | "brand";
  subtitulo?: string;
}) {
  const tons: Record<string, string> = {
    neutral: "text-[var(--fg-muted)]",
    success: "text-[var(--success)]",
    warning: "text-[var(--warning)]",
    danger: "text-[var(--danger)]",
    brand: "text-[var(--brand)]",
  };

  return (
    <Card className="p-5">
      <p className="flex items-center gap-1.5 truncate text-xs font-medium text-[var(--fg-muted)]">
        <Icone className={`size-3.5 shrink-0 ${tons[tom]}`} aria-hidden />
        {titulo}
      </p>
      <p className="mt-2 text-[1.75rem] font-semibold leading-none tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{valor}</p>
      {subtitulo && <p className="mt-2 truncate text-xs text-[var(--fg-muted)]">{subtitulo}</p>}
    </Card>
  );
}
