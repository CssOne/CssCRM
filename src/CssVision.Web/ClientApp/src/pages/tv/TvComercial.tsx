import { ArrowUp, RotateCcw, ChevronLeft, ChevronRight, CheckCircle2, CircleDollarSign, Activity, Pause, Play, Radio, RefreshCw, Satellite, ShoppingBag, SlidersHorizontal, Target, TrendingUp, ArrowUpRight, Wifi, WifiOff, X } from "lucide-react";
import { memo, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";
import { api, isAbortError } from "../../lib/api";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import type { TvAdministrativoIndicador, TvComercial, TvConversao, TvRanking, TvRegional, TvVenda } from "../../lib/types";
import { TvDetalhes, type Detalhe } from "./TvDetalhes";
import { dataBr, diaBrasilia, Foto, horaBr, moeda, moedaExata } from "./tvComum";
import { chaveDaCelebracao, detectarMetasBatidas, detectarMetasRegionaisBatidas, detectarSubidasNoRanking, horaDoRanking, type Celebracao } from "./tvEventos";
import "./tv.css";

type ModoRanking = "vendas" | "adesao" | "conversao";
type Tema = "light" | "dark";

const MODOS: ModoRanking[] = ["vendas", "adesao", "conversao"];
const ROTULOS: Record<ModoRanking, { aba: string; titulo: string }> = {
  vendas: { aba: "Vendas", titulo: "Ranking por vendas" },
  adesao: { aba: "Adesão", titulo: "Ranking por adesão" },
  conversao: { aba: "Conversão", titulo: "Ranking de conversão" },
};
const MEDALHAS = ["🥇", "🥈", "🥉"];
const INTERVALO_MS = 15_000;

/** Janela noturna: escuro a partir das 18h, claro de volta às 6h. `?tema=claro|escuro` força um tema (para testar). */
const ESCURO_A_PARTIR_DE = 18;
const CLARO_A_PARTIR_DE = 6;
function resolverTema(agora: Date, forcado: string | null): Tema {
  if (forcado === "claro" || forcado === "light") return "light";
  if (forcado === "escuro" || forcado === "dark") return "dark";
  const hora = agora.getHours();
  return hora >= ESCURO_A_PARTIR_DE || hora < CLARO_A_PARTIR_DE ? "dark" : "light";
}
const PALETA: Record<Tema, Record<"grid" | "axis" | "line" | "fill" | "tipBg" | "tipText" | "tipBorder", string>> = {
  light: { grid: "#d3e0ee", axis: "#4a627e", line: "#004384", fill: "#205f99", tipBg: "#ffffff", tipText: "#08192f", tipBorder: "#c5d7e8" },
  dark: { grid: "rgba(148,184,224,.16)", axis: "#8099b5", line: "#4fc3f7", fill: "#4fc3f7", tipBg: "#0e2038", tipText: "#f0f6fd", tipBorder: "rgba(148,184,224,.22)" },
};

function ha(valor: string, agora: Date) {
  const min = Math.max(0, Math.round((agora.getTime() - new Date(valor).getTime()) / 60000));
  return min < 1 ? "agora" : min < 60 ? `há ${min} min` : min < 1440 ? `há ${Math.floor(min / 60)}h` : `há ${Math.floor(min / 1440)}d`;
}
const diaDoMes = (iso: string) => Number(iso.slice(8, 10));

function Metrica({ rotulo, valor, dica, icone: Icone, comparacao, aoAbrir }: { rotulo: string; valor: string; dica: string; icone: typeof ShoppingBag; comparacao?: { delta: number; rotulo: string }; aoAbrir: () => void }) {
  const sobe = comparacao && comparacao.delta > 0.05;
  const desce = comparacao && comparacao.delta < -0.05;
  return (
    <button type="button" className="metric" onClick={aoAbrir} aria-label={`${rotulo}: ${valor}. Abrir detalhes por consultor`}>
      <div className="metric-icon"><Icone size={26} /></div>
      <div>
        <span>{rotulo}</span>
        <strong>{valor}</strong>
        <div className="metric-footer"><small>{dica}</small></div>
        {comparacao && (
          <div className={`metric-compare${sobe ? " up" : desce ? " down" : ""}`}>
            {sobe ? <ArrowUp size={11} /> : desce ? <ArrowUp size={11} className="rotate" /> : null}
            <span>{comparacao.delta >= 0 ? "+" : ""}{comparacao.delta.toFixed(0)}%</span>
            <small>{comparacao.rotulo}</small>
          </div>
        )}
      </div>
    </button>
  );
}

function LinhaRanking({ item, modo, maxAdesao, aoAbrir }: { item: TvRanking | TvConversao; modo: ModoRanking; maxAdesao: number; aoAbrir: () => void }) {
  const v = item as TvRanking;
  const c = item as TvConversao;
  const progresso = modo === "vendas" ? v.percentualMeta ?? 0 : modo === "adesao" ? (maxAdesao ? (v.valorVendido * 100) / maxAdesao : 0) : c.taxaConversao;
  return (
    <button type="button" className={`rank-row rank-row-${modo === "vendas" ? "sales" : modo === "adesao" ? "value" : "conversion"}${item.posicao <= 3 ? ` top-${item.posicao}` : ""}`} onClick={aoAbrir} aria-label={`${item.nome}. Abrir detalhes`}>
      <div className="rank-position">{MEDALHAS[item.posicao - 1] ?? String(item.posicao).padStart(2, "0")}</div>
      <Foto nome={item.nome} url={item.fotoUrl} />
      <div className="rank-person"><strong>{item.nome}</strong><span>{item.regional}</span></div>
      {modo === "conversao" ? (
        <>
          <div className="rank-sales"><strong>{c.leadsAtendidos}</strong><span>leads</span></div>
          <div className="rank-value">
            <strong>{c.taxaConversao.toFixed(1)}%</strong>
            <div className="progress"><i style={{ width: `${Math.min(progresso, 100)}%` }} /></div>
            <span>{c.vendasFechadas} fechados · {c.leadsPerdidos} perdidos</span>
          </div>
        </>
      ) : (
        <>
          <div className="rank-sales"><strong>{v.quantidadeVendas}</strong><span>{v.quantidadeVendas === 1 ? "venda" : "vendas"}</span></div>
          <div className="rank-value">
            <strong>{moeda.format(v.valorVendido)}</strong>
            <div className="progress"><i style={{ width: `${Math.min(progresso, 100)}%` }} /></div>
            <span>{modo === "vendas" ? (v.percentualMeta == null ? "Meta não cadastrada" : `${v.percentualMeta.toFixed(0)}% da meta`) : "valor de adesão"}</span>
          </div>
        </>
      )}
    </button>
  );
}

function PainelRankingsBase({ dados, periodo, aoAbrir }: { dados: TvComercial; periodo: string; aoAbrir: (d: Detalhe) => void }) {
  const [modo, setModo] = useState<ModoRanking>("vendas");
  const [pausado, setPausado] = useState(false);
  const lista = useRef<HTMLDivElement>(null);
  const itens: Array<TvRanking | TvConversao> = modo === "vendas" ? dados.rankingConsultores : modo === "adesao" ? dados.rankingValorAdesao : dados.rankingConversao;
  const maxAdesao = Math.max(...dados.rankingValorAdesao.map((i) => i.valorVendido), 0);

  // Rolagem automática: desce, volta e passa para o próximo ranking (vendas → adesão → conversão).
  useEffect(() => {
    const el = lista.current;
    if (!el || pausado) return;
    el.scrollTop = 0;
    const proximo = () => setModo((atual) => MODOS[(MODOS.indexOf(atual) + 1) % MODOS.length]);
    let quadro = 0;
    let troca = 0;
    let anterior = performance.now();
    let esperaAte = anterior + 1_600;
    let direcao: 1 | -1 = 1;
    const passo = (agora: number) => {
      const maximo = Math.max(0, el.scrollHeight - el.clientHeight);
      if (maximo <= 1) {
        if (!troca) troca = window.setTimeout(proximo, 6_000);
        return;
      }
      if (agora >= esperaAte) {
        const decorrido = Math.min(64, agora - anterior);
        el.scrollTop += (direcao * Math.max(48, maximo / 14) * decorrido) / 1_000;
        if (direcao === 1 && el.scrollTop >= maximo - 1) { el.scrollTop = maximo; direcao = -1; esperaAte = agora + 1_600; }
        else if (direcao === -1 && el.scrollTop <= 1) { el.scrollTop = 0; troca = window.setTimeout(proximo, 1_600); return; }
      }
      anterior = agora;
      quadro = requestAnimationFrame(passo);
    };
    quadro = requestAnimationFrame(passo);
    return () => { cancelAnimationFrame(quadro); if (troca) window.clearTimeout(troca); };
  }, [modo, itens.length, pausado]);

  return (
    <div className="panel ranking">
      <div className="panel-title ranking-title">
        <div><span className="eyebrow">DESEMPENHO INDIVIDUAL</span><h1>{ROTULOS[modo].titulo}</h1></div>
        <div className="period-row">
          <span className="period">{periodo}</span>
          <button type="button" className="scroll-toggle" onClick={() => setPausado((p) => !p)} aria-label={pausado ? "Continuar rolagem automática" : "Parar rolagem automática"} aria-pressed={pausado}>
            {pausado ? <Play /> : <Pause />}
          </button>
        </div>
      </div>
      <div className="ranking-tabs" role="tablist" aria-label="Tipo de ranking">
        {MODOS.map((m) => (
          <button key={m} type="button" role="tab" aria-selected={m === modo} className={m === modo ? "active" : ""} onClick={() => setModo(m)}>{ROTULOS[m].aba}</button>
        ))}
      </div>
      <div className={`ranking-auto-status${pausado ? " paused" : ""}`}><span>{pausado ? "Rolagem pausada" : "Rolagem automática"}</span><i aria-hidden="true" /></div>
      <div className="ranking-list" ref={lista}>
        {itens.map((i) => <LinhaRanking key={i.consultorId} item={i} modo={modo} maxAdesao={maxAdesao} aoAbrir={() => aoAbrir({ tipo: "consultor", id: i.consultorId })} />)}
        {!itens.length && <div className="empty">{modo === "conversao" ? "Nenhum lead recebido neste período" : "Nenhuma venda neste período"}</div>}
      </div>
    </div>
  );
}
const PainelRankings = memo(PainelRankingsBase);

const ICONES_ADMINISTRATIVO: Record<string, typeof Activity> = { reintegration: RefreshCw, claim: CheckCircle2, tracker: Satellite };

function CartaoAdministrativo({ item, aoAbrir }: { item: TvAdministrativoIndicador; aoAbrir: () => void }) {
  const Icone = ICONES_ADMINISTRATIVO[item.id] ?? Activity;
  return (
    <button type="button" className={`administrative-card administrative-card-${item.id}`} onClick={aoAbrir} aria-label={`${item.rotulo}: ${item.total}. Abrir detalhes`}>
      <div className="administrative-icon"><Icone /></div>
      <div className="administrative-card-main">
        <span>{item.rotulo}</span>
        <strong>{item.total}</strong>
        <small>{item.hoje} {item.hoje === 1 ? "registro hoje" : "registros hoje"}</small>
      </div>
      <div className="administrative-person"><span>Último registro</span><strong>{item.ultimo?.pessoa || "Sem registros"}</strong></div>
    </button>
  );
}

function PainelAdministrativo({ indicadores, aoAbrir }: { indicadores?: TvAdministrativoIndicador[] | null; aoAbrir: (d: Detalhe) => void }) {
  return (
    <div className="panel administrative">
      <div className="panel-title compact-title">
        <div><span className="eyebrow">ACOMPANHAMENTO OPERACIONAL</span><h1>Administrativo</h1></div>
        <span className={`administrative-hint${indicadores ? "" : " error"}`}>{indicadores ? "Dados do Notion" : "Notion indisponível"}</span>
      </div>
      <div className="administrative-grid">
        {(indicadores ?? []).map((i) => <CartaoAdministrativo key={i.id} item={i} aoAbrir={() => aoAbrir({ tipo: "admin", id: i.id })} />)}
      </div>
    </div>
  );
}

function PainelRegionais({ itens, compacto = false, aoAbrir }: { itens: TvRegional[]; compacto?: boolean; aoAbrir: (d: Detalhe) => void }) {
  return (
    <div className={`panel regions${compacto ? " regions-compact" : ""}`}>
      <div className="panel-title compact-title"><div><span className="eyebrow">PERFORMANCE</span><h1>Ranking regional</h1></div></div>
      <div className="region-list">
        {itens.slice(0, compacto ? 2 : 4).map((x) => (
          <button type="button" className="region-row" key={x.regionalId} onClick={() => aoAbrir({ tipo: "regional", id: x.regionalId })} aria-label={`Regional ${x.nome}. Abrir detalhes`}>
            <b>{x.posicao}</b>
            <div>
              <strong>{x.nome}</strong>
              <span>{x.quantidadeMeta ? `${x.quantidadeVendas} / ${x.quantidadeMeta} vendas` : `${x.quantidadeVendas} vendas`} · {moeda.format(x.valorTotal)}</span>
              <i><em style={{ width: `${Math.min(x.percentualMeta ?? x.percentualParticipacao, 100)}%` }} /></i>
            </div>
            <strong>{x.percentualMeta == null ? `${x.percentualParticipacao.toFixed(0)}%` : `${x.percentualMeta.toFixed(1)}%`}</strong>
          </button>
        ))}
        {!itens.length && <div className="empty">Sem regionais no período</div>}
      </div>
    </div>
  );
}

function PainelUltimas({ itens, agora, aoAbrir, aoRepetir }: { itens: TvVenda[]; agora: Date; aoAbrir: (d: Detalhe) => void; aoRepetir: (v: TvVenda) => void }) {
  return (
    <div className="panel latest">
      <div className="panel-title compact-title"><div><span className="eyebrow">TEMPO REAL</span><h1>Últimas vendas</h1></div><Activity size={20} className="pulse" /></div>
      <div className="sales-list">
        {itens.slice(0, 5).map((x) => {
          const abrir = () => aoAbrir({ tipo: "venda", id: x.vendaId });
          // A linha inteira abre os detalhes; o botão de repetir fica dentro dela, por isso a linha não pode ser um <button>.
          return (
            <div className="sale-row" key={x.vendaId} role="button" tabIndex={0} onClick={abrir} aria-label={`Venda de ${x.consultor}. Abrir detalhes`}
              onKeyDown={(e) => { if (e.target === e.currentTarget && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); abrir(); } }}>
              <Foto nome={x.consultor} url={x.fotoUrl} />
              <div><strong>{x.consultor}</strong><span>{x.regional}{x.cliente ? ` · ${x.cliente}` : ""}</span></div>
              <div className="sale-value-wrap">
                <div className="sale-value"><strong>{moeda.format(x.valor)}</strong><span>{ha(x.atualizadaEm, agora)}</span></div>
                <button type="button" className="sale-replay" title="Repetir animação da venda" aria-label={`Repetir a animação da venda de ${x.consultor}`}
                  onClick={(e) => { e.stopPropagation(); aoRepetir(x); }}>
                  <RotateCcw />
                </button>
              </div>
            </div>
          );
        })}
        {!itens.length && <div className="empty">Nenhuma venda neste período</div>}
      </div>
    </div>
  );
}

