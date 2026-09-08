import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarDataHora, formatarMoeda, formatarPercentual } from "../../lib/format";
import type { GestaoComercialResumo, RedistribuicaoHistorico, VendedorResumo } from "../../lib/types";
import { Badge, Card, ErrorState, Input, Skeleton, useToast } from "../../components/ui";

function LimiteMensalInput({ vendedor, onSalvo }: { vendedor: VendedorResumo; onSalvo: () => void }) {
  const { notificar } = useToast();
  const [valor, setValor] = useState(vendedor.limiteMensalLeads?.toString() ?? "");
  const [salvando, setSalvando] = useState(false);

  async function salvar() {
    const limite = valor.trim() === "" ? null : Number(valor);
    if (limite !== null && (Number.isNaN(limite) || limite < 0)) {
      notificar("error", "Limite inválido.");
      setValor(vendedor.limiteMensalLeads?.toString() ?? "");
      return;
    }
    if (limite === (vendedor.limiteMensalLeads ?? null)) return;

    setSalvando(true);
    try {
      await api.put(`/crm/management/vendedores/${vendedor.id}/limite`, { limite });
      notificar("success", "Limite mensal atualizado.");
      onSalvo();
    } catch {
      notificar("error", "Não foi possível atualizar o limite.");
      setValor(vendedor.limiteMensalLeads?.toString() ?? "");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <div className="mt-1 flex items-center gap-1 text-xs text-[var(--fg-muted)]">
      <span>{vendedor.leadsRecebidosNoMes} recebido(s) no mês · limite:</span>
      <Input
        className="h-6 w-16 px-1 py-0 text-xs"
        placeholder="—"
        value={valor}
        disabled={salvando}
        onChange={(e) => setValor(e.target.value.replace(/[^0-9]/g, ""))}
        onBlur={salvar}
      />
    </div>
  );
}

export function ManagementPage() {
  const [resumo, setResumo] = useState<GestaoComercialResumo | null>(null);
  const [vendedores, setVendedores] = useState<VendedorResumo[] | null>(null);
  const [historico, setHistorico] = useState<RedistribuicaoHistorico[] | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const carregar = useCallback((signal?: AbortSignal) => {
    setCarregando(true);
    setErro(null);
    Promise.all([
      api.get<GestaoComercialResumo>("/crm/management/summary", signal),
      api.get<VendedorResumo[]>("/crm/management/vendedores", signal),
      api.get<RedistribuicaoHistorico[]>("/crm/management/redistribuicoes", signal),
    ])
      .then(([r, v, h]) => {
        setResumo(r);
        setVendedores(v);
        setHistorico(h);
      })
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar a gestão comercial."); })
      .finally(() => setCarregando(false));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  if (carregando) {
    return (
      <div className="space-y-4">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-40" />
        ))}
      </div>
    );
  }

  if (erro || !resumo || !vendedores || !historico) {
    return <ErrorState message={erro ?? "Não foi possível carregar."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Gestão comercial</h1>
        <p className="text-sm text-[var(--fg-muted)]">
          Distribua leads na página de Leads (seleção em lote) e acompanhe aqui o desempenho da equipe.
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="p-4">
          <h2 className="mb-2 text-sm font-semibold text-[var(--fg)]">Tempo médio até 1º contato</h2>
          <p className="text-2xl font-semibold text-[var(--fg)]">{resumo.tempoMedioPrimeiroContatoHoras.toFixed(1)}h</p>
        </Card>
        <Card className="p-4 lg:col-span-2">
          <h2 className="mb-2 text-sm font-semibold text-[var(--fg)]">Carteira por vendedor</h2>
          <div className="flex flex-wrap gap-3">
            {vendedores.map((v) => (
              <div key={v.id} className="rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-sm">
                <p className="font-medium text-[var(--fg)]">{v.nome}</p>
                <p className="text-xs text-[var(--fg-muted)]">{v.leadsAtivos} leads · {v.oportunidadesAbertas} oportunidades</p>
                <LimiteMensalInput vendedor={v} onSalvo={() => setRecarregar((n) => n + 1)} />
              </div>
            ))}
          </div>
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="overflow-x-auto p-4">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Ranking comercial</h2>
          {resumo.ranking.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Sem vendas no período.</p>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                  <th className="pb-2 font-medium">#</th>
                  <th className="pb-2 font-medium">Vendedor</th>
                  <th className="pb-2 font-medium">Ganhas</th>
                  <th className="pb-2 font-medium">Valor</th>
                  <th className="pb-2 font-medium">Conversão</th>
                </tr>
              </thead>
              <tbody>
                {resumo.ranking.map((r) => (
                  <tr key={r.vendedorId} className="border-b border-[var(--border)] last:border-0">
                    <td className="py-2 text-[var(--fg-muted)]">{r.posicao}</td>
                    <td className="py-2 text-[var(--fg)]">{r.vendedorNome}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{r.vendasGanhas}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{formatarMoeda(r.valorGanho)}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{formatarPercentual(r.taxaConversao)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>

        <Card className="p-4">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Motivos de perda</h2>
          {resumo.motivosPerda.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Sem perdas registradas no período.</p>
          ) : (
            <ul className="space-y-2">
              {resumo.motivosPerda.map((m) => (
                <li key={m.motivo} className="flex items-center justify-between text-sm">
                  <span className="text-[var(--fg)]">{m.motivo}</span>
                  <span className="text-[var(--fg-muted)]">
                    {m.quantidade}x · {formatarMoeda(m.valorPerdido)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <Card className="p-4">
        <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Oportunidades sem movimentação</h2>
        {resumo.oportunidadesSemMovimentacao.length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhuma oportunidade estagnada. 🎉</p>
        ) : (
          <ul className="space-y-2">
            {resumo.oportunidadesSemMovimentacao.map((o) => (
              <li key={o.opportunityId} className="flex items-center justify-between rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-sm">
                <div>
                  <p className="font-medium text-[var(--fg)]">{o.leadNome}</p>
                  <p className="text-xs text-[var(--fg-muted)]">
                    {o.etapaNome} · {o.responsavelNome}
                  </p>
                </div>
                <Badge variant="warning">{o.diasSemMovimentacao} dia(s) parada</Badge>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card className="overflow-x-auto p-4">
        <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Histórico de redistribuições</h2>
        {historico.length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhuma redistribuição registrada.</p>
        ) : (
          <table className="w-full min-w-[560px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 font-medium">Lead</th>
                <th className="pb-2 font-medium">De</th>
                <th className="pb-2 font-medium">Para</th>
                <th className="pb-2 font-medium">Por</th>
                <th className="pb-2 font-medium">Quando</th>
              </tr>
            </thead>
            <tbody>
              {historico.map((h, i) => (
                <tr key={i} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2">
                    <Link to={`/app/crm/leads/${h.leadId}`} className="text-[var(--fg)] hover:text-[var(--brand)]">
                      {h.leadNome}
                    </Link>
                  </td>
                  <td className="py-2 text-[var(--fg-muted)]">{h.responsavelAnteriorNome ?? "—"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{h.responsavelNovoNome}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{h.alteradoPorNome ?? "—"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarDataHora(h.alteradoEm)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </div>
  );
}
