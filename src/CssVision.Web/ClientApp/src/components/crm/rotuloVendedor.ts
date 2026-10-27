import type { VendedorResumo } from "../../lib/types";

/** Texto da lista de atribuição: leads que já pegou (hoje e no mês) e se está ativo para receber leads. */
export function rotuloVendedor(v: VendedorResumo) {
  const situacao = v.ativo === false ? "conta inativa" : v.recebeLeads ? "ativo p/ receber leads" : "inativo p/ receber leads";
  const hoje = v.leadsRecebidosHoje ?? 0;
  return `${v.nome} — pegou ${hoje} hoje, ${v.leadsRecebidosNoMes} no mês · ${situacao}`;
}
