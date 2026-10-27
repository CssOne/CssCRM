import { AlertTriangle, CalendarClock, Handshake, PhoneMissed, Target, TrendingUp, UserPlus, Wallet } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { useAuth } from "../../context/AuthContext";
import { formatarDataHora, formatarMoeda, formatarPercentual } from "../../lib/format";
import type { Dashboard } from "../../lib/types";
import { Badge, Card, ErrorState, Skeleton } from "../../components/ui";
import { BarrasHorizontaisChart } from "../../components/marketing/GraficosTrafego";
import { mesAtualIso, useAtualizarAoVivo } from "../../lib/useAoVivo";
import { StatCard } from "../../components/crm/StatCard";
import { PagamentosEmAberto } from "../../components/crm/PagamentosEmAberto";
import { EvolucaoChart, OrigemChart } from "../../components/crm/Charts";
import { FiltroRegionalDoPainel, useRegionalDoPainel } from "../../components/crm/FiltroRegionalDoPainel";
import { DetalheDoCardModal } from "../../components/crm/DetalheDoCardModal";
import { MesInput } from "../../components/PeriodoInput";
import { CartaoRetratil } from "../../components/crm/CartaoRetratil";

// Cor de cada tipo de venda na barra da meta; tipos fora da lista pegam as de reserva, sempre a mesma para o mesmo nome.
const CORES_TIPO: Record<string, string> = {
  "lead": "#3f63c7",
  "indicação": "#16a34a",
  "indicação lead": "#0d9488",
  "indicação pessoal": "#f59e0b",
  "pessoal": "#f59e0b",
  "parceria": "#8b5cf6",
  "ação externa": "#06b6d4",
  "css": "#ec4899",
  "contemplando sonhos": "#f97316",
  "sem tipo": "#94a3b8",
};
const CORES_RESERVA = ["#84cc16", "#a855f7", "#14b8a6", "#eab308", "#6366f1"];
function corDoTipo(tipo: string) {
  const chave = tipo.trim().toLowerCase();
  if (CORES_TIPO[chave]) return CORES_TIPO[chave];
  let soma = 0;
  for (const c of chave) soma += c.charCodeAt(0);
  return CORES_RESERVA[soma % CORES_RESERVA.length];
}