function Comemoracao({ venda, posicao, vendasNoMes, aoFechar }: { venda: TvVenda; posicao: number; vendasNoMes: number; aoFechar: () => void }) {
  useEffect(() => { const id = window.setTimeout(aoFechar, 60_000); return () => window.clearTimeout(id); }, [aoFechar]);
  // Rede de segurança para TVs lentas: as animações de entrada prendem o card invisível até começarem.
  const raiz = useRef<HTMLDivElement>(null);
  useEffect(() => { const id = window.setTimeout(() => raiz.current?.classList.add("celebration-settled"), 2_500); return () => window.clearTimeout(id); }, []);
  const registrada = new Date(venda.atualizadaEm);
  return (
    <div className="celebration sale-celebration" role="status" ref={raiz}>
      <div className="celebration-rays" aria-hidden="true" />
      <div className="sale-celebration-waves" aria-hidden="true"><i /><i /><i /></div>
      <div className="confetti-rain sale-confetti" aria-hidden="true">
        {Array.from({ length: 36 }, (_, i) => <i key={i} style={{ left: `${(i * 31 + 2) % 100}%`, animationDelay: `-${(i * 0.41) % 5}s` }} />)}
      </div>
      <div className="celebration-card sale-celebration-card">
        <button type="button" className="sale-celebration-close" onClick={aoFechar} aria-label="Fechar animação"><X /></button>
        <div className="sale-celebration-symbol" aria-hidden="true"><CheckCircle2 /></div>
        <span className="eyebrow">NOVA VENDA REALIZADA</span>
        <div className="sale-celebration-person">
          <Foto nome={venda.consultor} url={venda.fotoUrl} grande />
          <div><span>PARABÉNS!</span><h2>{venda.consultor}</h2><small>Regional {venda.regional}</small></div>
        </div>
        <div className="celebration-value"><span>VALOR DA ADESÃO</span><strong>{moedaExata.format(venda.valor)}</strong><i aria-hidden="true" /></div>
        <div className="celebration-datetime">
          <div><span>DATA DA VENDA</span><strong>{dataBr.format(new Date(venda.dataVenda))}</strong></div>
          <div><span>HORA DA VENDA</span><strong>{horaBr.format(registrada)}</strong></div>
        </div>
        <div className="celebration-stats">
          <span><b>{vendasNoMes}</b> vendas no mês</span>
          {posicao > 0 && <span><b>{posicao}ª</b> posição no ranking</span>}
        </div>
        <div className="celebration-message">✓ MAIS UMA CONQUISTA PARA CELEBRAR!</div>
      </div>
    </div>
  );
}

