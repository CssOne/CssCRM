import { Maximize2, UserRound, X } from "lucide-react";
import { useEffect } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { PARAM_PAINEL_LEAD, useFecharPainelLead } from "../../lib/painelLead";
import { LeadDetailConteudo } from "../../pages/crm/LeadDetail";

/**
 * Lead aberto numa aba lateral à direita (como no Notion), por cima da tela atual — o quadro, a
 * agenda etc. continuam lá atrás, desfocados. Aberto por useAbrirLead (?lead=<id>); Esc, o X ou
 * um clique fora fecham.
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
    // Sem rolar a tela de trás enquanto o painel está aberto.
    const overflowAnterior = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", aoTeclar);
      document.body.style.overflow = overflowAnterior;
    };
  }, [leadId, fechar]);

  if (!leadId) return null;

  const botaoTopo =
    "focus-ring flex size-8 cursor-pointer items-center justify-center rounded-lg text-[var(--fg-muted)] transition hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]";

  return (
    <div className="fixed inset-0 z-40 flex justify-end" role="presentation">
      {/* Fundo desfocado */}
      <div className="painel-lead-fundo absolute inset-0 bg-slate-950/35 backdrop-blur-[3px]" onClick={fechar} aria-hidden />

      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Lead"
        data-painel-lead
        className="painel-lead-entrar relative m-0 flex h-full w-full flex-col overflow-hidden border-[var(--border)] bg-[var(--bg)] shadow-[0_24px_80px_-12px_rgba(15,23,42,0.45)] sm:m-2.5 sm:h-[calc(100%-1.25rem)] sm:w-[min(600px,calc(100vw-1.25rem))] sm:rounded-2xl sm:border"
      >
        <header className="flex items-center justify-between gap-2 border-b border-[var(--border)] bg-[var(--surface)]/80 px-4 py-2.5 backdrop-blur">
          <span className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-[var(--fg-muted)]">
            <span className="flex size-6 items-center justify-center rounded-md bg-[var(--brand-soft)] text-[var(--brand)]">
              <UserRound className="size-3.5" />
            </span>
            Lead
          </span>
          <div className="flex items-center gap-0.5">
            <Link to={`/app/crm/leads/${leadId}`} onClick={fechar} className={botaoTopo} title="Abrir em página inteira" aria-label="Abrir em página inteira">
              <Maximize2 className="size-4" />
            </Link>
            <button type="button" onClick={fechar} className={botaoTopo} title="Fechar (Esc)" aria-label="Fechar">
              <X className="size-4" />
            </button>
          </div>
        </header>
        <div className="flex-1 overflow-y-auto px-4 py-4 sm:px-5">
          <LeadDetailConteudo key={leadId} leadId={leadId} noPainel />
        </div>
      </aside>
    </div>
  );
}
