import { useEffect, useState } from "react";
import { api } from "../../lib/api";
import type { VendedorResumo } from "../../lib/types";
import { Button, Input, Label, Modal, Select } from "../ui";

export function AssignModal({
  open,
  quantidade,
  onClose,
  onConfirm,
}: {
  open: boolean;
  quantidade: number;
  onClose: () => void;
  onConfirm: (vendedorId: string, motivo: string) => Promise<void>;
}) {
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);
  const [vendedorId, setVendedorId] = useState("");
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!open) return;
    api.get<VendedorResumo[]>("/crm/management/vendedores").then((lista) => {
      setVendedores(lista);
      setVendedorId(lista[0]?.id ?? "");
    });
  }, [open]);

  async function confirmar() {
    if (!vendedorId) return;
    setEnviando(true);
    try {
      await onConfirm(vendedorId, motivo);
      setMotivo("");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} title={quantidade > 1 ? `Atribuir ${quantidade} leads` : "Atribuir lead"}>
      <div className="space-y-4">
        <div>
          <Label htmlFor="assign-vendedor" required>
            Vendedor responsável
          </Label>
          <Select id="assign-vendedor" value={vendedorId} onChange={(e) => setVendedorId(e.target.value)}>
            {vendedores.map((v) => (
              <option key={v.id} value={v.id}>
                {v.nome} ({v.leadsAtivos} leads ativos)
              </option>
            ))}
          </Select>
        </div>
        <div>
          <Label htmlFor="assign-motivo">Motivo (opcional)</Label>
          <Input id="assign-motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </div>
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={enviando}>
            Cancelar
          </Button>
          <Button onClick={confirmar} loading={enviando} disabled={!vendedorId}>
            Atribuir
          </Button>
        </div>
      </div>
    </Modal>
  );
}
