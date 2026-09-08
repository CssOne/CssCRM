import { useState, type FormEvent } from "react";
import { Button, Input, Label, Textarea } from "../ui";

export interface OpportunityFormValues {
  titulo: string;
  produtoOuServico: string;
  valorEstimado: string;
  probabilidadeFechamento: string;
  dataPrevistaFechamento: string;
  concorrente: string;
  observacoes: string;
}

export const opportunityFormVazio: OpportunityFormValues = {
  titulo: "",
  produtoOuServico: "",
  valorEstimado: "",
  probabilidadeFechamento: "",
  dataPrevistaFechamento: "",
  concorrente: "",
  observacoes: "",
};

export function OpportunityForm({
  salvando,
  onSubmit,
  onCancel,
}: {
  salvando: boolean;
  onSubmit: (valores: OpportunityFormValues) => void;
  onCancel: () => void;
}) {
  const [valores, setValores] = useState<OpportunityFormValues>(opportunityFormVazio);

  function set<K extends keyof OpportunityFormValues>(campo: K, valor: OpportunityFormValues[K]) {
    setValores((v) => ({ ...v, [campo]: valor }));
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!valores.titulo.trim()) return;
    onSubmit(valores);
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" noValidate>
      <div>
        <Label htmlFor="opp-titulo" required>
          Título
        </Label>
        <Input id="opp-titulo" value={valores.titulo} onChange={(e) => set("titulo", e.target.value)} required />
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <Label htmlFor="opp-produto">Produto/serviço</Label>
          <Input id="opp-produto" value={valores.produtoOuServico} onChange={(e) => set("produtoOuServico", e.target.value)} />
        </div>
        <div>
          <Label htmlFor="opp-valor" required>
            Valor estimado (R$)
          </Label>
          <Input
            id="opp-valor"
            type="number"
            min={0}
            step="0.01"
            value={valores.valorEstimado}
            onChange={(e) => set("valorEstimado", e.target.value)}
            required
          />
        </div>
        <div>
          <Label htmlFor="opp-prob">Probabilidade (%)</Label>
          <Input id="opp-prob" type="number" min={0} max={100} value={valores.probabilidadeFechamento} onChange={(e) => set("probabilidadeFechamento", e.target.value)} />
        </div>
        <div>
          <Label htmlFor="opp-data">Previsão de fechamento</Label>
          <Input id="opp-data" type="date" value={valores.dataPrevistaFechamento} onChange={(e) => set("dataPrevistaFechamento", e.target.value)} />
        </div>
        <div className="sm:col-span-2">
          <Label htmlFor="opp-concorrente">Concorrente</Label>
          <Input id="opp-concorrente" value={valores.concorrente} onChange={(e) => set("concorrente", e.target.value)} />
        </div>
        <div className="sm:col-span-2">
          <Label htmlFor="opp-obs">Observações</Label>
          <Textarea id="opp-obs" value={valores.observacoes} onChange={(e) => set("observacoes", e.target.value)} />
        </div>
      </div>
      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="secondary" onClick={onCancel} disabled={salvando}>
          Cancelar
        </Button>
        <Button type="submit" loading={salvando}>
          Salvar
        </Button>
      </div>
    </form>
  );
}
