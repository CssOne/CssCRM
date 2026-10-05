import { ChevronLeft, ChevronRight, CircleDollarSign, CheckCircle2, Pause, Play, Radio, ShoppingBag, Target, TrendingUp, Wifi, WifiOff, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";
import { api, isAbortError } from "../../lib/api";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import type { TvComercial, TvConversao, TvRanking, TvRegional, TvVenda } from "../../lib/types";
import "./tv.css";

type ModoRanking = "vendas" | "adesao" | "conversao";

const MODOS: ModoRanking[] = ["vendas", "adesao", "conversao"];
const ROTULOS: Record<ModoRanking, { aba: string; titulo: string }> = {
  vendas: { aba: "Vendas", titulo: "Ranking por vendas" },
  adesao: { aba: "Adesão", titulo: "Ranking por adesão" },
  conversao: { aba: "Conversão", titulo: "Ranking de conversão" },
};
const MEDALHAS = ["🥇", "🥈", "🥉"];
const INTERVALO_MS = 15_000;
const moeda = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });
const moedaExata = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const diaBrasilia = new Intl.DateTimeFormat("en-CA", { timeZone: "America/Sao_Paulo", year: "numeric", month: "2-digit", day: "2-digit" });
const dataBr = new Intl.DateTimeFormat("pt-BR", { timeZone: "America/Sao_Paulo", day: "2-digit", month: "2-digit", year: "numeric" });
const horaBr = new Intl.DateTimeFormat("pt-BR", { timeZone: "America/Sao_Paulo", hour: "2-digit", minute: "2-digit" });

const iniciais = (nome: string) => nome.split(" ").filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join("");
function ha(valor: string, agora: Date) {
  const min = Math.max(0, Math.round((agora.getTime() - new Date(valor).getTime()) / 60000));
  return min < 1 ? "agora" : min < 60 ? `há ${min} min` : min < 1440 ? `há ${Math.floor(min / 60)}h` : `há ${Math.floor(min / 1440)}d`;
}

function Foto({ nome, url, grande = false }: { nome: string; url?: string | null; grande?: boolean }) {
  const [falhou, setFalhou] = useState<string>();
  const mostrar = Boolean(url && falhou !== url);
  return (
    <div className={`tv-avatar${grande ? " tv-avatar-grande" : ""}`} title={nome}>
      {mostrar ? <img src={url!} alt={nome} referrerPolicy="no-referrer" onError={() => setFalhou(url ?? undefined)} /> : <span aria-hidden>{iniciais(nome)}</span>}
    </div>
  );
}

function LinhaRanking({ item, modo, maxAdesao }: { item: TvRanking | TvConversao; modo: ModoRanking; maxAdesao: number }) {
  const v = item as TvRanking;
  const c = item as TvConversao;
  const progresso = modo === "vendas" ? v.percentualMeta ?? 0 : modo === "adesao" ? (maxAdesao ? (v.valorVendido * 100) / maxAdesao : 0) : c.taxaConversao;
  return (
    <div className={`tv-rank-row${item.posicao <= 3 ? ` top-${item.posicao}` : ""}`}>
      <div className="tv-rank-pos">{MEDALHAS[item.posicao - 1] ?? String(item.posicao).padStart(2, "0")}</div>
      <Foto nome={item.nome} url={item.fotoUrl} />
      <div className="tv-rank-pessoa">
        <strong>{item.nome}</strong>
        <span>{item.regional}</span>
      </div>
      {modo === "conversao" ? (
        <>
          <div className="tv-rank-num"><strong>{c.leadsAtendidos}</strong><span>leads</span></div>
          <div className="tv-rank-valor">
            <strong>{c.taxaConversao.toFixed(1)}%</strong>
            <div className="tv-progress"><i style={{ width: `${Math.min(progresso, 100)}%` }} /></div>
            <span>{c.vendasFechadas} fechados · {c.leadsPerdidos} perdidos</span>
          </div>
        </>
      ) : (
        <>
          <div className="tv-rank-num"><strong>{v.quantidadeVendas}</strong><span>{v.quantidadeVendas === 1 ? "venda" : "vendas"}</span></div>
          <div className="tv-rank-valor">
            <strong>{moeda.format(v.valorVendido)}</strong>
            <div className="tv-progress"><i style={{ width: `${Math.min(progresso, 100)}%` }} /></div>
            <span>{modo === "vendas" ? (v.percentualMeta == null ? "Meta não cadastrada" : `${v.percentualMeta.toFixed(0)}% da meta`) : "valor de adesão"}</span>
          </div>
        </>
      )}
    </div>
  );
}

