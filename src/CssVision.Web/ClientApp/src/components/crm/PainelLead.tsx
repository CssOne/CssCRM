import { Maximize2, X } from "lucide-react";
import { useEffect } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { PARAM_PAINEL_LEAD, useFecharPainelLead } from "../../lib/painelLead";
import { LeadDetailConteudo } from "../../pages/crm/LeadDetail";
import { IconButton } from "../ui";

/**
 * Lead aberto numa aba lateral à direita (como no Notion), por cima da tela atual — o quadro, a
 * agenda etc. continuam lá atrás. Aberto por useAbrirLead (?lead=<id>); Esc ou o X fecham.
 */
export function PainelLead() {
  const [params] = useSearchParams();
  const leadId = params.get(PARAM_PAINEL_LEAD);
  const fechar = useFecharPainelLead();

  useEffect(() => {
    if (!leadId) return;
    const aoTeclar = (e: KeyboardEvent) => {
      // Esc de um modal aberto dentro do painel fecha só o modal.
      if (e.key === "Escape" && !document.querySelector("[role=dialog][aria-modal=true]:not([data-painel-lead])")) fechar();
    };
    document.addEventListener("keydown", aoTeclar);
    return () => document.removeEventListener("keydown", aoTeclar);
  }, [leadId, fechar]);

  if (!leadId) return null;

  return (
    <div className="fixed inset-0 z-40 flex justify-end" role="presentation">
      <div className="absolute inset-0 bg-black/30" onClick={fechar} aria-hidden />
      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Lead"
        data-painel-lead
        className="relative flex h-full w-full max-w-4xl flex-col border-l border-[var(--border)] bg-[var(--bg)] shadow-2xl sm:w-[min(900px,92vw)]"
      >
        <div className="flex items-center justify-between gap-2 border-b border-[var(--border)] bg-[var(--surface)] px-4 py-2">
          <Link
            to={`/app/crm/leads/${leadId}`}
            onClick={fechar}
            className="flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
            title="Abrir em página inteira"
          >
            <Maximize2 className="size-3.5" /> Abrir em página inteira
          </Link>
          <IconButton label="Fechar" onClick={fechar}>
            <X className="size-4" />
          </IconButton>
        </div>
        <div className="flex-1 overflow-y-auto p-4 lg:p-6">
          <LeadDetailConteudo key={leadId} leadId={leadId} noPainel />
        </div>
      </aside>
    </div>
  );
}