export function OverviewPage() {
  const { temPapel } = useAuth();
  const navegar = useNavigate();
  // Card inteiro clicável: leva para a tela do assunto. Links, botões, linhas da tabela e as fatias do gráfico de origem mantêm o clique deles.
  const ir = (destino: string): React.HTMLAttributes<HTMLDivElement> => ({
    role: "link",
    tabIndex: 0,
    onClick: (e) => {
      if ((e.target as HTMLElement).closest("a, button, input, select, tr, .apexcharts-pie-area")) return;
      navegar(destino);
    },
    onKeyDown: (e) => {
      if (e.target === e.currentTarget && (e.key === "Enter" || e.key === " ")) {
        e.preventDefault();
        navegar(destino);
      }
    },
  });
  // Consultor comercial: no "Resumo por mês" o valor ganho é a adesão, que é o que consta para ele em tempo real.
  const ehConsultor = temPapel("Comercial") && !temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial");
  const [dados, setDados] = useState<Dashboard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  // Fatia do gráfico de origem das vendas aberta: as vendas daquela origem.
  const [origemAberta, setOrigemAberta] = useState<string | null>(null);
  // Barra da meta: tipo de venda aberto ("" = todas as vendas do mês, nulo = fechado).
  const [tipoAberto, setTipoAberto] = useState<string | null>(null);
  // Mês mostrado (YYYY-MM): começa no mês atual e acompanha a virada do mês se ninguém escolheu outro.
  const [mes, setMes] = useState(() => mesAtualIso().slice(0, 7));
  const mesAtualRef = useRef(mesAtualIso().slice(0, 7));
  const { podeFiltrar, regionais, regionalId, escolher: escolherRegional, pronto } = useRegionalDoPainel();

  useEffect(() => {
    if (!pronto) return; // espera saber qual regional abrir, para carregar uma vez só
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    const [ano, numeroMes] = mes.split("-").map(Number);
    const ultimoDia = new Date(ano, numeroMes, 0).getDate();
    api
      .get<Dashboard>(`/crm/dashboard${toQueryString({ dataInicio: `${mes}-01`, dataFim: `${mes}-${String(ultimoDia).padStart(2, "0")}`, regionalId: regionalId || undefined })}`, controller.signal)
      .then(setDados)
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o painel."); })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, [recarregar, mes, regionalId, pronto]);

  // Tempo real: o painel recarrega (sem piscar) quando uma venda/lead muda e quando o mês vira.
  useAtualizarAoVivo(() => {
    const atual = mesAtualIso().slice(0, 7);
    setMes((escolhido) => (escolhido === mesAtualRef.current ? atual : escolhido));
    mesAtualRef.current = atual;
    setRecarregar((n) => n + 1);
  });

  if (carregando && !dados) {
    return (
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {Array.from({ length: 8 }).map((_, i) => (
          <Skeleton key={i} className="h-24" />
        ))}
      </div>
    );
  }

  if (!dados) {
    return <ErrorState message={erro ?? "Não foi possível carregar o painel."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  const { indicadores, meta, evolucaoVendas, origemVendas, atividadesDoDia, leadsParados } = dados;
  const funilLeads = dados.funilLeads ?? [];
  const resumoMensal = dados.resumoMensal ?? [];
  const leadsParadosTotal = dados.leadsParadosTotal ?? leadsParados.length;
  const tiposDaMeta = meta.porTipo ?? [];
  const [anoMes, numMes] = mes.split("-");
  const rotuloMes = `${numMes}/${anoMes}`;
  // Só no mês corrente: onde a meta deveria estar hoje, proporcional aos dias passados.
  const ritmo = (() => {
    if (mes !== mesAtualIso().slice(0, 7)) return null;
    const diasNoMes = new Date(Number(anoMes), Number(numMes), 0).getDate();
    const esperado = (new Date().getDate() / diasNoMes) * 100;
    return { esperado, noRitmo: meta.percentualAtingido >= esperado };
  })();

  return (
    <div className="visao-3d space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold tracking-tight text-[var(--fg)]">Visão geral</h1>
          <p className="text-sm text-[var(--fg-muted)]">Indicadores comerciais de {rotuloMes}.</p>
        </div>
        <div className="ui-surface flex flex-wrap items-end gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-2.5 shadow-[var(--shadow-card)]">
          <div className="w-44">
            <label className="mb-1 block px-1 text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Mês</label>
            <MesInput value={mes} onChange={(v) => v && setMes(v)} />
          </div>
          {podeFiltrar && <FiltroRegionalDoPainel regionais={regionais} valor={regionalId} aoEscolher={escolherRegional} />}
        </div>
      </div>
      <PagamentosEmAberto />

      <div className="grid gap-3 lg:grid-cols-3">
        <Card {...ir("/app/crm/goals")} className="!rounded-2xl p-4 lg:col-span-2">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2.5">
              <span className="flex size-9 items-center justify-center rounded-xl bg-[var(--surface-hover)] text-[var(--fg-muted)]">
                <Target className="size-5" aria-hidden />
              </span>
              <div>
                <h2 className="text-sm font-bold text-[var(--fg)]">Meta comercial do mês</h2>
                <p className="text-xs text-[var(--fg-muted)]">
                  {meta.realizadoQuantidade} de {meta.metaQuantidade} vendas
                </p>
              </div>
            </div>
            <div className="flex items-center gap-3">
              {ritmo && (
                <span className="inline-flex items-center gap-1.5 rounded-full border border-[var(--border)] px-2.5 py-1 text-xs font-medium text-[var(--fg-muted)]">
                  <span aria-hidden className={`size-2 rounded-full ${ritmo.noRitmo ? "bg-[var(--success)]" : "bg-[var(--warning)]"}`} />
                  {ritmo.noRitmo ? "No ritmo" : "Abaixo do ritmo"} · esperado {Math.round(ritmo.esperado)}%
                </span>
              )}
              <span className="text-4xl font-bold tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarPercentual(meta.percentualAtingido)}</span>
            </div>
          </div>
          <div className="relative h-3 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
            {tiposDaMeta.length > 0 && meta.metaQuantidade > 0 ? (
              // Cada tipo de venda numa cor, do tamanho das vendas dele; clicar abre as vendas (a soma dos trechos é o realizado).
              <div className="flex h-full" style={{ width: `${Math.min(100, meta.percentualAtingido)}%` }}>
                {tiposDaMeta.map((t) => (
                  <button
                    key={t.tipo}
                    type="button"
                    onClick={() => setTipoAberto(t.tipo)}
                    title={`${t.tipo}: ${t.quantidade} venda(s) — clique para ver`}
                    aria-label={`${t.tipo}: ${t.quantidade} venda(s)`}
                    className="focus-ring h-full min-w-0 cursor-pointer border-r border-[var(--surface)] transition-[filter,width] duration-700 last:border-r-0 hover:brightness-110"
                    style={{ width: `${(100 * t.quantidade) / meta.realizadoQuantidade}%`, backgroundColor: corDoTipo(t.tipo) }}
                  />
                ))}
              </div>
            ) : (
              <div
                className="h-full rounded-full bg-[var(--brand)] transition-all duration-700"
                style={{ width: `${Math.min(100, meta.percentualAtingido)}%` }}
              />
            )}
            {ritmo && <span aria-hidden className="pointer-events-none absolute top-0 h-full w-0.5 bg-[var(--fg)] opacity-60" style={{ left: `${Math.min(100, ritmo.esperado)}%` }} />}
          </div>
          {tiposDaMeta.length > 0 && (
            <ul className="mt-2 flex flex-wrap gap-1.5" aria-label="Vendas do mês por tipo">
              {tiposDaMeta.map((t) => (
                <li key={t.tipo}>
                  <button
                    type="button"
                    onClick={() => setTipoAberto(t.tipo)}
                    className="focus-ring inline-flex cursor-pointer items-center gap-1.5 rounded-full border border-[var(--border)] px-2.5 py-0.5 text-xs text-[var(--fg-muted)] transition hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
                  >
                    <span aria-hidden className="size-2 rounded-full" style={{ backgroundColor: corDoTipo(t.tipo) }} />
                    {t.tipo} · <strong className="text-[var(--fg)]">{t.quantidade}</strong>
                  </button>
                </li>
              ))}
              <li>
                <button type="button" onClick={() => setTipoAberto("")} className="focus-ring cursor-pointer px-1.5 py-0.5 text-xs font-medium text-[var(--brand)] hover:underline">
                  Ver todas
                </button>
              </li>
            </ul>
          )}
          <p className="mt-2 text-xs text-[var(--fg-muted)]">
            Vendas ganhas: <strong className="text-[var(--fg)]">{formatarMoeda(indicadores.vendasGanhasValor)}</strong> em {indicadores.vendasGanhasQuantidade} negócio(s)
            {ritmo ? " · a marca vertical indica onde a meta deveria estar hoje." : "."}
          </p>
        </Card>

        <Card {...ir("/app/crm/leads/kanban")} className="!rounded-2xl p-4">
          <h2 className="mb-3 flex items-center gap-2 text-sm font-bold text-[var(--fg)]">
            <span className="flex size-7 items-center justify-center rounded-lg bg-[var(--surface-hover)] text-[var(--fg-muted)]"><AlertTriangle className="size-3.5" /></span> Precisa de você
          </h2>
          <ul className="space-y-2 text-sm">
            <li className="flex items-center justify-between rounded-xl bg-[var(--surface-hover)] px-3 py-2">
              <span className="text-[var(--fg-muted)]">Atividades atrasadas</span>
              <span className={`font-bold [font-variant-numeric:tabular-nums] ${indicadores.atividadesAtrasadas > 0 ? "text-[var(--danger)]" : "text-[var(--fg)]"}`}>{indicadores.atividadesAtrasadas}</span>
            </li>
            <li className="flex items-center justify-between rounded-xl bg-[var(--surface-hover)] px-3 py-2">
              <span className="text-[var(--fg-muted)]">Leads sem contato</span>
              <span className={`font-bold [font-variant-numeric:tabular-nums] ${indicadores.leadsSemContato > 0 ? "text-[var(--warning)]" : "text-[var(--fg)]"}`}>{indicadores.leadsSemContato.toLocaleString("pt-BR")}</span>
            </li>
            <li className="flex items-center justify-between rounded-xl bg-[var(--surface-hover)] px-3 py-2">
              <span className="text-[var(--fg-muted)]">Leads parados</span>
              <span className={`font-bold [font-variant-numeric:tabular-nums] ${leadsParadosTotal > 0 ? "text-[var(--warning)]" : "text-[var(--fg)]"}`}>{leadsParadosTotal.toLocaleString("pt-BR")}</span>
            </li>
          </ul>
        </Card>
      </div>

      <div className="grid items-start gap-3 lg:grid-cols-2">
        <CartaoRetratil
          chave="atividades-de-hoje"
          icone={CalendarClock}
          titulo="Atividades de hoje"
          itens={atividadesDoDia}
          vazio="Nenhuma atividade prevista para hoje."
          chaveDoItem={(a) => a.id}
          cardProps={ir("/app/crm/agenda")}
          renderItem={(a) => (
            <Link
              to={`/app/crm/leads/${a.leadId}`}
              className="focus-ring flex items-center justify-between gap-2 rounded-xl bg-[var(--surface-hover)] px-3 py-2 text-sm transition hover:bg-[var(--brand-soft)]"
            >
              <div className="min-w-0">
                <p className="truncate font-medium text-[var(--fg)]">{a.assunto}</p>
                <p className="truncate text-xs text-[var(--fg-muted)]">{a.leadNome}</p>
              </div>
              <Badge variant={a.atrasada ? "danger" : "neutral"}>{formatarDataHora(a.dataHoraPrevista)}</Badge>
            </Link>
          )}
        />

        <CartaoRetratil
          chave="leads-parados"
          icone={PhoneMissed}
          titulo="Leads parados"
          total={leadsParadosTotal}
          descricao="Leads do tráfego pago (não os do Notion) dos últimos 60 dias, em coluna aberta e com consultor ativo, sem contato nem movimento há mais de 5 dias."
          itens={leadsParados}
          vazio="Nenhum lead parado no momento."
          chaveDoItem={(l) => l.leadId}
          cardProps={ir("/app/crm/leads/kanban")}
          renderItem={(l) => (
            <Link
              to={`/app/crm/leads/${l.leadId}`}
              className="focus-ring flex items-center justify-between gap-2 rounded-xl bg-[var(--surface-hover)] px-3 py-2 text-sm transition hover:bg-[var(--brand-soft)]"
            >
              <div className="min-w-0">
                <p className="truncate font-medium text-[var(--fg)]">{l.leadNome}</p>
                <p className="truncate text-xs text-[var(--fg-muted)]">{l.responsavelNome ?? "Sem responsável"}</p>
              </div>
              <div className="flex shrink-0 flex-col items-end gap-1">
                <Badge variant="neutral">{l.etapaNome ?? "Sem etapa"}</Badge>
                <span className="text-xs font-medium text-[var(--warning)]">{l.diasSemContato} dia(s)</span>
              </div>
            </Link>
          )}
        />
      </div>

      <div className="grid grid-cols-2 gap-2.5 md:grid-cols-3 lg:grid-cols-6">
        <StatCard titulo="Novos leads" valor={(indicadores.novosLeadsTrafegoSemEtapa ?? 0).toLocaleString("pt-BR")} subtitulo="tráfego pago em Sem etapa" icone={UserPlus} clicavel={ir("/app/crm/leads/kanban")} />
        <StatCard titulo="Oportunidades abertas" valor={String(indicadores.oportunidadesAbertas)} icone={Handshake} clicavel={ir("/app/crm/pipeline")} />
        <StatCard titulo="Valor em pipeline" valor={formatarMoeda(indicadores.valorPipeline)} icone={Wallet} clicavel={ir("/app/crm/pipeline")} />
        <StatCard titulo="Taxa de conversão" valor={formatarPercentual(indicadores.taxaConversao)} subtitulo="vendas ÷ leads do mês" icone={TrendingUp} clicavel={ir("/app/crm/relatorio-comercial")} />
        <StatCard titulo="Ticket médio" valor={formatarMoeda(indicadores.ticketMedio)} icone={Wallet} clicavel={ir("/app/crm/relatorio-comercial")} />
        <StatCard titulo="Vendas ganhas" valor={String(indicadores.vendasGanhasQuantidade)} subtitulo={formatarMoeda(indicadores.vendasGanhasValor)} icone={Handshake} clicavel={ir("/app/crm/relatorio-comercial")} />
      </div>

      <div className="grid gap-3 lg:grid-cols-3">
        <Card {...ir("/app/crm/leads/kanban")} className="!rounded-2xl p-4 lg:col-span-2">
          <h2 className="text-sm font-bold text-[var(--fg)]">Funil por etapa — resumo do quadro de leads</h2>
          <p className="mb-2 text-xs text-[var(--fg-muted)]">Leads que chegaram em {rotuloMes}, na coluna em que estão hoje no quadro.</p>
          <BarrasHorizontaisChart
            categorias={funilLeads.map((e) => e.etapa)}
            series={[{ nome: "Leads", valores: funilLeads.map((e) => e.quantidade) }]}
            coresPorBarra={funilLeads.map((e, i) => e.cor ?? ["#2563eb", "#0ea5e9", "#8b5cf6", "#22c55e", "#f59e0b", "#f97316", "#ef4444", "#64748b"][i % 8])}
          />
        </Card>
        <Card {...ir("/app/crm/relatorio-comercial")} className="!rounded-2xl p-4">
          <h2 className="mb-3 text-sm font-bold text-[var(--fg)]">Origem das vendas</h2>
          <OrigemChart dados={origemVendas} onSelecionar={setOrigemAberta} />
        </Card>
      </div>

      <Card {...ir("/app/crm/relatorio-comercial")} className="!rounded-2xl p-4">
        <h2 className="text-sm font-bold text-[var(--fg)]">Evolução de vendas (6 meses)</h2>
        <p className="mb-3 text-xs text-[var(--fg-muted)]">Rendimento de cada mês: valor ganho (total das vendas) e adesão recebida, pela data da venda.</p>
        <EvolucaoChart dados={evolucaoVendas} />
      </Card>

      <Card {...ir("/app/crm/relatorio-comercial")} className="overflow-x-auto !rounded-2xl p-4">
        <h2 className="text-sm font-bold text-[var(--fg)]">Resumo por mês</h2>
        <p className="mb-3 text-xs text-[var(--fg-muted)]">Últimos 12 meses. Clique em um mês para ver o painel dele.</p>
        <table className="w-full min-w-[640px] text-sm">
          <thead>
            <tr className="border-b border-[var(--border)] text-left text-[10px] uppercase tracking-wide text-[var(--fg-muted)]">
              <th className="pb-2 font-semibold">Mês</th>
              <th className="pb-2 text-right font-semibold">Leads</th>
              <th className="pb-2 text-right font-semibold">Perdidos</th>
              <th className="pb-2 text-right font-semibold">Vendas</th>
              <th className="pb-2 text-right font-semibold">Valor ganho</th>
              {!ehConsultor && <th className="pb-2 text-right font-semibold">Adesão</th>}
              <th className="pb-2 text-right font-semibold">Conversão</th>
            </tr>
          </thead>
          <tbody>
            {[...resumoMensal].reverse().map((m) => {
              const [mm, aaaa] = m.mes.split("/");
              const selecionado = `${aaaa}-${mm}` === mes;
              return (
                <tr
                  key={m.mes}
                  onClick={() => setMes(`${aaaa}-${mm}`)}
                  className={`cursor-pointer border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-hover)] ${selecionado ? "bg-[var(--brand-soft)]" : ""}`}
                >
                  <td className="py-2 font-medium text-[var(--fg)]">{m.mes}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{m.leads.toLocaleString("pt-BR")}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{m.perdidos.toLocaleString("pt-BR")}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{m.vendas.toLocaleString("pt-BR")}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{formatarMoeda(ehConsultor ? m.adesao : m.valorGanho)}</td>
                  {!ehConsultor && <td className="py-2 text-right text-[var(--fg-muted)]">{formatarMoeda(m.adesao)}</td>}
                  <td className="py-2 text-right text-[var(--fg-muted)]">{formatarPercentual(m.conversao)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </Card>

      <DetalheDoCardModal
        aberto={origemAberta !== null}
        titulo={origemAberta ? `Origem das vendas — ${origemAberta}` : ""}
        abas={
          origemAberta
            ? [
                {
                  rotulo: origemAberta,
                  url: `/crm/dashboard/origem-vendas${toQueryString({ origem: origemAberta, dataInicio: `${mes}-01`, dataFim: `${mes}-${String(new Date(Number(mes.split("-")[0]), Number(mes.split("-")[1]), 0).getDate()).padStart(2, "0")}`, regionalId: regionalId || undefined })}`,
                },
              ]
            : []
        }
        onFechar={() => setOrigemAberta(null)}
      />

      {/* Barra da meta: as vendas do mês, uma aba por tipo (a clicada abre primeiro) e "Todas". */}
      <DetalheDoCardModal
        aberto={tipoAberto !== null}
        titulo="Vendas do mês"
        abas={
          tipoAberto === null
            ? []
            : [tipoAberto, ...tiposDaMeta.map((t) => t.tipo).filter((t) => t !== tipoAberto), ...(tipoAberto === "" ? [] : [""])]
                .filter((t, i, todos) => todos.indexOf(t) === i)
                .map((t) => ({
                  rotulo: t === "" ? "Todas" : t,
                  url: `/crm/dashboard/vendas-do-mes${toQueryString({ tipo: t || undefined, regionalId: regionalId || undefined })}`,
                }))
        }
        onFechar={() => setTipoAberto(null)}
      />
    </div>
  );
}
