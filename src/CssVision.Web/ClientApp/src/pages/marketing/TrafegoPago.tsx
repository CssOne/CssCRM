import { Handshake, PhoneMissed, Target, TrendingUp, UserPlus, Users } from "lucide-react";
import { useEffect, useState } from "react";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarDataHora, formatarPercentual, formatarTelefone } from "../../lib/format";
import type { MarketingDashboard } from "../../lib/types";
import { Badge, Card, ErrorState, Input, Select, Skeleton, Button } from "../../components/ui";
import { StatCard } from "../../components/crm/StatCard";
import { LeadsEvolucaoChart, OrigemChart } from "../../components/crm/Charts";

export function TrafegoPagoPage() {
  const [dados, setDados] = useState<MarketingDashboard | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [origem, setOrigem] = useState("");
  const [dataInicio, setDataInicio] = useState("");
  const [dataFim, setDataFim] = useState("");

  const filtro = { origem: origem || undefined, dataInicio: dataInicio || undefined, dataFim: dataFim || undefined };
  const filtrosAtivos = !!(origem || dataInicio || dataFim);

  useEffect(() => {
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    api
      .get<MarketingDashboard>(`/marketing/dashboard${toQueryString(filtro)}`, controller.signal)
      .then(setDados)
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o painel."); })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [origem, dataInicio, dataFim, recarregar]);

  function limparFiltros() {
    setOrigem("");
    setDataInicio("");
    setDataFim("");
  }

  if (carregando && !dados) {
    return (
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-5">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-24" />
        ))}
      </div>
    );
  }

  if (erro || !dados) {
    return <ErrorState message={erro ?? "Não foi possível carregar o painel."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  const { indicadores, porOrigem, porCampanha, evolucao, origensDisponiveis, leads } = dados;
  const origemDonut = porOrigem.map((o) => ({ origem: o.origem, quantidade: o.totalLeads }));

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Tráfego pago</h1>
        <p className="text-sm text-[var(--fg-muted)]">Acompanhamento de leads por origem e campanha.</p>
      </div>

      <div className="flex flex-wrap items-end gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
        <div className="w-56">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Origem</label>
          <Select value={origem} onChange={(e) => setOrigem(e.target.value)}>
            <option value="">Todas</option>
            {origensDisponiveis.map((o) => (
              <option key={o} value={o}>
                {o}
              </option>
            ))}
          </Select>
        </div>
        <div className="flex items-end gap-1">
          <div className="w-36">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Chegada de</label>
            <Input type="date" value={dataInicio} onChange={(e) => setDataInicio(e.target.value)} />
          </div>
          <div className="w-36">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">até</label>
            <Input type="date" value={dataFim} onChange={(e) => setDataFim(e.target.value)} />
          </div>
        </div>
        {filtrosAtivos && (
          <Button variant="ghost" size="sm" onClick={limparFiltros}>
            Limpar filtros
          </Button>
        )}
        <span className="text-xs text-[var(--fg-muted)]">
          {dataInicio || dataFim ? "" : "Últimos 30 dias por padrão"}
        </span>
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-5">
        <StatCard titulo="Total de leads" valor={String(indicadores.totalLeads)} icone={UserPlus} tom="brand" />
        <StatCard titulo="Sem etapa" valor={String(indicadores.leadsSemEtapa)} icone={Users} tom="warning" />
        <StatCard titulo="Sem contato" valor={String(indicadores.leadsSemContato)} icone={PhoneMissed} tom="warning" />
        <StatCard titulo="Vendas ganhas" valor={String(indicadores.leadsGanhos)} icone={Handshake} tom="success" />
        <StatCard titulo="Taxa de conversão" valor={formatarPercentual(indicadores.taxaConversao)} icone={TrendingUp} tom="success" />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="p-4 lg:col-span-2">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Leads por dia</h2>
          <LeadsEvolucaoChart dados={evolucao} />
        </Card>
        <Card className="p-4">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Leads por origem</h2>
          <OrigemChart dados={origemDonut} />
        </Card>
      </div>

      <Card className="overflow-x-auto p-4">
        <div className="mb-3 flex items-center gap-2">
          <Target className="size-4 text-[var(--fg-muted)]" />
          <h2 className="text-sm font-semibold text-[var(--fg)]">Por campanha</h2>
        </div>
        {porCampanha.length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhuma campanha no período.</p>
        ) : (
          <table className="w-full min-w-[640px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 font-medium">Campanha</th>
                <th className="pb-2 font-medium">Origem</th>
                <th className="pb-2 font-medium">Leads</th>
                <th className="pb-2 font-medium">Ganhos</th>
                <th className="pb-2 font-medium">Conversão</th>
                <th className="pb-2 font-medium">Último lead</th>
              </tr>
            </thead>
            <tbody>
              {porCampanha.map((c) => (
                <tr key={c.campanha} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2 text-[var(--fg)]">{c.campanha}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{c.origem ?? "—"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{c.totalLeads}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{c.ganhos}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarPercentual(c.taxaConversao)}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarDataHora(c.ultimoLeadEm)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card className="overflow-x-auto p-4">
        <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Últimos leads</h2>
        {leads.length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhum lead no período.</p>
        ) : (
          <table className="w-full min-w-[760px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 font-medium">Nome</th>
                <th className="pb-2 font-medium">Telefone</th>
                <th className="pb-2 font-medium">Origem</th>
                <th className="pb-2 font-medium">Campanha</th>
                <th className="pb-2 font-medium">Etapa</th>
                <th className="pb-2 font-medium">Responsável</th>
                <th className="pb-2 font-medium">Chegada</th>
              </tr>
            </thead>
            <tbody>
              {leads.map((l) => (
                <tr key={l.id} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2 font-medium text-[var(--fg)]">{l.nomeOuRazaoSocial}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarTelefone(l.telefone) || "—"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{l.origem ?? "—"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{l.campanha ?? "—"}</td>
                  <td className="py-2">
                    <Badge variant={l.etapaNome ? "neutral" : "warning"}>{l.etapaNome ?? "Sem etapa"}</Badge>
                  </td>
                  <td className="py-2 text-[var(--fg-muted)]">{l.responsavelNome ?? "Sem responsável"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarDataHora(l.criadoEm)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </div>
  );
}
