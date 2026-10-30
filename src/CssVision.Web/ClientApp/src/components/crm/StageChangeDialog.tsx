import { useEffect, useState } from "react";
import { api } from "../../lib/api";
import type { LossReason } from "../../lib/types";
import { CalendarClock } from "lucide-react";
import { Button, Label, Modal, Select, Textarea } from "../ui";
import { exibirData, paraData } from "../Calendario";
import { hojeBrasilia } from "../../lib/format";
import { DataInput } from "../PeriodoInput";

export interface DadosFechamento {
  motivoPerdaId?: string;
  motivoPerdaObservacao?: string;
  /** Proposta do concorrente — só quando o motivo é "Fechado na Concorrência" e o chamador aceita o anexo. */
  propostaConcorrente?: File;
  /** Vencimento do seguro (AAAA-MM-DD) — só no "Follow Up" por "Já tem seguro". */
  seguroVenceEm?: string;
}

const MOTIVO_JA_TEM_SEGURO = "Já tem seguro";

/** "(hoje)", "(amanhã)" ou "(daqui a N dias)" para a data escolhida. */
function textoDias(iso: string) {
  const alvo = paraData(iso);
  const h = paraData(hojeBrasilia());
  if (!alvo || !h) return "";
  const dias = Math.round((alvo.getTime() - h.getTime()) / 86_400_000);
  return dias <= 0 ? "(hoje)" : dias === 1 ? "(amanhã)" : `(daqui a ${dias} dias)`;
}

/** Motivo de perda que permite anexar a proposta do concorrente (mesmo nome do CrmSeeder). */
export const MOTIVO_FECHADO_NA_CONCORRENCIA = "Fechado na Concorrência";

/** Motivos que mandam o lead para a coluna "Follow Up" (e só eles) — mesma lista do LeadService. "Perdido" não os aceita. */
export const MOTIVOS_FOLLOW_UP = ["Já tem seguro", "Preço"];

/** Motivos que não aparecem em "Perdido" do quadro de leads: os do Follow Up e "Não responde" (que é coluna do quadro, não motivo de perda). */
export const MOTIVOS_FORA_DE_PERDIDO = [...MOTIVOS_FOLLOW_UP, "Não responde"];

const naLista = (lista: string[], descricao: string) => lista.some((x) => x.toLowerCase() === descricao.toLowerCase());

