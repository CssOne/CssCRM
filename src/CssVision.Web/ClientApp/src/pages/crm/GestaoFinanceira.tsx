import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import type { VendedorResumo } from "../../lib/types";

interface AvisosEmAberto { consultorId: string; abertos: number }
import { Badge, Card, EmptyState, ErrorState, Input, Pagination, Skeleton, useToast } from "../../components/ui";
import { usePaginacao } from "../../lib/usePaginacao";

function Chave({ vendedor, onSalvo }: { vendedor: VendedorResumo; onSalvo: () => void }) {
  const { notificar } = useToast();
  const [ligado, setLigado] = useState(vendedor.recebeLeads !== false);
  const [salvando, setSalvando] = useState(false);

  async function alternar() {
    const novo = !ligado;
    setLigado(novo);
    setSalvando(true);
    try {
      await api.put(`/crm/management/vendedores/${vendedor.id}/recebe-leads`, { recebeLeads: novo });
      notificar("success", novo ? `${vendedor.nome} voltou a receber leads.` : `${vendedor.nome} parou de receber leads.`);
      onSalvo();
    } catch {
      setLigado(!novo);
      notificar("error", "Não foi possível alterar.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <button
      type="button"
      role="switch"
      aria-checked={ligado}
      disabled={salvando}
      onClick={alternar}
      title={ligado ? "Recebendo leads da distribuição automática — clique para pausar" : "Fora da distribuição de leads — clique para voltar a receber"}
      className="focus-ring flex cursor-pointer items-center gap-2 text-xs font-medium disabled:opacity-60"
    >
      <span className={`relative inline-flex h-4 w-7 shrink-0 items-center rounded-full transition-colors ${ligado ? "bg-[var(--success)]" : "bg-[var(--border)]"}`}>
        <span className={`inline-block size-3 rounded-full bg-white shadow transition-transform ${ligado ? "translate-x-3.5" : "translate-x-0.5"}`} />
      </span>
      <span className={ligado ? "text-[var(--success)]" : "text-[var(--fg-muted)]"}>{ligado ? "Recebe leads" : "Não recebe leads"}</span>
    </button>
  );
}

/**
 * Gestão comercial do perfil Financeiro: os consultores da regional dele, quantos leads cada um pegou e a chave de "recebe lead".
 * (Limites, horários e tipos de lead continuam com a gestão comercial.)
 */
export function GestaoFinanceiraPage() {
  const [vendedores, setVendedores] = useState<VendedorResumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [busca, setBusca] = useState("");
  const [avisosAbertos, setAvisosAbertos] = useState<Record<string, number>>({});

  useEffect(() => {
    const controller = new AbortController();
    // Avisos de pagamento em aberto por consultor: se falhar, a tela segue sem a contagem.
    api
      .get<AvisosEmAberto[]>("/crm/avisos-pagamento/resumo", controller.signal)
      .then((r) => setAvisosAbertos(Object.fromEntries(r.map((x) => [x.consultorId, x.abertos]))))
      .catch(() => {});
    api
      .get<VendedorResumo[]>("/crm/management/vendedores", controller.signal)
      .then((v) => {
        setVendedores(v);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar a gestão comercial.");
      });
    return () => controller.abort();
  }, [recarregar]);
  useAtualizarAoVivo(useCallback(() => setRecarregar((n) => n + 1), []));

  const filtrados = (vendedores ?? []).filter((v) => v.nome.toLowerCase().includes(busca.trim().toLowerCase()));
  const pagina = usePaginacao(filtrados, 12);

  if (erro) return <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />;
  if (!vendedores) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-24" />
        ))}
      </div>
    );
  }

  const recebendo = vendedores.filter((v) => v.recebeLeads !== false).length;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Gestão comercial</h1>
          <p className="text-sm text-[var(--fg-muted)]">
            Consultores da sua regional · {recebendo} de {vendedores.length} recebendo leads.
          </p>
        </div>
        <div className="w-60">
          <Input placeholder="Buscar consultor…" value={busca} onChange={(e) => setBusca(e.target.value)} aria-label="Buscar consultor" />
        </div>
      </div>

      {filtrados.length === 0 ? (
        <EmptyState title="Nenhum consultor" description="Não há consultores da sua regional para mostrar." />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {pagina.itensDaPagina.map((v) => (
              <Card key={v.id} className="space-y-2 p-4 text-sm">
                <div className="flex items-start justify-between gap-2">
                  <p className="min-w-0 truncate font-semibold text-[var(--fg)]" title={v.nome}>{v.nome}</p>
                  {v.ativo === false && <Badge variant="danger">Inativo</Badge>}
                </div>
                <p className="text-xs text-[var(--fg-muted)]">
                  <span className="font-semibold text-[var(--fg)]">{v.leadsAtivos.toLocaleString("pt-BR")}</span> leads na carteira ·{" "}
                  {v.oportunidadesAbertas} oportunidades abertas
                </p>
                <p className="text-xs text-[var(--fg-muted)]" title="Leads de tráfego pago que chegaram para o consultor">
                  <span className="font-semibold text-[var(--brand)]">{(v.leadsTrafegoNoMes ?? 0).toLocaleString("pt-BR")}</span> leads pegos no mês ·{" "}
                  {(v.leadsTrafegoMesAnterior ?? 0).toLocaleString("pt-BR")} no mês anterior
                </p>
                <p className="text-xs text-[var(--fg-muted)]">
                  Hoje: {(v.leadsRecebidosHoje ?? 0).toLocaleString("pt-BR")}
                  {v.limiteDiarioLeads != null ? ` de ${v.limiteDiarioLeads}` : ""} · No mês: {v.leadsRecebidosNoMes.toLocaleString("pt-BR")}
                  {v.limiteMensalLeads != null ? ` de ${v.limiteMensalLeads}` : ""}
                </p>
                <div className="flex items-center justify-between gap-2 border-t border-[var(--border)] pt-2">
                  <Chave vendedor={v} onSalvo={() => setRecarregar((n) => n + 1)} />
                  <Link
                    to={`/app/crm/avisos-pagamento?consultor=${v.id}`}
                    className="shrink-0 text-xs font-medium text-[var(--brand)] hover:underline"
                    title="Enviar ou ver avisos de pagamento em aberto deste consultor"
                  >
                    {(avisosAbertos[v.id] ?? 0) > 0 ? `${avisosAbertos[v.id]} aviso(s) em aberto` : "Avisar pagamento"}
                  </Link>
                </div>
              </Card>
            ))}
          </div>
          <Pagination pagina={pagina.pagina} totalPaginas={pagina.totalPaginas} onChange={pagina.setPagina} />
        </>
      )}
    </div>
  );
}