function PainelRankings({ dados, periodo }: { dados: TvComercial; periodo: string }) {
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
        if (!troca) troca = window.setTimeout(proximo, 8_000);
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
    <div className="tv-panel tv-ranking">
      <div className="tv-panel-title">
        <div><span className="tv-eyebrow">DESEMPENHO INDIVIDUAL</span><h1>{ROTULOS[modo].titulo}</h1></div>
        <div className="tv-period-row">
          <span className="tv-period">{periodo}</span>
          <button type="button" className="tv-icon-btn" onClick={() => setPausado((p) => !p)} aria-label={pausado ? "Continuar rolagem automática" : "Parar rolagem automática"}>
            {pausado ? <Play /> : <Pause />}
          </button>
        </div>
      </div>
      <div className="tv-tabs" role="tablist">
        {MODOS.map((m) => (
          <button key={m} type="button" role="tab" aria-selected={m === modo} className={m === modo ? "active" : ""} onClick={() => setModo(m)}>{ROTULOS[m].aba}</button>
        ))}
      </div>
      <div className="tv-rank-list" ref={lista}>
        {itens.map((i) => <LinhaRanking key={i.consultorId} item={i} modo={modo} maxAdesao={maxAdesao} />)}
        {!itens.length && <div className="tv-empty">{modo === "conversao" ? "Nenhum lead recebido neste período" : "Nenhuma venda neste período"}</div>}
      </div>
    </div>
  );
}

function Metrica({ rotulo, valor, dica, icone: Icone }: { rotulo: string; valor: string; dica: string; icone: typeof ShoppingBag }) {
  return (
    <div className="tv-metric">
      <div className="tv-metric-icon"><Icone size={26} /></div>
      <div><span>{rotulo}</span><strong>{valor}</strong><small>{dica}</small></div>
    </div>
  );
}

function PainelRegionais({ itens }: { itens: TvRegional[] }) {
  return (
    <div className="tv-panel tv-regions">
      <div className="tv-panel-title tv-compact"><div><span className="tv-eyebrow">PERFORMANCE</span><h1>Ranking regional</h1></div></div>
      <div className="tv-region-list">
        {itens.slice(0, 4).map((r) => (
          <div className="tv-region-row" key={r.regionalId}>
            <b>{r.posicao}</b>
            <div>
              <strong>{r.nome}</strong>
              <span>{r.quantidadeMeta ? `${r.quantidadeVendas} / ${r.quantidadeMeta} vendas` : `${r.quantidadeVendas} vendas`} · {moeda.format(r.valorTotal)}</span>
              <i><em style={{ width: `${Math.min(r.percentualMeta ?? r.percentualParticipacao, 100)}%` }} /></i>
            </div>
            <strong>{r.percentualMeta == null ? `${r.percentualParticipacao.toFixed(0)}%` : `${r.percentualMeta.toFixed(1)}%`}</strong>
          </div>
        ))}
        {!itens.length && <div className="tv-empty">Sem regionais no período</div>}
      </div>
    </div>
  );
}

function PainelUltimas({ itens, agora }: { itens: TvVenda[]; agora: Date }) {
  return (
    <div className="tv-panel tv-latest">
      <div className="tv-panel-title tv-compact"><div><span className="tv-eyebrow">TEMPO REAL</span><h1>Últimas vendas</h1></div></div>
      <div className="tv-sales-list">
        {itens.slice(0, 5).map((v) => (
          <div className="tv-sale-row" key={v.vendaId}>
            <Foto nome={v.consultor} url={v.fotoUrl} />
            <div><strong>{v.consultor}</strong><span>{v.regional}{v.cliente ? ` · ${v.cliente}` : ""}</span></div>
            <div><strong>{moeda.format(v.valor)}</strong><span>{ha(v.atualizadaEm, agora)}</span></div>
          </div>
        ))}
        {!itens.length && <div className="tv-empty">Nenhuma venda neste período</div>}
      </div>
    </div>
  );
}

function Comemoracao({ venda, posicao, vendasNoMes, aoFechar }: { venda: TvVenda; posicao: number; vendasNoMes: number; aoFechar: () => void }) {
  useEffect(() => { const id = window.setTimeout(aoFechar, 30_000); return () => window.clearTimeout(id); }, [aoFechar]);
  return (
    <div className="tv-celebration" role="status">
      <div className="tv-confetti" aria-hidden>
        {Array.from({ length: 40 }, (_, i) => <i key={i} style={{ left: `${(i * 31 + 2) % 100}%`, animationDelay: `-${(i * 0.41) % 5}s` }} />)}
      </div>
      <div className="tv-celebration-card">
        <button type="button" className="tv-celebration-close" onClick={aoFechar} aria-label="Fechar animação"><X /></button>
        <div className="tv-celebration-symbol" aria-hidden><CheckCircle2 /></div>
        <span className="tv-eyebrow">NOVA VENDA REALIZADA</span>
        <div className="tv-celebration-person">
          <Foto nome={venda.consultor} url={venda.fotoUrl} grande />
          <div><span>PARABÉNS!</span><h2>{venda.consultor}</h2><small>Regional {venda.regional}</small></div>
        </div>
        <div className="tv-celebration-value"><span>VALOR DA ADESÃO</span><strong>{moedaExata.format(venda.valor)}</strong></div>
        <div className="tv-celebration-datetime">
          <div><span>DATA DA VENDA</span><strong>{dataBr.format(new Date(venda.dataVenda))}</strong></div>
          <div><span>REGISTRADA ÀS</span><strong>{horaBr.format(new Date(venda.atualizadaEm))}</strong></div>
        </div>
        <div className="tv-celebration-stats">
          <span><b>{vendasNoMes}</b> vendas no mês</span>
          {posicao > 0 && <span><b>{posicao}ª</b> posição no ranking</span>}
        </div>
        <div className="tv-celebration-message">✓ MAIS UMA CONQUISTA PARA CELEBRAR!</div>
      </div>
    </div>
  );
}

