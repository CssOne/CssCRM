import { useEffect, useState } from "react";
import { api, ApiRequestError } from "../../lib/api";
import type { VendedorResumo } from "../../lib/types";
import { Button, Input, Label, Modal, Select, useToast } from "../ui";

/**
 * Troca o consultor responsável por um lead (só gestão/administração — o servidor confere). O novo
 * responsável recebe o aviso de "novo lead" e a troca fica no histórico de atribuições do lead.
 */
export function AlterarResponsavelDialog({
  open,
  leadId,
  leadNome,
  responsavelAtualId,
  onCancel,
  onConcluido,
}: {
  open: boolean;
  leadId: string | null;
  leadNome?: string;
  responsavelAtualId?: string | null;
  onCancel: () => void;
  onConcluido: () => void;
}) {
  const { notificar } = useToast();
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);
  const [responsavelId, setResponsavelId] = useState("");
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!open) return;
    setResponsavelId("");
    setMotivo("");
    const controller = new AbortController();
    api
      .get<VendedorResumo[]>("/crm/management/vendedores", controller.signal)
      .then(setVendedores)
      .catch(() => setVendedores([]));
    return () => controller.abort();
  }, [open]);

  async function confirmar() {
    if (!leadId || !responsavelId) return;
    setEnviando(true);
    try {
      await api.post(`/crm/leads/${leadId}/assign`, { responsavelId, motivo: motivo.trim() || null });
      notificar("success", "Responsável alterado.");
      onConcluido();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível alterar o responsável.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal open={open} onClose={onCancel} title="Alterar responsável" size="sm">
      <div className="space-y-4">
        {leadNome && (
          <p className="text-sm text-[var(--fg-muted)]">
            Lead: <strong className="text-[var(--fg)]">{leadNome}</strong>
          </p>
        )}
        <div>
          <Label htmlFor="novo-responsavel" required>
            Novo responsável
          </Label>
          <Select id="novo-responsavel" value={responsavelId} onChange={(e) => setResponsavelId(e.target.value)}>
            <option value="">Selecione...</option>
            {vendedores
              .filter((v) => v.id !== responsavelAtualId)
              .map((v) => (
                <option key={v.id} value={v.id}>
                  {v.nome}
                </option>
              ))}
          </Select>
        </div>
        <div>
          <Label htmlFor="motivo-responsavel">Motivo (opcional)</Label>
          <Input id="motivo-responsavel" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </div>
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onCancel} disabled={enviando}>
            Cancelar
          </Button>
          <Button onClick={confirmar} loading={enviando} disabled={!responsavelId}>
            Alterar
          </Button>
        </div>
      </div>
    </Modal>
  );
}
