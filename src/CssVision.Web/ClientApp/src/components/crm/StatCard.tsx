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
  const cores: Record<string, string> = {
    neutral: "var(--fg-muted)",
    success: "var(--success)",
    warning: "var(--warning)",
    danger: "var(--danger)",
    brand: "var(--brand)",
  };
  const cor = cores[tom];

  return (
    <Card className="group relative overflow-hidden !rounded-xl p-3 transition-[transform,box-shadow] duration-200 hover:-translate-y-0.5 hover:shadow-md">
      {/* Brilho suave da cor do indicador no canto */}
      <span aria-hidden className="pointer-events-none absolute -right-6 -top-6 size-24 rounded-full opacity-60 blur-2xl" style={{ backgroundColor: `color-mix(in srgb, ${cor} 20%, transparent)` }} />
      <div className="relative flex items-start justify-between gap-2">
        <p className="truncate text-xs font-medium text-[var(--fg-muted)]">{titulo}</p>
        <span className="flex size-7 shrink-0 items-center justify-center rounded-lg" style={{ backgroundColor: `color-mix(in srgb, ${cor} 15%, transparent)`, color: cor }}>
          <Icone className="size-3.5" aria-hidden />
        </span>
      </div>
      <p className="relative mt-0.5 text-xl font-bold leading-tight tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{valor}</p>
      {subtitulo && <p className="relative mt-0.5 truncate text-xs text-[var(--fg-muted)]">{subtitulo}</p>}
    </Card>
  );
}