/** Meta batida, meta regional, subida no ranking e ranking da hora: usam o visual "milestone" do painel (raios, fogos, confete e cartão dourado). */
function ComemoracaoEvento({ item, aoFechar }: { item: Exclude<Celebracao, { tipo: "venda" }>; aoFechar: () => void }) {
  useEffect(() => { const id = window.setTimeout(aoFechar, item.tipo === "hora" ? 30_000 : 25_000); return () => window.clearTimeout(id); }, [aoFechar, item.tipo]);
  // Rede de segurança para TVs lentas (mesma da comemoração de venda).
  const raiz = useRef<HTMLDivElement>(null);
  useEffect(() => { const id = window.setTimeout(() => raiz.current?.classList.add("celebration-settled"), 6_000); return () => window.clearTimeout(id); }, []);
  const classeFundo = item.tipo === "hora" ? "hourly-ranking-celebration" : item.tipo === "posicao" ? "rank-up-celebration" : "";
  const classeCartao = item.tipo === "hora" ? "podium-card hourly-ranking-card" : item.tipo === "posicao" ? "rank-up-card" : item.tipo === "meta" ? "consultant-goal-card" : "regional-goal-card";
  return (
    <div className={`celebration milestone-celebration ${classeFundo}`} role="status" ref={raiz}>
      <div className="milestone-rays" aria-hidden="true" />
      <div className="milestone-fireworks" aria-hidden="true">{Array.from({ length: 8 }, (_, i) => <i key={i} />)}</div>
      <div className="confetti-rain milestone-confetti" aria-hidden="true">
        {Array.from({ length: 40 }, (_, i) => <i key={i} style={{ left: `${(i * 29 + 3) % 100}%`, animationDelay: `-${(i * 0.37) % 5}s` }} />)}
      </div>
      <div className="milestone-stars" aria-hidden="true">{Array.from({ length: 12 }, (_, i) => <span key={i}>★</span>)}</div>
      <div className={`celebration-card milestone-card ${classeCartao}`}>
        <button type="button" className="milestone-close" onClick={aoFechar} aria-label="Fechar animação"><X /></button>
        {item.tipo === "meta" && <MetaDoConsultor consultor={item.consultor} />}
        {item.tipo === "meta-regional" && <MetaRegional regional={item.regional} />}
        {item.tipo === "posicao" && <SubidaNoRanking item={item} />}
        {item.tipo === "hora" && <RankingDaHora item={item} />}
      </div>
    </div>
  );
}

