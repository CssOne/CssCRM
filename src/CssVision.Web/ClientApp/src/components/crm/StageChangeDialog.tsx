import { useEffect, useState } from "react";
import { api } from "../../lib/api";
import type { LossReason } from "../../lib/types";
import { Button, Label, Modal, Select, Textarea } from "../ui";

export interface DadosFechamento {
  motivoPerdaId?: string;
  motivoPerdaObservacao?: string;
}

/** Move um cartão para uma etapa "Perdido", exigindo o motivo da perda. Para "Ganho", ver VendaConcluidaDialog. */
export function StageChangeDialog({
  open,
  tipo,
  etapaNome,
  enviando,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  tipo: "perdido" | null;
  etapaNome: string;
  enviando: boolean;
  onConfirm: (dados: DadosFechamento) => void;
  onCancel: () => void;
}) {
  const [motivos, setMotivos] = useState<LossReason[]>([]);
  const [motivoPerdaId, setMotivoPerdaId] = useState("");
  const [observacao, setObservacao] = useState("");

  useEffect(() => {
    if (open) {
      api.get<LossReason[]>("/crm/settings/loss-reasons").then((lista) => {
        setMotivos(lista);
        setMotivoPerdaId(lista[0]?.id ?? "");
      });
      setObservacao("");
    }
  }, [open]);

  if (!open || !tipo) return null;

  return (
    <Modal open={open} onClose={onCancel} title={`Mover para "${etapaNome}"`} size="sm">
      <div className="space-y-4">
        <div>
          <Label htmlFor="motivo-perda" required>
            Motivo da perda
          </Label>
          <Select id="motivo-perda" value={motivoPerdaId} onChange={(e) => setMotivoPerdaId(e.target.value)}>
            {motivos.map((m) => (
              <option key={m.id} value={m.id}>
                {m.descricao}
              </option>
            ))}
          </Select>
        </div>

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
            onClick={() => onConfirm({ motivoPerdaId, motivoPerdaObservacao: observacao.trim() || undefined })}
          >
            Confirmar
          </Button>
        </div>
      </div>
    </Modal>
  );
}