/** Move um cartão para uma etapa "Perdido" ou "Follow Up", exigindo o motivo (cada etapa aceita só os seus). Para "Ganho", ver VendaConcluidaDialog. */
export function StageChangeDialog({
  open,
  tipo,
  etapaNome,
  enviando,
  permitirPropostaConcorrente = false,
  motivosPermitidos,
  motivosExcluidos,
  edicao = null,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  tipo: "perdido" | null;
  etapaNome: string;
  enviando: boolean;
  /** Mostra o campo para anexar a proposta do concorrente quando o motivo escolhido é "Fechado na Concorrência" (só leads). */
  permitirPropostaConcorrente?: boolean;
  /** Só estes motivos aparecem (ex.: "Follow Up"). */
  motivosPermitidos?: string[];
  /** Estes motivos não aparecem (ex.: "Perdido" não aceita os do "Follow Up"). */
  motivosExcluidos?: string[];
  /** Edição do motivo de um cartão que já está na coluna: parte do motivo e da explicação atuais, e o título/botão dizem "Editar". */
  edicao?: { motivo: string | null; observacao: string | null } | null;
  onConfirm: (dados: DadosFechamento) => void;
  onCancel: () => void;
}) {
  const [motivos, setMotivos] = useState<LossReason[]>([]);
  const [motivoPerdaId, setMotivoPerdaId] = useState("");
  const [observacao, setObservacao] = useState("");
  const [proposta, setProposta] = useState<File | null>(null);
  const [seguroVenceEm, setSeguroVenceEm] = useState("");

  useEffect(() => {
    if (open) {
      // Nada vem marcado: o consultor tem que escolher o motivo (antes vinha o primeiro da lista). Na edição, vem o motivo atual.
      setMotivoPerdaId("");
      api.get<LossReason[]>("/crm/settings/loss-reasons").then((lista) => {
        setMotivos(lista);
        const atual = edicao?.motivo?.toLowerCase();
        const motivoAtual = atual ? lista.find((m) => m.descricao.toLowerCase() === atual) : undefined;
        if (motivoAtual) setMotivoPerdaId(motivoAtual.id);
      });
      setObservacao(edicao?.observacao ?? "");
      setProposta(null);
      setSeguroVenceEm("");
    }
  }, [open]);

  if (!open || !tipo) return null;

  const pedeVencimentoDoSeguro =
    etapaNome === "Follow Up" && motivos.find((m) => m.id === motivoPerdaId)?.descricao.toLowerCase() === MOTIVO_JA_TEM_SEGURO.toLowerCase();
  const opcoes = motivos.filter(
    (m) => (!motivosPermitidos || naLista(motivosPermitidos, m.descricao)) && !(motivosExcluidos && naLista(motivosExcluidos, m.descricao))
  );

  const pedeProposta = permitirPropostaConcorrente && motivos.find((m) => m.id === motivoPerdaId)?.descricao === MOTIVO_FECHADO_NA_CONCORRENCIA;

  return (
    <Modal open={open} onClose={onCancel} title={edicao ? `Editar motivo — ${etapaNome}` : `Mover para "${etapaNome}"`} size="sm">
      <div className="space-y-4">
        <div>
          <Label htmlFor="motivo-perda" required>
            {etapaNome === "Follow Up" ? "Motivo do Follow Up" : "Motivo da perda"}
          </Label>
          <Select id="motivo-perda" value={motivoPerdaId} onChange={(e) => setMotivoPerdaId(e.target.value)}>
            <option value="" disabled>
              Selecione o motivo...
            </option>
            {opcoes.map((m) => (
              <option key={m.id} value={m.id}>
                {m.descricao}
              </option>
            ))}
          </Select>
          {!motivoPerdaId && <p className="mt-1 text-xs text-[var(--fg-muted)]">{edicao ? "Escolha o motivo." : `Obrigatório para mover para "${etapaNome}".`}</p>}
        </div>

        {pedeVencimentoDoSeguro && (
          <div>
            <Label htmlFor="seguro-vence-em">Quando o seguro do associado vence</Label>
            <DataInput id="seguro-vence-em" min={hojeBrasilia()} value={seguroVenceEm} onChange={setSeguroVenceEm} />
            {seguroVenceEm ? (
              <p className="mt-2 flex items-start gap-2 rounded-lg bg-[var(--brand-soft)] px-2.5 py-2 text-xs text-[var(--fg)]">
                <CalendarClock className="mt-0.5 size-3.5 shrink-0 text-[var(--brand)]" aria-hidden />
                <span>
                  Em <strong>{exibirData(seguroVenceEm)}</strong> {textoDias(seguroVenceEm)} você recebe um aviso e uma atividade aparece no seu calendário.
                </span>
              </p>
            ) : (
              <p className="mt-1 text-xs text-[var(--fg-muted)]">Opcional — nesse dia você recebe um aviso e uma atividade aparece no seu calendário.</p>
            )}
          </div>
        )}

        {pedeProposta && (
          <div>
            <Label htmlFor="proposta-concorrente">Proposta do concorrente</Label>
            <input
              id="proposta-concorrente"
              type="file"
              accept="application/pdf,image/png,image/jpeg,image/webp"
              className="block w-full text-sm text-[var(--fg-muted)] file:mr-3 file:rounded-lg file:border file:border-[var(--border)] file:bg-[var(--surface-hover)] file:px-3 file:py-1.5 file:text-sm file:text-[var(--fg)]"
              onChange={(e) => setProposta(e.target.files?.[0] ?? null)}
            />
            <p className="mt-1 text-xs text-[var(--fg-muted)]">Opcional — PDF, JPG, PNG ou WEBP, até 10 MB.</p>
          </div>
        )}

        <div>
          <Label htmlFor="motivo-perda-observacao">Explique com suas palavras o que aconteceu</Label>
          <Textarea
            id="motivo-perda-observacao"
            rows={3}
            placeholder="Opcional — conte em poucas palavras o que motivou a perda."
            value={observacao}
            onChange={(e) => setObservacao(e.target.value)}
          />
        </div>

        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onCancel} disabled={enviando}>
            Cancelar
          </Button>
          <Button
            variant="danger"
            loading={enviando}
            disabled={!motivoPerdaId}
            onClick={() => onConfirm({ motivoPerdaId, motivoPerdaObservacao: observacao.trim() || undefined, propostaConcorrente: pedeProposta && proposta ? proposta : undefined, seguroVenceEm: pedeVencimentoDoSeguro && seguroVenceEm ? seguroVenceEm : undefined })}
          >
            {edicao ? "Salvar" : "Confirmar"}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
