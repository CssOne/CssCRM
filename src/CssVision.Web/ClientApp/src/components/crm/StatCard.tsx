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
    neutral: "bg-[var(--surface-hover)] text-[var(--fg-muted)]",
    success: "bg-[var(--success-soft)] text-[var(--success)]",
    warning: "bg-[var(--warning-soft)] text-[var(--warning)]",
    danger: "bg-[var(--danger-soft)] text-[var(--danger)]",
    brand: "bg-[var(--brand-soft)] text-[var(--brand)]",
  };

  return (
    <Card className="flex items-start gap-3 p-4">
      <div className={`flex size-10 shrink-0 items-center justify-center rounded-lg ${tons[tom]}`}>
        <Icone className="size-5" aria-hidden />
      </div>
      <div className="min-w-0">
        <p className="truncate text-xs font-medium text-[var(--fg-muted)]">{titulo}</p>
        <p className="text-xl font-semibold text-[var(--fg)]">{valor}</p>
        {subtitulo && <p className="truncate text-xs text-[var(--fg-muted)]">{subtitulo}</p>}
      </div>
    </Card>
  );
}
