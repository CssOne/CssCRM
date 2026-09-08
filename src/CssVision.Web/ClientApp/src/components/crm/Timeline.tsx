import { ArrowRightLeft, FileText, MessageSquare, Pencil, PhoneCall, ThumbsDown, ThumbsUp, UserPlus2 } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { formatarDataHora } from "../../lib/format";
import { TipoEventoTimeline, type LeadTimelineItem } from "../../lib/types";

const iconePorTipo: Record<TipoEventoTimeline, LucideIcon> = {
  [TipoEventoTimeline.LeadCriado]: UserPlus2,
  [TipoEventoTimeline.LeadAtualizado]: Pencil,
  [TipoEventoTimeline.TrocaResponsavel]: UserPlus2,
  [TipoEventoTimeline.MudancaEtapa]: ArrowRightLeft,
  [TipoEventoTimeline.Atividade]: PhoneCall,
  [TipoEventoTimeline.Nota]: MessageSquare,
  [TipoEventoTimeline.Proposta]: FileText,
  [TipoEventoTimeline.OportunidadeGanha]: ThumbsUp,
  [TipoEventoTimeline.OportunidadePerdida]: ThumbsDown,
  [TipoEventoTimeline.OportunidadeCriada]: FileText,
};

export function Timeline({ itens }: { itens: LeadTimelineItem[] }) {
  if (itens.length === 0) {
    return <p className="text-sm text-[var(--fg-muted)]">Nenhum evento registrado ainda.</p>;
  }

  return (
    <ol className="space-y-4">
      {itens.map((item) => {
        const Icone = iconePorTipo[item.tipo] ?? FileText;
        return (
          <li key={item.id} className="flex gap-3">
            <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-[var(--brand-soft)] text-[var(--brand)]">
              <Icone className="size-4" aria-hidden />
            </div>
            <div className="min-w-0 flex-1 pb-1">
              <p className="text-sm font-medium text-[var(--fg)]">{item.titulo}</p>
              {item.descricao && <p className="text-sm text-[var(--fg-muted)]">{item.descricao}</p>}
              <p className="text-xs text-[var(--fg-muted)]">
                {formatarDataHora(item.ocorridoEm)} {item.usuarioNome && `· ${item.usuarioNome}`}
              </p>
            </div>
          </li>
        );
      })}
    </ol>
  );
}
