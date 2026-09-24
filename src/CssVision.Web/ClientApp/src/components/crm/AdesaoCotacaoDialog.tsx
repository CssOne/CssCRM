import { useEffect, useState } from "react";
import { Button, Label, Modal, MoneyInput } from "../ui";

export interface DadosCotacao {
  valorAdesao: number;
}

/**
 * Move um lead para a etapa "Cotação", exigindo o valor da adesão (o servidor também recusa sem ele).
 * O valor fica salvo no lead e pré-preenche o "Pagamento de adesão" da Oportunidade e da Venda concluída.
 */
export function AdesaoCotacaoDialog({
  open,
  etapaNome,
  valorAtual,
  enviando,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  etapaNome: string;
  /** Valor já salvo no lead (vem pré-preenchido). */
  valorAtual?: number | null;
  enviando: boolean;
  onConfirm: (dados: DadosCotacao) => void;
  onCancel: () => void;
}) {
  const [valor, setValor] = useState<number | null>(null);

  useEffect(() => {
    if (open) setValor(valorAtual ?? null);
  }, [open, valorAtual]);

  if (!open) return null;

  const valido = valor != null && valor > 0;

  return (
    <Modal open={open} onClose={onCancel} title={`Mover para "${etapaNome}"`} size="sm">
      <div className="space-y-4">
        <div>
          <Label htmlFor="cotacao-adesao" required>
            Valor da adesão (R$)
          </Label>
          <MoneyInput id="cotacao-adesao" value={valor} onChange={setValor} />
          <p className="mt-1 text-xs text-[var(--fg-muted)]">
            Obrigatório para mover para Cotação. Esse valor já vem preenchido na Oportunidade e na Venda concluída.
          </p>
        </div>

        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onCancel} disabled={enviando}>
            Cancelar
          </Button>
          <Button loading={enviando} disabled={!valido} onClick={() => valido && onConfirm({ valorAdesao: valor! })}>
            Confirmar
          </Button>
        </div>
      </div>
    </Modal>
  );
}
