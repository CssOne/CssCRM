import type { TvComercial, TvRanking, TvRegional, TvVenda } from "../../lib/types";

/**
 * Eventos que o painel da TV comemora, descobertos comparando duas leituras seguidas do painel (a tela consulta o CRM a cada 15 s).
 * Tudo aqui é lógica pura (sem tela), para poder ser testada.
 */
export type Celebracao =
  | { tipo: "venda"; venda: TvVenda; posicao: number; vendasNoMes: number }
  | { tipo: "meta"; consultor: TvRanking }
  | { tipo: "meta-regional"; regional: TvRegional }
  | { tipo: "posicao"; consultor: TvRanking; de: number; para: number; ultrapassou: string[]; ultrapassado: TvRanking | null }
  | { tipo: "hora"; hora: number; ranking: TvRanking[]; regionais: TvRegional[] };

/** Chave para não repetir a mesma comemoração enquanto a tela está aberta. */
export function chaveDaCelebracao(c: Exclude<Celebracao, { tipo: "venda" }>, mes: { mes: number; ano: number }): string {
  const periodo = `${mes.ano}-${mes.mes}`;
  switch (c.tipo) {
    case "meta": return `meta:${periodo}:${c.consultor.consultorId}`;
    case "meta-regional": return `meta-regional:${periodo}:${c.regional.regionalId}`;
    case "posicao": return `posicao:${periodo}:${c.consultor.consultorId}:${c.para}:${c.consultor.quantidadeVendas}`;
    case "hora": return `hora:${new Date().toISOString().slice(0, 10)}:${c.hora}`;
  }
}

const bateuMeta = (percentual?: number | null) => percentual != null && percentual >= 100;

/** Consultores que passaram a ter a meta do mês batida entre as duas leituras (estavam abaixo de 100% e agora estão em 100% ou mais). */
export function detectarMetasBatidas(anterior: TvComercial, atual: TvComercial): TvRanking[] {
  if (!mesmoPeriodo(anterior, atual)) return [];
  const antes = new Map(anterior.rankingConsultores.map((r) => [r.consultorId, r]));
  return atual.rankingConsultores.filter((r) => {
    if (!bateuMeta(r.percentualMeta)) return false;
    const a = antes.get(r.consultorId);
    // Quem não estava no ranking (primeira venda do mês) também conta, desde que agora tenha batido a meta.
    return a === undefined || !bateuMeta(a.percentualMeta);
  });
}

/** Regionais que passaram a ter a meta do mês batida entre as duas leituras. */
export function detectarMetasRegionaisBatidas(anterior: TvComercial, atual: TvComercial): TvRegional[] {
  if (!mesmoPeriodo(anterior, atual)) return [];
  const antes = new Map(anterior.rankingRegionais.map((r) => [r.regionalId, r]));
  return atual.rankingRegionais.filter((r) => {
    if (!bateuMeta(r.percentualMeta)) return false;
    const a = antes.get(r.regionalId);
    return a === undefined || !bateuMeta(a.percentualMeta);
  });
}

/**
 * Consultores que SUBIRAM no ranking (por vendas) entre as duas leituras, com quem foi ultrapassado. Só conta quem já estava no ranking e melhorou de
 * posição: descer porque outro passou na frente não é comemorado (aparece na lista de quem ultrapassou).
 */
export function detectarSubidasNoRanking(anterior: TvComercial, atual: TvComercial): Array<{ consultor: TvRanking; de: number; para: number; ultrapassou: string[]; ultrapassado: TvRanking | null }> {
  if (!mesmoPeriodo(anterior, atual)) return [];
  const antes = new Map(anterior.rankingConsultores.map((r) => [r.consultorId, r]));
  const subidas: Array<{ consultor: TvRanking; de: number; para: number; ultrapassou: string[]; ultrapassado: TvRanking | null }> = [];
  for (const r of atual.rankingConsultores) {
    const a = antes.get(r.consultorId);
    if (!a || r.posicao >= a.posicao) continue;
    // Ultrapassados: estavam à frente dele antes e agora estão atrás.
    const ultrapassou = anterior.rankingConsultores
      .filter((x) => x.consultorId !== r.consultorId && x.posicao < a.posicao)
      .filter((x) => (atual.rankingConsultores.find((y) => y.consultorId === x.consultorId)?.posicao ?? Number.MAX_SAFE_INTEGER) > r.posicao)
      .map((x) => x.nome);
    // Quem ocupava a posição que ele tomou (o ultrapassado mais próximo), já com os números atuais, para a animação da troca.
    const quemTinhaAPosicao = anterior.rankingConsultores.find((x) => x.consultorId !== r.consultorId && x.posicao === r.posicao);
    const ultrapassado = quemTinhaAPosicao ? atual.rankingConsultores.find((y) => y.consultorId === quemTinhaAPosicao.consultorId) ?? null : null;
    subidas.push({ consultor: r, de: a.posicao, para: r.posicao, ultrapassou, ultrapassado });
  }
  return subidas.sort((x, y) => x.para - y.para);
}

function mesmoPeriodo(a: TvComercial, b: TvComercial) {
  return a.periodo.mes === b.periodo.mes && a.periodo.ano === b.periodo.ano;
}

/** Horário em que o ranking é mostrado de hora em hora: do 1º horário do expediente ao último, na virada de cada hora. */
export const HORA_INICIAL_DO_RANKING = 8;
export const HORA_FINAL_DO_RANKING = 20;

/**
 * Se está na hora de mostrar o ranking: na virada da hora (primeiros minutos), dentro do horário do painel, e uma vez só por hora.
 * `ultimaHoraMostrada` é a chave "AAAA-MM-DD-HH" da última exibição.
 */
export function horaDoRanking(agora: Date, ultimaHoraMostrada: string | null, diaIso: string): { hora: number; chave: string } | null {
  const hora = agora.getHours();
  if (agora.getMinutes() > 2) return null;
  if (hora < HORA_INICIAL_DO_RANKING || hora > HORA_FINAL_DO_RANKING) return null;
  const chave = `${diaIso}-${hora}`;
  return chave === ultimaHoraMostrada ? null : { hora, chave };
}