function MetaDoConsultor({ consultor }: { consultor: TvRanking }) {
  const pct = consultor.percentualMeta ?? 100;
  return (
    <>
      <div className="consultant-goal-target" aria-hidden="true"><Target /></div>
      <span className="milestone-kicker">META INDIVIDUAL BATIDA</span>
      <h2>PARABÉNS!</h2>
      <div className="consultant-goal-person">
        <Foto nome={consultor.nome} url={consultor.fotoUrl} grande />
        <div><span>CONSULTOR(A)</span><strong>{consultor.nome}</strong><small>Regional {consultor.regional}</small></div>
      </div>
      <div className="consultant-goal-progress">
        <div><i style={{ "--consultant-goal-progress": `${Math.min(pct, 100)}%` } as CSSProperties} /><strong>{pct.toFixed(0)}% da meta</strong></div>
      </div>
      <div className="regional-goal-numbers">
        <div><strong>{consultor.quantidadeVendas}</strong><span>{consultor.quantidadeVendas === 1 ? "venda" : "vendas"} no mês</span></div>
        <div><strong>{moeda.format(consultor.valorVendido)}</strong><span>em adesões</span></div>
      </div>
      <div className="milestone-banner">★ META DO MÊS CONQUISTADA! ★</div>
    </>
  );
}

function MetaRegional({ regional }: { regional: TvRegional }) {
  const pct = regional.percentualMeta ?? 100;
  return (
    <>
      <div className="regional-goal-hero">
        <div className="regional-goal-trophy" aria-hidden="true"><i /><i /><span>🏆</span></div>
        <div>
          <span className="milestone-kicker">META REGIONAL BATIDA</span>
          <h2>PARABÉNS!</h2>
          <span className="regional-goal-name">{regional.nome}</span>
        </div>
      </div>
      <div className="regional-goal-achievement">
        <div className="regional-goal-progress"><span style={{ "--goal-progress": `${Math.min(pct, 100)}%` } as CSSProperties}><b>{pct.toFixed(0)}%</b><small>DA META</small></span></div>
        <div className="regional-goal-summary">
          <p>A regional {regional.nome} alcançou a meta de vendas do mês.</p>
          <div className="regional-goal-numbers">
            <div><strong>{regional.quantidadeMeta ? `${regional.quantidadeVendas}/${regional.quantidadeMeta}` : regional.quantidadeVendas}</strong><span>vendas</span></div>
            <div><strong>{moeda.format(regional.valorTotal)}</strong><span>em adesões</span></div>
          </div>
        </div>
      </div>
      <div className="milestone-banner">★ A REGIONAL BATEU A META! ★</div>
    </>
  );
}

