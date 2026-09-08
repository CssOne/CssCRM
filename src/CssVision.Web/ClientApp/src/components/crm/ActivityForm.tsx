import { useState, type FormEvent } from "react";
import { TipoAtividade } from "../../lib/types";
import { Button, Input, Label, Select, Textarea } from "../ui";

export interface ActivityFormValues {
  tipo: TipoAtividade;
  assunto: string;
  descricao: string;
  dataHoraPrevista: string;
  lembreteMinutosAntes: string;
}

const tipoLabel: Record<TipoAtividade, string> = {
  [TipoAtividade.Ligacao]: "Ligação",
  [TipoAtividade.WhatsApp]: "WhatsApp",
  [TipoAtividade.Email]: "E-mail",
  [TipoAtividade.Reuniao]: "Reunião",
  [TipoAtividade.Visita]: "Visita",
  [TipoAtividade.Retorno]: "Retorno",
  [TipoAtividade.Tarefa]: "Tarefa",
  [TipoAtividade.Observacao]: "Observação",
};

export { tipoLabel };

function agoraLocalIso(): string {
  const agora = new Date(Date.now() - new Date().getTimezoneOffset() * 60000);
  return agora.toISOString().slice(0, 16);
}

export function ActivityForm({
  salvando,
  onSubmit,
  onCancel,
}: {
  salvando: boolean;
  onSubmit: (valores: ActivityFormValues) => void;
  onCancel: () => void;
}) {
  const [valores, setValores] = useState<ActivityFormValues>({
    tipo: TipoAtividade.Ligacao,
    assunto: "",
    descricao: "",
    dataHoraPrevista: agoraLocalIso(),
    lembreteMinutosAntes: "",
  });

  function set<K extends keyof ActivityFormValues>(campo: K, valor: ActivityFormValues[K]) {
    setValores((v) => ({ ...v, [campo]: valor }));
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!valores.assunto.trim() || !valores.dataHoraPrevista) return;
    onSubmit(valores);
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" noValidate>
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <Label htmlFor="ativ-tipo">Tipo</Label>
          <Select id="ativ-tipo" value={valores.tipo} onChange={(e) => set("tipo", Number(e.target.value) as TipoAtividade)}>
            {Object.entries(tipoLabel).map(([valor, rotulo]) => (
              <option key={valor} value={valor}>
                {rotulo}
              </option>
            ))}
          </Select>
        </div>
        <div>
          <Label htmlFor="ativ-data" required>
            Data e hora
          </Label>
          <Input id="ativ-data" type="datetime-local" value={valores.dataHoraPrevista} onChange={(e) => set("dataHoraPrevista", e.target.value)} required />
        </div>
        <div className="sm:col-span-2">
          <Label htmlFor="ativ-assunto" required>
            Assunto
          </Label>
          <Input id="ativ-assunto" value={valores.assunto} onChange={(e) => set("assunto", e.target.value)} required />
        </div>
        <div className="sm:col-span-2">
          <Label htmlFor="ativ-desc">Descrição</Label>
          <Textarea id="ativ-desc" value={valores.descricao} onChange={(e) => set("descricao", e.target.value)} />
        </div>
        <div>
          <Label htmlFor="ativ-lembrete">Lembrete (min. antes)</Label>
          <Input id="ativ-lembrete" type="number" min={0} value={valores.lembreteMinutosAntes} onChange={(e) => set("lembreteMinutosAntes", e.target.value)} />
        </div>
      </div>
      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="secondary" onClick={onCancel} disabled={salvando}>
          Cancelar
        </Button>
        <Button type="submit" loading={salvando}>
          Agendar
        </Button>
      </div>
    </form>
  );
}
