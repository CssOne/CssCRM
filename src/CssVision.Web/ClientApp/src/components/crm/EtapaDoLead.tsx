import { ArrowRightLeft, Car, XCircle } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { api, ApiRequestError } from "../../lib/api";
import { TipoEtapaPipeline, type LeadDetail, type LeadStage, type PipelineBoard } from "../../lib/types";
import { Badge, Button, Modal, useToast } from "../ui";
import { AdesaoCotacaoDialog } from "./AdesaoCotacaoDialog";
import { StageChangeDialog } from "./StageChangeDialog";
import { VeiculoNaoFazemosDialog } from "./VeiculoNaoFazemosDialog";
import { VendaConcluidaDialog } from "./VendaConcluidaDialog";

// Mesmas regras do quadro de leads (LeadsKanban.tsx): nomes das etapas com formulário próprio e as colunas "(Leads)"/"(Indicação)".
const ETAPA_EM_ATENDIMENTO = "Em atendimento";
const ETAPA_VENDA_CONCLUIDA = "Venda concluída";
const ETAPA_PERDIDO = "Perdido";
const ETAPA_NAO_FAZEMOS = "Não fazemos";
const ETAPA_COTACAO = "Cotação";

type Extra = { motivoPerdaId?: string; motivoPerdaObservacao?: string; veiculoNaoAtendido?: string; valorAdesao?: number };

/** Lead ou indicação — mesma etiqueta do cartão do quadro. */
function classificar(lead: LeadDetail): "lead" | "indicacao" | null {
  const tipo = lead.tipoIndicacao?.trim().toLowerCase();
  if (tipo === "lead") return "lead";
  return lead.criadoManualmente || tipo ? "indicacao" : null;
}

/**
 * Coluna em que o cliente está no quadro de leads e a troca de etapa, sem voltar ao quadro. A troca segue as mesmas regras do quadro: "Venda concluída"
 * abre o formulário da venda, "Perdido" pede o motivo, "Não fazemos" o veículo, "Cotação" a adesão, e as colunas "(Leads)"/"(Indicação)" só aceitam o
 * cartão da etiqueta certa.
 */
