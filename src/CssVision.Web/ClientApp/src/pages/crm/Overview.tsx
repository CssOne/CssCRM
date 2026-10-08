import { AlertTriangle, CalendarClock, Handshake, PhoneMissed, Target, TrendingUp, UserPlus, Wallet } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarDataHora, formatarMoeda, formatarPercentual } from "../../lib/format";
import type { Dashboard } from "../../lib/types";
import { Badge, Card, ErrorState, Input, Pagination, Skeleton } from "../../components/ui";
import { usePaginacao } from "../../lib/usePaginacao";
import { BarrasHorizontaisChart } from "../../components/marketing/GraficosTrafego";
import { mesAtualIso, useAtualizarAoVivo } from "../../lib/useAoVivo";
import { StatCard } from "../../components/crm/StatCard";
import { EvolucaoChart, OrigemChart } from "../../components/crm/Charts";
import { FiltroRegionalDoPainel, useRegionalDoPainel } from "../../components/crm/FiltroRegionalDoPainel";

const TAMANHO_PAGINA_VENDEDORES = 10;

export function OverviewPage() {
  const [dados, setDados] = useState<Dashboard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
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

  const paginaVendedores = usePaginacao(dados?.desempenhoPorVendedor, TAMANHO_PAGINA_VENDEDORES);

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

  const { indicadores, meta, evolucaoVendas, origemLeads, desempenhoPorVendedor, atividadesDoDia, leadsParados } = dados;
  const funilLeads = dados.funilLeads ?? [];
  const resumoMensal = dados.resumoMensal ?? [];
  const leadsParadosTotal = dados.leadsParadosTotal ?? leadsParados.length;
  const [anoMes, numMes] = mes.split("-");
  const rotuloMes = `${numMes}/${anoMes}`;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Visão geral</h1>
        <p className="text-sm text-[var(--fg-muted)]">Indicadores comerciais de {rotuloMes}.</p>
      </div>
      <div className="flex flex-wrap gap-4">
        <div className="w-44">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Mês</label>
          <Input type="month" value={mes} onChange={(e) => e.target.value && setMes(e.target.value)} />
        </div>
        {podeFiltrar && <FiltroRegionalDoPainel regionais={regionais} valor={regionalId} aoEscolher={escolherRegional} />}
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatCard titulo="Novos leads" valor={(indicadores.novosLeadsTrafegoSemEtapa ?? 0).toLocaleString("pt-BR")} subtitulo={`do tráfego pago em Sem etapa — ${rotuloMes}`} icone={UserPlus} tom="brand" />
        <StatCard titulo="Leads sem contato" valor={indicadores.leadsSemContato.toLocaleString("pt-BR")} subtitulo="do mês, ainda em Sem etapa" icone={PhoneMissed} tom="warning" />
        <StatCard titulo="Atividades atrasadas" valor={String(indicadores.atividadesAtrasadas)} icone={AlertTriangle} tom="danger" />
        <StatCard titulo="Oportunidades abertas" valor={String(indicadores.oportunidadesAbertas)} icone={Handshake} tom="brand" />
        <StatCard titulo="Valor em pipeline" valor={formatarMoeda(indicadores.valorPipeline)} icone={Wallet} />
        <StatCard titulo="Taxa de conversão" valor={formatarPercentual(indicadores.taxaConversao)} subtitulo="vendas ÷ leads do mês" icone={TrendingUp} tom="success" />
        <StatCard titulo="Ticket médio" valor={formatarMoeda(indicadores.ticketMedio)} icone={Wallet} />
        <StatCard
          titulo="Vendas ganhas (mês)"
          valor={formatarMoeda(indicadores.vendasGanhasValor)}
          subtitulo={`${indicadores.vendasGanhasQuantidade} negócio(s)`}
          icone={Handshake}
          tom="success"
        />
      </div>

      <Card className="p-4">
        <div className="mb-2 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-[var(--fg)]">Meta comercial do mês</h2>
          <span className="text-sm text-[var(--fg-muted)]">
            {meta.realizadoQuantidade} de {meta.metaQuantidade} vendas
          </span>
        </div>
        <div className="h-2.5 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
          <div
            className="h-full rounded-full bg-[var(--brand)] transition-all"
            style={{ width: `${Math.min(100, meta.percentualAtingido)}%` }}
          />
        </div>
        <p className="mt-1 text-xs text-[var(--fg-muted)]">{formatarPercentual(meta.percentualAtingido)} atingido</p>
      </Card>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="p-4 lg:col-span-2">
          <h2 className="text-sm font-semibold text-[var(--fg)]">Funil por etapa — resumo do quadro de leads</h2>
          <p className="mb-2 text-xs text-[var(--fg-muted)]">Leads que chegaram em {rotuloMes}, na coluna em que estão hoje no quadro.</p>
          <BarrasHorizontaisChart
            categorias={funilLeads.map((e) => e.etapa)}
            series={[{ nome: "Leads", valores: funilLeads.map((e) => e.quantidade) }]}
            coresPorBarra={funilLeads.map((e, i) => e.cor ?? ["#2563eb", "#0ea5e9", "#8b5cf6", "#22c55e", "#f59e0b", "#f97316", "#ef4444", "#64748b"][i % 8])}
          />
        </Card>
        <Card className="p-4">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Origem dos leads</h2>
          <OrigemChart dados={origemLeads} />
        </Card>
      </div>

      <Card className="p-4">
        <h2 className="text-sm font-semibold text-[var(--fg)]">Evolução de vendas (6 meses)</h2>
        <p className="mb-3 text-xs text-[var(--fg-muted)]">Rendimento de cada mês: valor ganho (total das vendas) e adesão recebida, pela data da venda.</p>
        <EvolucaoChart dados={evolucaoVendas} />
      </Card>

      <Card className="overflow-x-auto p-4">
        <h2 className="text-sm font-semibold text-[var(--fg)]">Resumo por mês</h2>
        <p className="mb-3 text-xs text-[var(--fg-muted)]">Últimos 12 meses. Clique em um mês para ver o painel dele.</p>
        <table className="w-full min-w-[640px] text-sm">
          <thead>
            <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
              <th className="pb-2 font-medium">Mês</th>
              <th className="pb-2 text-right font-medium">Leads</th>
              <th className="pb-2 text-right font-medium">Perdidos</th>
              <th className="pb-2 text-right font-medium">Vendas</th>
              <th className="pb-2 text-right font-medium">Valor ganho</th>
              <th className="pb-2 text-right font-medium">Adesão</th>
              <th className="pb-2 text-right font-medium">Conversão</th>
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
                  <td className="py-2 text-right text-[var(--fg-muted)]">{formatarMoeda(m.valorGanho)}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{formatarMoeda(m.adesao)}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{formatarPercentual(m.conversao)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </Card>

      {desempenhoPorVendedor.length > 0 && (
        <Card className="overflow-x-auto p-4">
          <h2 className="text-sm font-semibold text-[var(--fg)]">Desempenho por vendedor</h2>
          <p className="mb-3 text-xs text-[var(--fg-muted)]">
            Ranking pela maior conversão. Leads que chegaram para o vendedor e vendas que ele fechou em {rotuloMes}. Conversão = vendas ÷ leads do mês.
          </p>
          <table className="w-full min-w-[560px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 pr-2 font-medium">#</th>
                <th className="pb-2 font-medium">Vendedor</th>
                <th className="pb-2 text-right font-medium">Leads</th>
                <th className="pb-2 text-right font-medium">Vendas</th>
                <th className="pb-2 text-right font-medium">Valor ganho</th>
                <th className="pb-2 text-right font-medium">Adesão</th>
                <th className="pb-2 text-right font-medium">Conversão</th>
              </tr>
            </thead>
            <tbody>
              {paginaVendedores.itensDaPagina.map((v, i) => (
                <tr key={v.vendedorId} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2 pr-2 text-[var(--fg-muted)]">{(paginaVendedores.pagina - 1) * TAMANHO_PAGINA_VENDEDORES + i + 1}</td>
                  <td className="py-2 text-[var(--fg)]">{v.vendedorNome}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{v.leadsAtribuidos.toLocaleString("pt-BR")}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{v.vendasGanhas}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{formatarMoeda(v.valorGanho)}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]">{formatarMoeda(v.valorAdesao)}</td>
                  <td className="py-2 text-right text-[var(--fg-muted)]" title={v.leadsAtribuidos === 0 ? "Sem leads novos no mês: as vendas são de leads anteriores" : undefined}>
                    {v.leadsAtribuidos === 0 ? "—" : formatarPercentual(v.taxaConversao)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination pagina={paginaVendedores.pagina} totalPaginas={paginaVendedores.totalPaginas} onChange={paginaVendedores.setPagina} />
        </Card>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="p-4">
          <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold text-[var(--fg)]">
            <CalendarClock className="size-4" /> Atividades de hoje
          </h2>
          {atividadesDoDia.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Nenhuma atividade prevista para hoje.</p>
          ) : (
            <ul className="space-y-2">
              {atividadesDoDia.map((a) => (
                <li key={a.id} className="flex items-center justify-between gap-2 rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-sm">
                  <div className="min-w-0">
                    <p className="truncate font-medium text-[var(--fg)]">{a.assunto}</p>
                    <p className="truncate text-xs text-[var(--fg-muted)]">{a.leadNome}</p>
                  </div>
                  <Badge variant={a.atrasada ? "danger" : "neutral"}>{formatarDataHora(a.dataHoraPrevista)}</Badge>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card className="p-4">
          <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold text-[var(--fg)]">
            <Target className="size-4" /> Leads parados{leadsParadosTotal > 0 ? ` (${leadsParadosTotal.toLocaleString("pt-BR")})` : ""}
          </h2>
          <p className="mb-2 text-xs text-[var(--fg-muted)]">
            Leads do tráfego pago (não os do Notion) dos últimos 60 dias, em coluna aberta e com consultor ativo, sem contato nem movimento há mais de 5 dias.
          </p>
          {leadsParados.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Nenhum lead parado no momento. 🎉</p>
          ) : (
            <ul className="space-y-2">
              {leadsParados.map((l) => (
                <li key={l.leadId}>
                  <Link
                    to={`/app/crm/leads/${l.leadId}`}
                    className="focus-ring flex items-center justify-between gap-2 rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-sm hover:opacity-80"
                  >
                    <div className="min-w-0">
                      <p className="truncate font-medium text-[var(--fg)]">{l.leadNome}</p>
                      <p className="truncate text-xs text-[var(--fg-muted)]">{l.responsavelNome ?? "Sem responsável"}</p>
                    </div>
                    <div className="flex shrink-0 flex-col items-end gap-1">
                      <Badge variant="info">{l.etapaNome ?? "Sem etapa"}</Badge>
                      <Badge variant="warning">{l.diasSemContato} dia(s)</Badge>
                    </div>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </div>
  );
}
