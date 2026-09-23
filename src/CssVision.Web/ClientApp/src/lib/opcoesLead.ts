/** Tags do campo "O que?" (produto de interesse do lead) — mesmas opções do campo "O que" no Notion. */
export const OPCOES_O_QUE = ["AGV", "AGV ELÉTRICO", "AGV TRUCK"];

/**
 * Tags do campo Origem (visível só para administradores) — mesmas opções de campanha usadas no
 * Notion. Leads automáticos ainda chegam com a origem técnica ("Meta ads", "Site", "Sincronização
 * Notion"...); o seletor mostra esse valor como uma tag extra para não perdê-lo.
 */
export const OPCOES_ORIGEM = [
  "Lookalike",
  "UGC VENDA",
  "UGC CAMINHÃO",
  "Pesquisa",
  "Pmax",
  "Demand Gen",
  "UGC",
  "INFLUENCERS",
  "Lead convertido",
  "Caixa de pergunta",
];

/** Compara sem diferenciar maiúsculas nem acentos ("AGV ELETRICO" do Notion = "AGV ELÉTRICO"). */
export function mesmaTag(a: string | null | undefined, b: string | null | undefined): boolean {
  if (!a || !b) return false;
  const normalizar = (s: string) => s.normalize("NFD").replace(/[̀-ͯ]/g, "").trim().toUpperCase();
  return normalizar(a) === normalizar(b);
}