/**
 * Painel comercial da TV (/tv/comercial): abre em outra aba do navegador, em tela cheia, e lê direto do CRM —
 * vendas, adesão, conversão, regionais e evolução do mês, com atualização ao vivo e comemoração de nova venda.
 */
export function TvComercialPage() {
  const [dados, setDados] = useState<TvComercial | null>(null);
  const [erro, setErro] = useState(false);
  const [periodo, setPeriodo] = useState<{ mes: number; ano: number } | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [agora, setAgora] = useState(new Date());
  const [fila, setFila] = useState<Array<{ venda: TvVenda; posicao: number; vendasNoMes: number }>>([]);
  const vistas = useRef<Set<string> | null>(null);
  const audio = useRef<HTMLAudioElement>(null);
  const atual = periodo === null;

  useAtualizarAoVivo(useCallback(() => setRecarregar((n) => n + 1), []));

  useEffect(() => {
    const id = window.setInterval(() => setRecarregar((n) => n + 1), INTERVALO_MS);
    return () => window.clearInterval(id);
  }, []);
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
        if (vistas.current === null) { vistas.current = new Set(novo.ultimasVendas.map((v) => v.vendaId)); return; }
        const novas = novo.ultimasVendas.filter((v) => !vistas.current!.has(v.vendaId));
        novo.ultimasVendas.forEach((v) => vistas.current!.add(v.vendaId));
        const celebrar = novas.filter((v) => v.valor > 0 && diaBrasilia.format(new Date(v.dataVenda)) === hoje).slice(0, 3);
        if (celebrar.length) {
          setFila((f) => [...f, ...celebrar.map((venda) => {
            const rank = novo.rankingConsultores.find((r) => r.nome === venda.consultor);
            return { venda, posicao: rank?.posicao ?? 0, vendasNoMes: rank?.quantidadeVendas ?? 0 };
          })]);
        }
      })
      .catch((e) => { if (!isAbortError(e)) setErro(true); });
    return () => controller.abort();
  }, [recarregar, periodo, atual]);

  const ativa = fila[0];
  useEffect(() => {
    if (!ativa || !audio.current) return;
    audio.current.currentTime = 0;
    void audio.current.play().catch(() => undefined);
  }, [ativa]);
  const fecharAtiva = useCallback(() => setFila((f) => f.slice(1)), []);

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

  const grafico = useMemo(() => dados?.evolucaoMensal.map((d) => ({ dia: Number(d.data.slice(8, 10)), diaQtd: d.quantidadeVendasDia, acumulado: d.quantidadeAcumulada })) ?? [], [dados]);
  const opcoes: ApexOptions = useMemo(() => ({
    chart: { type: "line", toolbar: { show: false }, background: "transparent", foreColor: "#9fb0c8", animations: { enabled: false } },
    theme: { mode: "dark" },
    grid: { borderColor: "rgba(255,255,255,.08)", strokeDashArray: 4 },
    stroke: { width: [0, 4], curve: "smooth" },
    plotOptions: { bar: { borderRadius: 4, columnWidth: "55%" } },
    colors: ["#3b82f6", "#22c55e"],
    dataLabels: { enabled: false },
    legend: { show: false },
    xaxis: { categories: grafico.map((g) => g.dia), labels: { style: { fontSize: "13px" } } },
    yaxis: [
      { labels: { formatter: (v) => String(Math.round(v)), style: { fontSize: "12px" } } },
      { opposite: true, labels: { formatter: (v) => String(Math.round(v)), style: { fontSize: "12px" } } },
    ],
    tooltip: { theme: "dark", shared: true, y: { formatter: (v) => `${Math.round(v)} vendas` } },
  }), [grafico]);

  if (!dados) {
    return <main className="tv-dashboard tv-loading">{erro ? "Não foi possível carregar o painel. Tentando novamente…" : "Carregando painel…"}</main>;
  }

  const diaHoje = agora.getDate();
  const diasNoMes = new Date(dados.periodo.ano, dados.periodo.mes, 0).getDate();
  const decorridos = atual ? diaHoje : diasNoMes;
  const mediaPorDia = decorridos ? dados.resumo.vendasNoMes / decorridos : 0;
  const projecao = Math.round(mediaPorDia * diasNoMes);
  const periodoRotulo = new Date(dados.periodo.ano, dados.periodo.mes - 1).toLocaleDateString("pt-BR", { month: "long", year: "numeric" });
  const ticketHoje = dados.resumo.vendasHoje ? dados.resumo.valorHoje / dados.resumo.vendasHoje : 0;
  const ticketMes = dados.resumo.vendasNoMes ? dados.resumo.valorNoMes / dados.resumo.vendasNoMes : 0;

  return (
    <main className="tv-dashboard">
      <button type="button" className="tv-month-nav tv-prev" onClick={() => mudarMes(-1)} aria-label="Mês anterior"><ChevronLeft /></button>
      <button type="button" className="tv-month-nav tv-next" onClick={() => mudarMes(1)} disabled={atual} aria-label="Próximo mês"><ChevronRight /></button>
      <header>
        <div className="tv-brand">
          <img src="/logo-css-white.png" alt="CSS Brasil" />
          <div><strong>CSS BRASIL</strong><span>PAINEL COMERCIAL</span></div>
        </div>
        <div className="tv-live"><Radio size={18} /><span>AO VIVO</span></div>
        <div className="tv-header-right">
          <div className={`tv-status ${erro ? "offline" : "online"}`}>{erro ? <WifiOff size={20} /> : <Wifi size={20} />}{erro ? "Reconectando" : "Conectado"}</div>
          <div className="tv-clock">
            <strong>{agora.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}</strong>
            <span>{agora.toLocaleDateString("pt-BR", { weekday: "long", day: "2-digit", month: "long" })}</span>
          </div>
        </div>
      </header>
      {erro && <div className="tv-stale">Exibindo os últimos dados válidos · atualização automática em andamento</div>}
      <section className="tv-content">
        <div className="tv-left"><PainelRankings dados={dados} periodo={periodoRotulo} /></div>
        <div className="tv-right">
          <div className="tv-metrics">
            <Metrica rotulo="VENDAS HOJE" valor={String(dados.resumo.vendasHoje)} dica={`ticket médio ${moeda.format(ticketHoje)}`} icone={ShoppingBag} />
            <Metrica rotulo="VENDAS NO MÊS" valor={String(dados.resumo.vendasNoMes)} dica="volume acumulado" icone={TrendingUp} />
            <Metrica rotulo="ADESÃO HOJE" valor={moeda.format(dados.resumo.valorHoje)} dica="faturamento do dia" icone={CircleDollarSign} />
            <Metrica rotulo="ADESÃO NO MÊS" valor={moeda.format(dados.resumo.valorNoMes)} dica={`ticket médio ${moeda.format(ticketMes)}`} icone={CircleDollarSign} />
          </div>
          <div className="tv-panel tv-evolution">
            <div className="tv-panel-title">
              <div><span className="tv-eyebrow">RITMO DO MÊS</span><h1>Evolução das vendas</h1></div>
              <div className="tv-goal"><Target size={18} /><b>{dados.resumo.percentualMetaGeral == null ? "—" : `${dados.resumo.percentualMetaGeral.toFixed(1)}%`}</b><span>da meta geral</span></div>
            </div>
            <div className="tv-chart-summary">
              <span><small>HOJE</small><b>{dados.resumo.vendasHoje}</b></span>
              <span><small>MÉDIA / DIA</small><b>{mediaPorDia.toFixed(1)}</b></span>
              <span><small>PROJEÇÃO DO MÊS</small><b>{projecao}</b></span>
              <div className="tv-legend"><span><i className="daily" />Vendas no dia</span><span><i className="total" />Total acumulado</span></div>
            </div>
            <div className="tv-chart">
              <Chart
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
          <div className="tv-bottom">
            <PainelRegionais itens={dados.rankingRegionais} />
            <PainelUltimas itens={dados.ultimasVendas} agora={agora} />
          </div>
        </div>
      </section>
      <footer>
        <span>Atualizado em {new Date(dados.atualizadoEm).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit", second: "2-digit" })}</span>
        <span>CSS Brasil · Inteligência comercial</span>
      </footer>
      <audio ref={audio} src="/sounds/nova-venda.mp3" preload="auto" />
      {ativa && <Comemoracao key={ativa.venda.vendaId} venda={ativa.venda} posicao={ativa.posicao} vendasNoMes={ativa.vendasNoMes} aoFechar={fecharAtiva} />}
    </main>
  );
}
