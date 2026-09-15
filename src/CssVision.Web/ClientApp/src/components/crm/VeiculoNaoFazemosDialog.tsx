import { useEffect, useState } from "react";
import { Button, Input, Label, Modal } from "../ui";

export interface DadosNaoFazemos {
  veiculoNaoAtendido: string;
}

/** Move um lead para a etapa "Não fazemos", exigindo o modelo do veículo que a CSS Brasil não atende. */
export function VeiculoNaoFazemosDialog({
  open,
  etapaNome,
  enviando,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  etapaNome: string;
  enviando: boolean;
  onConfirm: (dados: DadosNaoFazemos) => void;
  onCancel: () => void;
}) {
  const [veiculo, setVeiculo] = useState("");

  useEffect(() => {
    if (open) setVeiculo("");
  }, [open]);

  if (!open) return null;

  return (
    <Modal open={open} onClose={onCancel} title={`Mover para "${etapaNome}"`} size="sm">
      <div className="space-y-4">
        <div>
          <Label htmlFor="nao-fazemos-veiculo" required>
            Modelo do veículo
          </Label>
          <Input
            id="nao-fazemos-veiculo"
            autoFocus
            placeholder="Ex: Ferrari 488, moto acima de 1000cc..."
            value={veiculo}
            onChange={(e) => setVeiculo(e.target.value)}
          />
        </div>

        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onCancel} disabled={enviando}>
            Cancelar
          </Button>
          <Button
            variant="danger"
            loading={enviando}
            disabled={!veiculo.trim()}
            onClick={() => onConfirm({ veiculoNaoAtendido: veiculo.trim() })}
          >
            Confirmar
          </Button>
        </div>
      </div>
    </Modal>
  );
}