function SubidaNoRanking({ item }: { item: Extract<Celebracao, { tipo: "posicao" }> }) {
  const { consultor, de, para, ultrapassado } = item;
  const subiu = de - para;
  return (
    <>
      <div className="rank-up-heading">
        <div className="rank-up-icon" aria-hidden="true"><ArrowUp /></div>
        <div>
          <span className="milestone-kicker">SUBIU NO RANKING</span>
          <h2>{consultor.nome}</h2>
          <p>Regional {consultor.regional} · {consultor.quantidadeVendas} {consultor.quantidadeVendas === 1 ? "venda" : "vendas"} no mês</p>
        </div>
      </div>
      <div className="rank-up-zoom-scene">
        <div className="rank-up-live"><span><i />AO VIVO</span><span>RANKING POR VENDAS</span></div>
        <div className="rank-up-ladder">
          <div className="rank-up-guide rank-up-guide-target"><b>{para}º</b><span>NOVA POSIÇÃO</span></div>
          <div className="rank-up-guide rank-up-guide-origin"><b>{de}º</b><span>POSIÇÃO ANTERIOR</span></div>
          <div className="rank-up-path" aria-hidden="true"><ArrowUp /></div>
          {ultrapassado && (
            <div className="rank-up-ranking-row rank-up-overtaken-row">
              <b>{para + 1}º</b>
              <Foto nome={ultrapassado.nome} url={ultrapassado.fotoUrl} />
              <div><strong>{ultrapassado.nome}</strong><small>{ultrapassado.quantidadeVendas} {ultrapassado.quantidadeVendas === 1 ? "venda" : "vendas"}</small></div>
              <div className="rank-up-row-indicator" aria-hidden="true"><ArrowUp className="rotate" /></div>
            </div>
          )}
          <div className="rank-up-ranking-row rank-up-climber-row">
            <div className="rank-up-position-swap"><span>{de}º</span><em>{para}º</em></div>
            <Foto nome={consultor.nome} url={consultor.fotoUrl} />
            <div><strong>{consultor.nome}</strong><small>{consultor.quantidadeVendas} {consultor.quantidadeVendas === 1 ? "venda" : "vendas"}</small></div>
            <div className="rank-up-row-indicator" aria-hidden="true"><ArrowUp /></div>
          </div>
        </div>
      </div>
      <div className="rank-up-result">
        <span><ArrowUp />SUBIU {subiu} {subiu === 1 ? "POSIÇÃO" : "POSIÇÕES"}</span>
        <strong>{de}º → {para}º</strong>
      </div>
      <div className="milestone-banner">▲ UMA POSIÇÃO MAIS PERTO DO TOPO!</div>
    </>
  );
}

function RankingDaHora({ item }: { item: Extract<Celebracao, { tipo: "hora" }> }) {
  const podio = item.ranking.slice(0, 3);
  return (
    <>
      <div className="podium-header-icons" aria-hidden="true"><span>⭐</span><b>🏆</b><span>⭐</span></div>
      <span className="milestone-kicker">RANKING DAS {String(item.hora).padStart(2, "0")}H</span>
      <h2>Quem lidera o mês</h2>
      {podio.length === 0 ? (
        <p>Nenhuma venda registrada neste mês até agora.</p>
      ) : (
        <>
          <div className="podium-stage">
            <div className="podium-spotlights" aria-hidden="true"><i /><i /><i /></div>
            <div className="monthly-podium">
              {podio.map((r) => (
                <div className={`podium-winner podium-position-${r.posicao}`} key={r.consultorId}>
                  <span className="podium-medal" aria-hidden="true">{MEDALHAS[r.posicao - 1]}</span>
                  <span className="podium-place">{r.posicao}º LUGAR</span>
                  <Foto nome={r.nome} url={r.fotoUrl} grande />
                  <strong>{r.nome}</strong>
                  <small>{r.regional}</small>
                  <div><b>{r.quantidadeVendas}</b><span>{r.quantidadeVendas === 1 ? "venda" : "vendas"}</span></div>
                  <em>{moeda.format(r.valorVendido)}</em>
                </div>
              ))}
            </div>
            <div className="podium-floor" aria-hidden="true"><i /><i /><i /></div>
          </div>
          {item.regionais.length > 0 && (
            <p>{item.regionais.map((g) => `${g.posicao}º ${g.nome}: ${g.quantidadeVendas} ${g.quantidadeVendas === 1 ? "venda" : "vendas"}${g.percentualMeta != null ? ` (${g.percentualMeta.toFixed(0)}% da meta)` : ""}`).join("  ·  ")}</p>
          )}
        </>
      )}
      <div className="milestone-banner">⏰ RANKING DE HORA EM HORA</div>
    </>
  );
}

const idDaFila = (c: Celebracao) => (c.tipo === "venda" ? `venda:${c.venda.vendaId}` : chaveDaCelebracao(c, { mes: 0, ano: 0 }));

/**
 * Painel comercial da TV (/tv/comercial): abre em outra aba do navegador, em tela cheia, e lê direto do CRM —
 * vendas, adesão, conversão, regionais e evolução do mês, com atualização ao vivo e comemoração de nova venda.
 * Mesmo visual do painel antigo; o tema escuro entra sozinho às 18h e o claro volta às 6h.
 */
