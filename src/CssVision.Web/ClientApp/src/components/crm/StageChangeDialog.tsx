import { useEffect, useState } from "react";
import { api } from "../../lib/api";
import type { LossReason } from "../../lib/types";
import { Button, Input, Label, Modal, Select } from "../ui";

export interface DadosFechamento {
  motivoPerdaId?: string;
  valorFinal?: number;
  dataEfetivaFechamento?: string;
}

export function StageChangeDialog({
  open,
  tipo,
  etapaNome,
  valorEstimado,
  enviando,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  tipo: "ganho" | "perdido" | null;
  etapaNome: string;
  valorEstimado: number;
  enviando: boolean;
  onConfirm: (dados: DadosFechamento) => void;
  onCancel: () => void;
}) {
  const [motivos, setMotivos] = useState<LossReason[]>([]);
  const [motivoPerdaId, setMotivoPerdaId] = useState("");
  const [valorFinal, setValorFinal] = useState(String(valorEstimado));
  const [dataFechamento, setDataFechamento] = useState(() => new Date().toISOString().slice(0, 10));

  useEffect(() => {
    if (open && tipo === "perdido") {
      api.get<LossReason[]>("/crm/settings/loss-reasons").then((lista) => {
        setMotivos(lista);
        setMotivoPerdaId(lista[0]?.id ?? "");
      });
    }
    if (open) {
      setValorFinal(String(valorEstimado));
      setDataFechamento(new Date().toISOString().slice(0, 10));
    }
  }, [open, tipo, valorEstimado]);

  if (!open || !tipo) return null;

  const podeConfirmar = tipo === "perdido" ? !!motivoPerdaId : Number(valorFinal) >= 0 && !!dataFechamento;

  return (
    <Modal open={open} onClose={onCancel} title={tipo === "ganho" ? `Marcar como "${etapaNome}"` : `Mover para "${etapaNome}"`} size="sm">
      <div className="space-y-4">
        {tipo === "perdido" ? (
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
        ) : (
          <>
            <div>
              <Label htmlFor="valor-final" required>
                Valor final (R$)
              </Label>
              <Input id="valor-final" type="number" min={0} step="0.01" value={valorFinal} onChange={(e) => setValorFinal(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="data-fechamento" required>
                Data de fechamento
              </Label>
              <Input id="data-fechamento" type="date" value={dataFechamento} onChange={(e) => setDataFechamento(e.target.value)} />
            </div>
          </>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onCancel} disabled={enviando}>
            Cancelar
          </Button>
          <Button
            variant={tipo === "perdido" ? "danger" : "primary"}
            loading={enviando}
            disabled={!podeConfirmar}
            onClick={() =>
              onConfirm(
                tipo === "perdido"
                  ? { motivoPerdaId }
                  : { valorFinal: Number(valorFinal), dataEfetivaFechamento: dataFechamento }
              )
            }
          >
            Confirmar
          </Button>
        </div>
      </div>
    </Modal>
  );
}
