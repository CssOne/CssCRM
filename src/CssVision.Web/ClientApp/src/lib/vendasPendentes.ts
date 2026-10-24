/**
 * Regra de visibilidade das vendas no Controle financeiro: o filtro de mês olha o mês em que a venda foi fechada, mas o que ainda está para receber
 * ("Com pendência") e o que passou da data combinada ("Em atraso") precisa aparecer em qualquer mês — uma venda fechada em setembro com a adesão
 * agendada para outubro continua pendente em outubro, e não pode sumir só porque o mês escolhido mudou.
 */
export const FILTROS_SEM_MES = ["pendente", "atrasada"] as const;

export const ignoraFiltroDeMes = (filtro: string) => (FILTROS_SEM_MES as readonly string[]).includes(filtro);

/**
 * Texto da data combinada para o pagamento da adesão, mostrado abaixo da situação ("paga em 30/10/2026" / "venceu em 20/10/2026").
 * Só para adesão ainda não recebida (pendente ou em atraso) e com data combinada; nulo nos demais casos.
 */
export function rotuloDaDataDeAdesao(situacao: string, prevista: string | null | undefined): { texto: string; atrasada: boolean } | null {
  if (!prevista || (situacao !== "Pendente" && situacao !== "Atrasado")) return null;
  const [ano, mes, dia] = prevista.slice(0, 10).split("-");
  const data = `${dia}/${mes}/${ano}`;
  return situacao === "Atrasado" ? { texto: `venceu em ${data}`, atrasada: true } : { texto: `paga em ${data}`, atrasada: false };
}

export interface OpcoesDeVendas<T> {
  /** Mês escolhido (1 a 12); 0 = ano inteiro. */
  mes: number;
  /** Atalho de situação escolhido ("" = todas). */
  filtro: string;
  /** A venda foi fechada no mês escolhido? */
  noMes: (venda: T) => boolean;
  /** Passa nos outros filtros (busca, produto, consultor)? */
  passaOutros: (venda: T) => boolean;
  /** A venda se encaixa no atalho de situação? */
  confere: (venda: T, filtro: string) => boolean;
}

export function vendasVisiveis<T>(todas: T[], o: OpcoesDeVendas<T>) {
  const semMes = todas.filter(o.passaOutros);
  const doMes = o.mes ? semMes.filter(o.noMes) : semMes;
  const lista = (ignoraFiltroDeMes(o.filtro) ? semMes : doMes).filter((v) => o.confere(v, o.filtro));
  /** Contagem do atalho: pendentes e atrasadas contam o ano todo; os demais, só o mês escolhido. */
  const contagem = (filtro: string) => (ignoraFiltroDeMes(filtro) ? semMes : doMes).filter((v) => o.confere(v, filtro)).length;
  return { doMes, semMes, lista, contagem, fezCortePorMes: ignoraFiltroDeMes(o.filtro) && o.mes !== 0 && semMes.length > doMes.length };
}