export function TvComercialPage() {
  const [dados, setDados] = useState<TvComercial | null>(null);
  const [anterior, setAnterior] = useState<TvComercial | null>(null);
  const [erro, setErro] = useState(false);
  const [periodo, setPeriodo] = useState<{ mes: number; ano: number } | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [agora, setAgora] = useState(new Date());
  const [fila, setFila] = useState<Celebracao[]>([]);
  // Janela de detalhes: pilha, para navegar de um card ao consultor/venda e voltar.
  const [pilha, setPilha] = useState<Detalhe[]>([]);
  const abrirDetalhe = useCallback((d: Detalhe) => setPilha((p) => [...p, d]), []);
  const voltarDetalhe = useCallback(() => setPilha((p) => p.slice(0, -1)), []);
  const fecharDetalhe = useCallback(() => setPilha([]), []);
  const [forcado] = useState(() => new URLSearchParams(window.location.search).get("tema"));
  const vistas = useRef<Set<string> | null>(null);
  // Leitura anterior do mês corrente (para descobrir meta batida / subida no ranking), comemorações já feitas nesta sessão e última hora cheia mostrada.
  const leituraAnterior = useRef<TvComercial | null>(null);
  const celebradas = useRef<Set<string>>(new Set());
  const ultimaHoraMostrada = useRef<string | null>(null);
  const audio = useRef<HTMLAudioElement>(null);
  const atual = periodo === null;
  const tema = resolverTema(agora, forcado);
  const paleta = PALETA[tema];

  useAtualizarAoVivo(useCallback(() => setRecarregar((n) => n + 1), []));

  useEffect(() => {
    const id = window.setInterval(() => setRecarregar((n) => n + 1), INTERVALO_MS);
    return () => window.clearInterval(id);
  }, []);
  // O relógio (e, com ele, o tema) acorda na virada do minuto: o escuro entra às 18:00 em ponto.
  useEffect(() => {
    let id = 0;
    const agendar = () => { id = window.setTimeout(() => { setAgora(new Date()); agendar(); }, 60_000 - (Date.now() % 60_000) + 50); };
    agendar();
    return () => window.clearTimeout(id);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const query = periodo ? `?mes=${periodo.mes}&ano=${periodo.ano}` : "";
    api
      .get<TvComercial>(`/crm/tv/comercial${query}`, controller.signal)
      .then((novo) => {
        setDados(novo);
        setErro(false);
        if (!atual) return;
        // Comemoração: vendas novas de hoje que apareceram depois da primeira carga (a primeira só marca o que já existia).
        const hoje = diaBrasilia.format(new Date());
        const antes = leituraAnterior.current;
        leituraAnterior.current = novo;
        if (vistas.current === null) { vistas.current = new Set(novo.ultimasVendas.map((v) => v.vendaId)); return; }
        const novas = novo.ultimasVendas.filter((v) => !vistas.current!.has(v.vendaId));
        novo.ultimasVendas.forEach((v) => vistas.current!.add(v.vendaId));
        const celebrar = novas.filter((v) => v.valor > 0 && diaBrasilia.format(new Date(v.dataVenda)) === hoje).slice(0, 3);
        const eventos: Celebracao[] = celebrar.map((venda) => {
          const rank = novo.rankingConsultores.find((r) => r.nome === venda.consultor);
          return { tipo: "venda", venda, posicao: rank?.posicao ?? 0, vendasNoMes: rank?.quantidadeVendas ?? 0 };
        });
        // Depois da venda, o que ela provocou: meta batida, meta regional e subida no ranking (comparando com a leitura anterior).
        if (antes) {
          const candidatos: Array<Exclude<Celebracao, { tipo: "venda" } | { tipo: "hora" }>> = [
            ...detectarMetasBatidas(antes, novo).map((consultor) => ({ tipo: "meta" as const, consultor })),
            ...detectarMetasRegionaisBatidas(antes, novo).map((regional) => ({ tipo: "meta-regional" as const, regional })),
            ...detectarSubidasNoRanking(antes, novo).slice(0, 2).map((s) => ({ tipo: "posicao" as const, ...s })),
          ];
          for (const c of candidatos) {
            const chave = chaveDaCelebracao(c, novo.periodo);
            if (celebradas.current.has(chave)) continue;
            celebradas.current.add(chave);
            eventos.push(c);
          }
        }
        if (eventos.length) setFila((f) => [...f, ...eventos]);
      })
      .catch((e) => { if (!isAbortError(e)) setErro(true); });
    return () => controller.abort();
  }, [recarregar, periodo, atual]);

  // Mês anterior, para a comparação nos cartões do topo (consulta rara: ele quase não muda).
  const mesAtualDoPainel = dados ? `${dados.periodo.ano}-${dados.periodo.mes}` : null;
  useEffect(() => {
    if (!dados) return;
    const mes = dados.periodo.mes === 1 ? 12 : dados.periodo.mes - 1;
    const ano = dados.periodo.mes === 1 ? dados.periodo.ano - 1 : dados.periodo.ano;
    const controller = new AbortController();
    api.get<TvComercial>(`/crm/tv/comercial?mes=${mes}&ano=${ano}`, controller.signal).then(setAnterior).catch(() => undefined);
    return () => controller.abort();
    // só quando o mês exibido muda
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mesAtualDoPainel]);

  // Ranking de hora em hora: na virada da hora (8h às 20h), com o painel do mês corrente aberto e sem outra comemoração na frente.
  useEffect(() => {
    if (!dados || !atual) return;
    const chaveDia = diaBrasilia.format(agora);
    const hora = horaDoRanking(agora, ultimaHoraMostrada.current, chaveDia);
    if (!hora) return;
    ultimaHoraMostrada.current = hora.chave;
    setFila((f) => [...f, { tipo: "hora", hora: hora.hora, ranking: dados.rankingConsultores.slice(0, 5), regionais: dados.rankingRegionais.slice(0, 3) }]);
  }, [agora, dados, atual]);

  const ativa = fila[0];
  useEffect(() => {
    if (!ativa || !audio.current) return;
    if (ativa.tipo === "hora" || ativa.tipo === "posicao") return; // só venda e metas têm som
    audio.current.currentTime = 0;
    audio.current.volume = 0.85;
    void audio.current.play().catch(() => undefined);
  }, [ativa]);
  const fecharAtiva = useCallback(() => setFila((f) => f.slice(1)), []);
  // Botão "repetir" das últimas vendas: coloca a venda de novo na fila da comemoração (uma vez só por venda enquanto ela estiver na fila).
  const repetirAnimacao = useCallback((venda: TvVenda) => {
    const rank = dados?.rankingConsultores.find((r) => r.nome === venda.consultor);
    setFila((f) => (f.some((i) => i.tipo === "venda" && i.venda.vendaId === venda.vendaId) ? f : [...f, { tipo: "venda", venda, posicao: rank?.posicao ?? 0, vendasNoMes: rank?.quantidadeVendas ?? 0 }]));
  }, [dados]);

  const mudarMes = (dir: 1 | -1) => {
    if (!dados) return;
    const real = { mes: new Date().getMonth() + 1, ano: new Date().getFullYear() };
    let { mes, ano } = periodo ?? dados.periodo;
    mes += dir;
    if (mes < 1) { mes = 12; ano -= 1; }
    if (mes > 12) { mes = 1; ano += 1; }
    if (ano > real.ano || (ano === real.ano && mes > real.mes)) return;
    setPeriodo(ano === real.ano && mes === real.mes ? null : { mes, ano });
  };

  const grafico = useMemo(() => dados?.evolucaoMensal.map((d) => ({ dia: diaDoMes(d.data), diaQtd: d.quantidadeVendasDia, acumulado: d.quantidadeAcumulada })) ?? [], [dados]);
  const opcoes: ApexOptions = useMemo(() => ({
    chart: { type: "line", toolbar: { show: false }, background: "transparent", foreColor: paleta.axis, animations: { enabled: false }, fontFamily: "inherit" },
    theme: { mode: tema === "dark" ? "dark" : "light" },
    grid: { borderColor: paleta.grid, strokeDashArray: 3, xaxis: { lines: { show: false } } },
    stroke: { width: [0, 4], curve: "smooth" },
    plotOptions: { bar: { borderRadius: 5, columnWidth: "42%" } },
    colors: [paleta.fill, paleta.line],
    dataLabels: { enabled: false },
    legend: { show: false },
    xaxis: { categories: grafico.map((g) => g.dia), labels: { style: { fontSize: "13px" } }, axisBorder: { show: false }, axisTicks: { show: false } },
    yaxis: [
      { labels: { formatter: (v) => String(Math.round(v)), style: { fontSize: "12px" } } },
      { opposite: true, labels: { formatter: (v) => String(Math.round(v)), style: { fontSize: "12px" } } },
    ],
    tooltip: { theme: tema, shared: true, y: { formatter: (v) => `${Math.round(v)} vendas` } },
  }), [grafico, paleta, tema]);

  if (!dados) {
    return (
      <div className="tvx" data-theme={tema}>
        <main className="dashboard"><div className="stale">{erro ? "Não foi possível carregar o painel. Tentando novamente…" : "Carregando painel…"}</div></main>
      </div>
    );
  }

  const diaHoje = agora.getDate();
  const diasNoMes = new Date(dados.periodo.ano, dados.periodo.mes, 0).getDate();
  const decorridos = atual ? diaHoje : diasNoMes;
  const mediaPorDia = decorridos ? dados.resumo.vendasNoMes / decorridos : 0;
  const projecao = Math.round(mediaPorDia * diasNoMes);
  const periodoRotulo = new Date(dados.periodo.ano, dados.periodo.mes - 1).toLocaleDateString("pt-BR", { month: "long", year: "numeric" });

  // Comparação com o mês anterior: no mês corrente, no MESMO ponto do calendário (não com o mês anterior inteiro).
  const delta = (corrente: number, base: number | undefined) => (base ? ((corrente - base) * 100) / base : undefined);
  const noDia = anterior?.evolucaoMensal.filter((e) => diaDoMes(e.data) <= diaHoje).at(-1);
  const mesmoDia = anterior?.evolucaoMensal.find((e) => diaDoMes(e.data) === diaHoje);
  const comparacoes = anterior ? {
    vendasHoje: atual ? delta(dados.resumo.vendasHoje, mesmoDia?.quantidadeVendasDia) : undefined,
    vendasMes: delta(dados.resumo.vendasNoMes, atual ? noDia?.quantidadeAcumulada : anterior.resumo.vendasNoMes),
    valorHoje: atual ? delta(dados.resumo.valorHoje, mesmoDia?.valorVendidoDia) : undefined,
    valorMes: delta(dados.resumo.valorNoMes, atual ? noDia?.valorAcumulado : anterior.resumo.valorNoMes),
  } : undefined;
  const cmp = (valor: number | undefined, rotulo: string) => (valor == null ? undefined : { delta: valor, rotulo });

  return (
    <div className="tvx" data-theme={tema}>
      <main className="dashboard">
        <button type="button" className="month-nav month-nav-prev" onClick={() => mudarMes(-1)} aria-label="Ver mês anterior"><ChevronLeft /></button>
        <button type="button" className="month-nav month-nav-next" onClick={() => mudarMes(1)} disabled={atual} aria-label="Ver próximo mês"><ChevronRight /></button>
        <header>
          <div className="brand">
            <img className="brand-logo" src="/css-brasil-logo.png" alt="CSS Brasil" width={43} height={43} />
            <div><strong>CSS BRASIL</strong><span>PAINEL COMERCIAL</span></div>
          </div>
          <div className="live"><Radio size={18} /><span>AO VIVO</span></div>
          <div className="header-right">
            <a className="goals-button" href="/app/crm/goals" target="_blank" rel="noopener"><SlidersHorizontal /><span>Editar metas</span></a>
            <div className={`status ${erro ? "offline" : "online"}`}>{erro ? <WifiOff size={20} /> : <Wifi size={20} />}{erro ? "Reconectando" : "Conectado"}</div>
            <div className="clock">
              <strong>{agora.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}</strong>
              <span>{agora.toLocaleDateString("pt-BR", { weekday: "long", day: "2-digit", month: "long" })}</span>
            </div>
          </div>
        </header>
        {erro && <div className="stale">Exibindo os últimos dados válidos · atualização automática em andamento</div>}
        <section className="content">
          <div className="left-column"><PainelRankings dados={dados} periodo={periodoRotulo} aoAbrir={abrirDetalhe} /></div>
          <div className="right-column">
            <div className="metrics">
              <Metrica rotulo="VENDAS HOJE" valor={String(dados.resumo.vendasHoje)} dica="negócios confirmados" icone={ShoppingBag} comparacao={cmp(comparacoes?.vendasHoje, "vs mesmo dia, mês anterior")} aoAbrir={() => abrirDetalhe({ tipo: "metrica", id: "vendasHoje" })} />
              <Metrica rotulo="VENDAS NO MÊS" valor={String(dados.resumo.vendasNoMes)} dica="volume acumulado" icone={TrendingUp} comparacao={cmp(comparacoes?.vendasMes, "vs mês anterior")} aoAbrir={() => abrirDetalhe({ tipo: "metrica", id: "vendasMes" })} />
              <Metrica rotulo="VALOR HOJE" valor={moeda.format(dados.resumo.valorHoje)} dica="faturamento do dia" icone={CircleDollarSign} comparacao={cmp(comparacoes?.valorHoje, "vs mesmo dia, mês anterior")} aoAbrir={() => abrirDetalhe({ tipo: "metrica", id: "valorHoje" })} />
              <Metrica rotulo="VALOR NO MÊS" valor={moeda.format(dados.resumo.valorNoMes)} dica="faturamento acumulado" icone={ArrowUpRight} comparacao={cmp(comparacoes?.valorMes, "vs mês anterior")} aoAbrir={() => abrirDetalhe({ tipo: "metrica", id: "valorMes" })} />
            </div>
            <div className="panel evolution" role="button" tabIndex={0} style={{ cursor: "pointer" }} aria-label="Evolução das vendas. Abrir detalhes por consultor"
              onClick={() => abrirDetalhe({ tipo: "evolucao" })} onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); abrirDetalhe({ tipo: "evolucao" }); } }}>
              <div className="panel-title">
                <div><span className="eyebrow">RITMO DO MÊS</span><h1>Evolução das vendas</h1></div>
                <div className="goal"><Target size={18} /><b>{dados.resumo.percentualMetaGeral == null ? "—" : `${dados.resumo.percentualMetaGeral.toFixed(1)}%`}</b><span>da meta geral</span></div>
              </div>
              <div className="chart-summary">
                <span><small>HOJE</small><b>{dados.resumo.vendasHoje}</b></span>
                <span><small>MÉDIA / DIA</small><b>{mediaPorDia.toFixed(1)}</b></span>
                <span><small>PROJEÇÃO DO MÊS</small><b>{projecao}</b></span>
                <div className="chart-legend"><span><i className="daily-dot" />Vendas no dia</span><span><i className="total-dot" />Total acumulado</span></div>
              </div>
              <div className="chart">
                <Chart
                  key={tema}
                  type="line"
                  height="100%"
                  options={opcoes}
                  series={[
                    { name: "No dia", type: "column", data: grafico.map((g) => g.diaQtd) },
                    { name: "Acumulado", type: "line", data: grafico.map((g) => g.acumulado) },
                  ]}
                />
              </div>
            </div>
            <div className="bottom-grid bottom-grid-administrative">
              <div className="operational-stack">
                <PainelAdministrativo indicadores={dados.administrativo?.indicadores} aoAbrir={abrirDetalhe} />
                <PainelRegionais itens={dados.rankingRegionais} compacto aoAbrir={abrirDetalhe} />
              </div>
              <PainelUltimas itens={dados.ultimasVendas} agora={agora} aoAbrir={abrirDetalhe} aoRepetir={repetirAnimacao} />
            </div>
          </div>
        </section>
        <footer>
          <span>Atualizado em {new Date(dados.atualizadoEm).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit", second: "2-digit" })}</span>
          <span>CSS Brasil · Inteligência comercial</span>
        </footer>
        <audio ref={audio} src="/sounds/nova-venda.mp3" preload="auto" />
        {pilha.length > 0 && !ativa && (
          <TvDetalhes dados={dados} detalhe={pilha[pilha.length - 1]} mediaPorDia={mediaPorDia} projecao={projecao}
            aoAbrir={abrirDetalhe} aoVoltar={pilha.length > 1 ? voltarDetalhe : undefined} aoFechar={fecharDetalhe} aoRepetir={repetirAnimacao} />
        )}
        {ativa?.tipo === "venda" && <Comemoracao key={idDaFila(ativa)} venda={ativa.venda} posicao={ativa.posicao} vendasNoMes={ativa.vendasNoMes} aoFechar={fecharAtiva} />}
        {ativa && ativa.tipo !== "venda" && <ComemoracaoEvento key={idDaFila(ativa)} item={ativa} aoFechar={fecharAtiva} />}
      </main>
    </div>
  );
}
