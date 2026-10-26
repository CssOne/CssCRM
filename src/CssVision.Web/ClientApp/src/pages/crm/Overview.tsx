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
            <div
              className="h-full rounded-full bg-[var(--brand)] transition-all duration-700"
              style={{ width: `${Math.min(100, meta.percentualAtingido)}%` }}
            />
            {ritmo && <span aria-hidden className="absolute top-0 h-full w-0.5 bg-[var(--fg)] opacity-60" style={{ left: `${Math.min(100, ritmo.esperado)}%` }} />}
          </div>
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

      <div className="grid gap-3 lg:grid-cols-2">
        <Card {...ir("/app/crm/agenda")} className="!rounded-2xl p-4">
          <h2 className="mb-3 flex items-center gap-2 text-sm font-bold text-[var(--fg)]">
            <span className="flex size-7 items-center justify-center rounded-lg bg-[var(--surface-hover)] text-[var(--fg-muted)]"><CalendarClock className="size-3.5" /></span> Atividades de hoje
          </h2>
          {atividadesDoDia.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Nenhuma atividade prevista para hoje.</p>
          ) : (
            <ul className="space-y-2">
              {atividadesDoDia.map((a) => (
                <li key={a.id}>
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
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card {...ir("/app/crm/leads/kanban")} className="!rounded-2xl p-4">
          <h2 className="mb-3 flex items-center gap-2 text-sm font-bold text-[var(--fg)]">
            <span className="flex size-7 items-center justify-center rounded-lg bg-[var(--surface-hover)] text-[var(--fg-muted)]"><PhoneMissed className="size-3.5" /></span> Leads parados{leadsParadosTotal > 0 ? ` (${leadsParadosTotal.toLocaleString("pt-BR")})` : ""}
          </h2>
          <p className="mb-2 text-xs text-[var(--fg-muted)]">
            Leads do tráfego pago (não os do Notion) dos últimos 60 dias, em coluna aberta e com consultor ativo, sem contato nem movimento há mais de 5 dias.
          </p>
          {leadsParados.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Nenhum lead parado no momento.</p>
          ) : (
            <ul className="space-y-2">
              {leadsParados.map((l) => (
                <li key={l.leadId}>
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
                </li>
              ))}
            </ul>
          )}
        </Card>
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
    </div>
  );
}