export function EtapaDoLead({ lead, onMudou }: { lead: LeadDetail; onMudou: () => void }) {
  const { notificar } = useToast();
  const [etapas, setEtapas] = useState<LeadStage[]>([]);
  const [etapaGanhoPipelineId, setEtapaGanhoPipelineId] = useState<string | null>(null);
  const [aberto, setAberto] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [pendenciaVenda, setPendenciaVenda] = useState<{ etapa: LeadStage; indicacaoLead?: boolean } | null>(null);
  const [pendenciaPerda, setPendenciaPerda] = useState<LeadStage | null>(null);
  const [pendenciaNaoFazemos, setPendenciaNaoFazemos] = useState<LeadStage | null>(null);
  const [pendenciaCotacao, setPendenciaCotacao] = useState<LeadStage | null>(null);

  useEffect(() => {
    api.get<LeadStage[]>("/crm/settings/lead-stages").then((lista) => setEtapas(lista.filter((e) => e.ativa).sort((a, b) => a.ordem - b.ordem))).catch(() => setEtapas([]));
    api
      .get<PipelineBoard>("/crm/pipeline")
      .then((board) => setEtapaGanhoPipelineId(board.colunas.find((c) => c.etapa.tipo === TipoEtapaPipeline.Ganho)?.etapa.id ?? null))
      .catch(() => setEtapaGanhoPipelineId(null));
  }, []);

  const emAtendimento = useMemo(() => etapas.filter((e) => e.nome.startsWith(ETAPA_EM_ATENDIMENTO)), [etapas]);
  const vendaConcluida = useMemo(() => etapas.filter((e) => e.nome.startsWith(ETAPA_VENDA_CONCLUIDA)), [etapas]);
  const destinos = etapas.filter((e) => e.id !== lead.etapaId);

  async function mover(etapa: LeadStage, extra?: Extra) {
    setEnviando(true);
    try {
      await api.post(`/crm/leads/${lead.id}/stage`, { novaEtapaId: etapa.id, rowVersion: lead.rowVersion, ...extra });
      notificar("success", "Etapa atualizada.");
      setAberto(false);
      setPendenciaPerda(null);
      setPendenciaNaoFazemos(null);
      setPendenciaCotacao(null);
      onMudou();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível mudar a etapa. Tente novamente.");
    } finally {
      setEnviando(false);
    }
  }

  function iniciar(etapa: LeadStage) {
    const grupo = etapa.nome.startsWith(ETAPA_EM_ATENDIMENTO) ? emAtendimento : etapa.nome.startsWith(ETAPA_VENDA_CONCLUIDA) ? vendaConcluida : null;
    if (grupo) {
      const indice = grupo.findIndex((e) => e.id === etapa.id);
      const classe = classificar(lead);
      if (indice === 0 && classe === "indicacao") {
        notificar("error", `Esta coluna "${etapa.nome}" aceita só cartões com a etiqueta "Lead".`);
        return;
      }
      // Lead que fechou por indicação vai para "Venda concluída (Indicação)" e ganha a etiqueta "Indicação Lead".
      if (indice === 1 && classe === "lead" && grupo === vendaConcluida) {
        if (!etapaGanhoPipelineId) {
          notificar("error", "Não foi possível carregar as etapas do pipeline. Recarregue a página e tente novamente.");
          return;
        }
        setAberto(false);
        setPendenciaVenda({ etapa, indicacaoLead: true });
        return;
      }
      if (indice === 1 && classe === "lead") {
        notificar("error", `Esta coluna "${etapa.nome}" aceita só cartões com a etiqueta "Indicação".`);
        return;
      }
    }

    if (etapa.nome.startsWith(ETAPA_VENDA_CONCLUIDA)) {
      if (!etapaGanhoPipelineId) {
        notificar("error", "Não foi possível carregar as etapas do pipeline. Recarregue a página e tente novamente.");
        return;
      }
      setAberto(false);
      setPendenciaVenda({ etapa });
    } else if (etapa.nome === ETAPA_PERDIDO) {
      setAberto(false);
      setPendenciaPerda(etapa);
    } else if (etapa.nome === ETAPA_NAO_FAZEMOS) {
      setAberto(false);
      setPendenciaNaoFazemos(etapa);
    } else if (etapa.nome === ETAPA_COTACAO) {
      setAberto(false);
      setPendenciaCotacao(etapa);
    } else {
      void mover(etapa);
    }
  }

  return (
    <>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <span className="text-xs text-[var(--fg-muted)]">Coluna no quadro de leads:</span>
        <Badge variant="brand">{lead.etapaNome ?? "Sem etapa"}</Badge>
        <Button size="sm" variant="secondary" onClick={() => setAberto(true)} disabled={etapas.length === 0}>
          <ArrowRightLeft className="size-3.5" /> Mudar de etapa
        </Button>
      </div>

      {lead.etapaNome === ETAPA_PERDIDO && (
        <div
          className={`mt-3 rounded-xl border px-3.5 py-2.5 text-sm ${
            lead.motivoPerdaDescricao
              ? "border-[color-mix(in_srgb,var(--danger)_35%,transparent)] bg-[color-mix(in_srgb,var(--danger)_9%,transparent)]"
              : "border-dashed border-[var(--border)] bg-[var(--surface-hover)]"
          }`}
        >
          <p className={`flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wide ${lead.motivoPerdaDescricao ? "text-[var(--danger)]" : "text-[var(--fg-muted)]"}`}>
            <XCircle className="size-3.5 shrink-0" aria-hidden /> Motivo da perda
          </p>
          <p className={`mt-1 font-semibold ${lead.motivoPerdaDescricao ? "text-[var(--fg)]" : "font-normal italic text-[var(--fg-muted)]"}`}>
            {lead.motivoPerdaDescricao ?? "Sem motivo informado"}
          </p>
          {lead.motivoPerdaObservacao && <p className="mt-1 text-[var(--fg-muted)]">{lead.motivoPerdaObservacao}</p>}
        </div>
      )}

      {lead.etapaNome === ETAPA_NAO_FAZEMOS && lead.veiculoNaoAtendido && (
        <div className="mt-3 rounded-xl border border-[color-mix(in_srgb,var(--danger)_35%,transparent)] bg-[color-mix(in_srgb,var(--danger)_9%,transparent)] px-3.5 py-2.5 text-sm">
          <p className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wide text-[var(--danger)]">
            <Car className="size-3.5 shrink-0" aria-hidden /> Veículo que não atendemos
          </p>
          <p className="mt-1 font-semibold text-[var(--fg)]">{lead.veiculoNaoAtendido}</p>
        </div>
      )}

      <Modal open={aberto} onClose={() => setAberto(false)} title="Mudar de etapa" size="sm">
        <div className="space-y-1">
          <p className="mb-3 text-sm text-[var(--fg-muted)]">
            {lead.nomeOuRazaoSocial} — hoje em <strong className="text-[var(--fg)]">{lead.etapaNome ?? "Sem etapa"}</strong>
          </p>
          {destinos.map((e) => (
            <button
              key={e.id ?? "sem-etapa"}
              disabled={enviando}
              onClick={() => iniciar(e)}
              className="focus-ring flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)] disabled:opacity-50"
            >
              <ArrowRightLeft className="size-4 text-[var(--fg-muted)]" />
              {e.nome}
            </button>
          ))}
        </div>
      </Modal>

      <VendaConcluidaDialog
        open={!!pendenciaVenda}
        leadId={pendenciaVenda ? lead.id : null}
        opportunityId={null}
        pipelineGanhoEtapaId={etapaGanhoPipelineId ?? undefined}
        etapaNome={pendenciaVenda?.etapa.nome ?? ETAPA_VENDA_CONCLUIDA}
        valorEstimado={0}
        indicacaoLead={pendenciaVenda?.indicacaoLead}
        onCancel={() => setPendenciaVenda(null)}
        onConcluido={() => {
          setPendenciaVenda(null);
          notificar("success", "Venda concluída.");
          onMudou();
        }}
      />

      <StageChangeDialog
        open={!!pendenciaPerda}
        tipo={pendenciaPerda ? "perdido" : null}
        etapaNome={ETAPA_PERDIDO}
        enviando={enviando}
        onCancel={() => setPendenciaPerda(null)}
        onConfirm={(dados) => {
          if (pendenciaPerda) void mover(pendenciaPerda, dados);
        }}
      />

      <AdesaoCotacaoDialog
        open={!!pendenciaCotacao}
        etapaNome={ETAPA_COTACAO}
        valorAtual={lead.valorAdesao}
        enviando={enviando}
        onCancel={() => setPendenciaCotacao(null)}
        onConfirm={(dados) => {
          if (pendenciaCotacao) void mover(pendenciaCotacao, dados);
        }}
      />

      <VeiculoNaoFazemosDialog
        open={!!pendenciaNaoFazemos}
        etapaNome={ETAPA_NAO_FAZEMOS}
        enviando={enviando}
        onCancel={() => setPendenciaNaoFazemos(null)}
        onConfirm={(dados) => {
          if (pendenciaNaoFazemos) void mover(pendenciaNaoFazemos, dados);
        }}
      />
    </>
  );
}
