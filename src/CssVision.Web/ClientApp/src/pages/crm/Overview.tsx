import { AlertTriangle, CalendarClock, Handshake, PhoneMissed, Target, TrendingUp, UserPlus, Wallet } from "lucide-react";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarDataHora, formatarMoeda, formatarPercentual } from "../../lib/format";
import type { Dashboard } from "../../lib/types";
import { Badge, Card, ErrorState, Skeleton } from "../../components/ui";
import { StatCard } from "../../components/crm/StatCard";
import { EvolucaoChart, FunilChart, OrigemChart } from "../../components/crm/Charts";

export function OverviewPage() {
  const [dados, setDados] = useState<Dashboard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    api
      .get<Dashboard>(`/crm/dashboard${toQueryString({})}`, controller.signal)
      .then(setDados)
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o painel."); })
      .finally(() => setCarregando(false));
    return () => controller.abort();
  }, [recarregar]);

  if (carregando) {
    return (
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {Array.from({ length: 8 }).map((_, i) => (
          <Skeleton key={i} className="h-24" />
        ))}
      </div>
    );
  }

  if (erro || !dados) {
    return <ErrorState message={erro ?? "Não foi possível carregar o painel."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  const { indicadores, meta, funil, evolucaoVendas, origemLeads, desempenhoPorVendedor, atividadesDoDia, leadsParados } = dados;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Visão geral</h1>
        <p className="text-sm text-[var(--fg-muted)]">Indicadores comerciais do período atual.</p>
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatCard titulo="Novos leads" valor={String(indicadores.novosLeads)} icone={UserPlus} tom="brand" />
        <StatCard titulo="Leads sem contato" valor={String(indicadores.leadsSemContato)} icone={PhoneMissed} tom="warning" />
        <StatCard titulo="Atividades atrasadas" valor={String(indicadores.atividadesAtrasadas)} icone={AlertTriangle} tom="danger" />
        <StatCard titulo="Oportunidades abertas" valor={String(indicadores.oportunidadesAbertas)} icone={Handshake} tom="brand" />
        <StatCard titulo="Valor em pipeline" valor={formatarMoeda(indicadores.valorPipeline)} icone={Wallet} />
        <StatCard titulo="Taxa de conversão" valor={formatarPercentual(indicadores.taxaConversao)} icone={TrendingUp} tom="success" />
        <StatCard titulo="Ticket médio" valor={formatarMoeda(indicadores.ticketMedio)} icone={Wallet} />
        <StatCard
          titulo="Vendas ganhas (período)"
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
            {formatarMoeda(meta.realizadoValor)} de {formatarMoeda(meta.metaValor)}
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
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Funil por etapa</h2>
          <FunilChart dados={funil} />
        </Card>
        <Card className="p-4">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Origem dos leads</h2>
          <OrigemChart dados={origemLeads} />
        </Card>
      </div>

      <Card className="p-4">
        <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Evolução de vendas (6 meses)</h2>
        <EvolucaoChart dados={evolucaoVendas} />
      </Card>

      {desempenhoPorVendedor.length > 1 && (
        <Card className="overflow-x-auto p-4">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Desempenho por vendedor</h2>
          <table className="w-full min-w-[560px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 font-medium">Vendedor</th>
                <th className="pb-2 font-medium">Leads</th>
                <th className="pb-2 font-medium">Abertas</th>
                <th className="pb-2 font-medium">Pipeline</th>
                <th className="pb-2 font-medium">Ganhas</th>
                <th className="pb-2 font-medium">Conversão</th>
              </tr>
            </thead>
            <tbody>
              {desempenhoPorVendedor.map((v) => (
                <tr key={v.vendedorId} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2 text-[var(--fg)]">{v.vendedorNome}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{v.leadsAtribuidos}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{v.oportunidadesAbertas}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarMoeda(v.valorPipeline)}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarMoeda(v.valorGanho)}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarPercentual(v.taxaConversao)}</td>
                </tr>
              ))}
            </tbody>
          </table>
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
            <Target className="size-4" /> Leads parados
          </h2>
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
                    <Badge variant="warning">{l.diasSemContato} dia(s)</Badge>
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
