import { useEffect, useState, type FormEvent } from "react";
import { api } from "../../lib/api";
import type { VendedorResumo } from "../../lib/types";
import { Button, Checkbox, Input, Label, Select, Textarea } from "../ui";

export interface OpportunityFormValues {
  titulo: string;
  produtoOuServico: string;
  valorEstimado: string;
  probabilidadeFechamento: string;
  dataPrevistaFechamento: string;
  concorrente: string;
  observacoes: string;
  dataAdesao: string;
  mensalidade: string;
  mensalidadeComDesconto: string;
  pagamentoAdesao: string;
  porcentagem: string;
  termoAdesaoAceito: boolean;
  migracao: boolean;
  veiculoDescricao: string;
  veiculoPlaca: string;
  veiculoFipe: string;
  veiculoRastreador: string;
  veiculoVistoriadorId: string;
}

export const opportunityFormVazio: OpportunityFormValues = {
  titulo: "",
  produtoOuServico: "",
  valorEstimado: "",
  probabilidadeFechamento: "",
  dataPrevistaFechamento: "",
  concorrente: "",
  observacoes: "",
  dataAdesao: "",
  mensalidade: "",
  mensalidadeComDesconto: "",
  pagamentoAdesao: "",
  porcentagem: "",
  termoAdesaoAceito: false,
  migracao: false,
  veiculoDescricao: "",
  veiculoPlaca: "",
  veiculoFipe: "",
  veiculoRastreador: "",
  veiculoVistoriadorId: "",
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
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);

  useEffect(() => {
    api.get<VendedorResumo[]>("/crm/management/vendedores").then(setVendedores).catch(() => setVendedores([]));
  }, []);

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

      <div className="border-t border-[var(--border)] pt-4">
        <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">Adesão e cobrança</h3>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor="opp-data-adesao">Data de adesão</Label>
            <Input id="opp-data-adesao" type="date" value={valores.dataAdesao} onChange={(e) => set("dataAdesao", e.target.value)} />
          </div>
          <div>
            <Label htmlFor="opp-porcentagem">Comissão/desconto (%)</Label>
            <Input id="opp-porcentagem" type="number" min={0} max={100} step="0.01" value={valores.porcentagem} onChange={(e) => set("porcentagem", e.target.value)} />
          </div>
          <div>
            <Label htmlFor="opp-mensalidade">Mensalidade (R$)</Label>
            <Input id="opp-mensalidade" type="number" min={0} step="0.01" value={valores.mensalidade} onChange={(e) => set("mensalidade", e.target.value)} />
          </div>
          <div>
            <Label htmlFor="opp-mensalidade-desconto">Mensalidade com desconto (R$)</Label>
            <Input id="opp-mensalidade-desconto" type="number" min={0} step="0.01" value={valores.mensalidadeComDesconto} onChange={(e) => set("mensalidadeComDesconto", e.target.value)} />
          </div>
          <div>
            <Label htmlFor="opp-pagamento-adesao">Pagamento de adesão (R$)</Label>
            <Input id="opp-pagamento-adesao" type="number" min={0} step="0.01" value={valores.pagamentoAdesao} onChange={(e) => set("pagamentoAdesao", e.target.value)} />
          </div>
        </div>
        <div className="mt-3 flex flex-wrap gap-4">
          <Checkbox label="Termo de adesão aceito" checked={valores.termoAdesaoAceito} onChange={(e) => set("termoAdesaoAceito", e.target.checked)} />
          <Checkbox label="É uma migração" checked={valores.migracao} onChange={(e) => set("migracao", e.target.checked)} />
        </div>
      </div>

      <div className="border-t border-[var(--border)] pt-4">
        <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">Veículo</h3>
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <Label htmlFor="opp-veiculo-descricao">Veículo (marca/modelo)</Label>
            <Input id="opp-veiculo-descricao" value={valores.veiculoDescricao} onChange={(e) => set("veiculoDescricao", e.target.value)} />
          </div>
          <div>
            <Label htmlFor="opp-veiculo-placa">Placa</Label>
            <Input id="opp-veiculo-placa" value={valores.veiculoPlaca} onChange={(e) => set("veiculoPlaca", e.target.value.toUpperCase())} />
          </div>
          <div>
            <Label htmlFor="opp-veiculo-fipe">Valor FIPE (R$)</Label>
            <Input id="opp-veiculo-fipe" type="number" min={0} step="0.01" value={valores.veiculoFipe} onChange={(e) => set("veiculoFipe", e.target.value)} />
          </div>
          <div>
            <Label htmlFor="opp-veiculo-rastreador">Rastreador</Label>
            <Input id="opp-veiculo-rastreador" value={valores.veiculoRastreador} onChange={(e) => set("veiculoRastreador", e.target.value)} />
          </div>
          <div>
            <Label htmlFor="opp-veiculo-vistoriador">Vistoriador</Label>
            <Select id="opp-veiculo-vistoriador" value={valores.veiculoVistoriadorId} onChange={(e) => set("veiculoVistoriadorId", e.target.value)}>
              <option value="">Nenhum</option>
              {vendedores.map((v) => (
                <option key={v.id} value={v.id}>
                  {v.nome}
                </option>
              ))}
            </Select>
          </div>
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
