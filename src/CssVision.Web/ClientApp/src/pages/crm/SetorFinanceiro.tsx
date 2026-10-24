import { AlertTriangle, BellRing, CalendarClock, ChevronRight, CircleDollarSign, Search, Target, Users } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarData, formatarMoeda } from "../../lib/format";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import { usePaginacao } from "../../lib/usePaginacao";
import { Avatar, Badge, Card, EmptyState, ErrorState, Input, Pagination, Select, Skeleton } from "../../components/ui";
import { StatCard } from "../../components/crm/StatCard";

interface Resumo {
  consultorId: string;
  consultorNome: string;
  tipoContrato: "Pj" | "Clt" | null;
  modeloPj: "BonusPorPlaca" | "Recorrencia" | null;
  emAberto: number;
  cobrancasEmAberto: number;
  cobrancasAtrasadas: number;
  avisosAbertos: number;
  proximaCobranca: string | null;
  custoLeadMes: number;
  leadsMes: number;
  /** Tem cobranças (de contrato ou automáticas, do padrão da gestão). */
  temCobrancas: boolean;
  /** Cobranças que o consultor disse ter pago e o financeiro ainda não conferiu. */
  aguardandoConferencia: number;
}

type Situacao = "todos" | "atrasados" | "conferir" | "pendentes" | "avisos" | "emdia";

function tipoRotulo(r: Resumo) {
  if (r.tipoContrato === "Clt") return "CLT";
  if (r.tipoContrato === "Pj") return `PJ · ${r.modeloPj === "Recorrencia" ? "recorrência" : "bônus por placa"}`;
  if (r.temCobrancas) return "Sem contrato · valores padrão";
  return "Sem contrato";
}

/**
 * Setor Financeiro: a visão do financeiro e da gestão sobre cada consultor — o que ele deve à CSS (notebook em parcelas, custo de lead, tráfego
 * pago), o que está atrasado e os avisos de pagamento. Cada consultor abre a própria página, onde tudo isso é registrado e editado.
 */
export function SetorFinanceiroPage() {
  const navigate = useNavigate();
  const [dados, setDados] = useState<Resumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [busca, setBusca] = useState("");
  const [situacao, setSituacao] = useState<Situacao>("todos");

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<Resumo[]>("/crm/setor-financeiro/resumo", controller.signal)
      .then((r) => {
        setDados(r);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o Setor Financeiro.");
      });
    return () => controller.abort();
  }, [recarregar]);
  useAtualizarAoVivo(useCallback(() => setRecarregar((n) => n + 1), []));

  const totais = useMemo(() => {
    const lista = dados ?? [];
    return {
      consultores: lista.length,
      emAberto: lista.reduce((n, r) => n + r.emAberto, 0),
      atrasadas: lista.reduce((n, r) => n + r.cobrancasAtrasadas, 0),
      avisos: lista.reduce((n, r) => n + r.avisosAbertos, 0),
      custoLead: lista.reduce((n, r) => n + r.custoLeadMes, 0),
      leads: lista.reduce((n, r) => n + r.leadsMes, 0),
    };
  }, [dados]);

  const filtrados = useMemo(() => {
    const termo = busca.trim().toLowerCase();
    return (dados ?? [])
      .filter((r) => r.consultorNome.toLowerCase().includes(termo))
      .filter((r) => {
        switch (situacao) {
          case "atrasados": return r.cobrancasAtrasadas > 0;
          case "conferir": return r.aguardandoConferencia > 0;
          case "pendentes": return r.cobrancasEmAberto > 0;
          case "avisos": return r.avisosAbertos > 0;
          case "emdia": return (r.tipoContrato !== null || r.temCobrancas) && r.cobrancasEmAberto === 0 && r.avisosAbertos === 0;
          default: return true;
        }
      })
      // Quem mais precisa de atenção primeiro: atrasados, depois maior valor em aberto.
      .sort((a, b) => b.aguardandoConferencia - a.aguardandoConferencia || b.cobrancasAtrasadas - a.cobrancasAtrasadas || b.emAberto - a.emAberto || a.consultorNome.localeCompare(b.consultorNome));
  }, [dados, busca, situacao]);
  const pagina = usePaginacao(filtrados, 12);

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-bold text-[var(--fg)]">Setor Financeiro</h1>
        <p className="text-sm text-[var(--fg-muted)]">
          O que cada consultor deve à CSS — coworking, custo de lead, tráfego pago e custos em parcelas, como o notebook — e os avisos de pagamento. Abra um consultor para registrar pagamentos e editar os custos.
        </p>
      </div>

      {erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados ? (
        <div className="space-y-3">
          <Skeleton className="h-20" />
          <Skeleton className="h-40" />
        </div>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
            <StatCard titulo="Custo de lead no mês" valor={formatarMoeda(totais.custoLead)} icone={Target} tom="brand" subtitulo={`${totais.leads.toLocaleString("pt-BR")} lead(s) de tráfego pago e Notion`} />
            <StatCard titulo="Consultores" valor={String(totais.consultores)} icone={Users} tom="brand" subtitulo="ativos no seu escopo" />
            <StatCard titulo="Total em aberto" valor={formatarMoeda(totais.emAberto)} icone={CircleDollarSign} tom={totais.emAberto > 0 ? "warning" : "success"} subtitulo="cobranças ainda não pagas" />
            <StatCard titulo="Cobranças atrasadas" valor={String(totais.atrasadas)} icone={AlertTriangle} tom={totais.atrasadas > 0 ? "danger" : "success"} subtitulo="de meses que já acabaram" />
            <StatCard titulo="Avisos em aberto" valor={String(totais.avisos)} icone={BellRing} tom={totais.avisos > 0 ? "warning" : "success"} subtitulo="enviados aos consultores" />
          </div>

          <div className="filtros-painel flex flex-wrap items-center gap-2">
            <div className="relative w-full sm:w-72">
              <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
              <Input className="!pl-9" placeholder="Buscar consultor…" aria-label="Buscar consultor" value={busca} onChange={(e) => setBusca(e.target.value)} />
            </div>
            <Select aria-label="Situação" value={situacao} onChange={(e) => setSituacao(e.target.value as Situacao)} className="!w-auto">
              <option value="todos">Todos</option>
              <option value="atrasados">Com atraso</option>
              <option value="conferir">Aguardando conferência</option>
              <option value="pendentes">Com cobrança em aberto</option>
              <option value="avisos">Com aviso em aberto</option>
              <option value="emdia">Em dia</option>
            </Select>
          </div>

          {filtrados.length === 0 ? (
            <EmptyState title="Nenhum consultor" description={dados.length === 0 ? "Não há consultores para mostrar." : "Nenhum consultor com esse filtro."} />
          ) : (
            <>
              <Card className="overflow-x-auto">
                <table className="w-full min-w-[56rem] text-sm [font-variant-numeric:tabular-nums]">
                  <thead>
                    <tr className="text-left">
                      {["Consultor", "Contrato", "Leads no mês", "Custo de lead", "Em aberto", "Situação", ""].map((h, i) => (
                        <th key={`${h}${i}`} className={`border-b border-[var(--border)] px-4 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)] ${i >= 2 && i <= 4 ? "text-right" : ""}`}>{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {pagina.itensDaPagina.map((r) => {
                      const pj = r.tipoContrato === "Pj" || (r.tipoContrato === null && r.temCobrancas);
                      const semContrato = r.tipoContrato === null && !r.temCobrancas;
                      return (
                        <tr
                          key={r.consultorId}
                          onClick={() => navigate(`/app/crm/setor-financeiro/${r.consultorId}`)}
                          className="group cursor-pointer border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--brand-soft)]"
                        >
                          <td className="px-4 py-2.5">
                            <Link
                              to={`/app/crm/setor-financeiro/${r.consultorId}`}
                              onClick={(e) => e.stopPropagation()}
                              className="focus-ring flex items-center gap-3 rounded"
                              aria-label={`Abrir o Setor Financeiro de ${r.consultorNome}`}
                            >
                              <Avatar nome={r.consultorNome} usuarioId={r.consultorId} className="size-8 text-xs" />
                              <span className="min-w-0 truncate font-semibold text-[var(--fg)]" title={r.consultorNome}>{r.consultorNome}</span>
                            </Link>
                          </td>
                          <td className="whitespace-nowrap px-4 py-2.5 text-[var(--fg-muted)]">{tipoRotulo(r)}</td>
                          <td className="px-4 py-2.5 text-right">{pj ? r.leadsMes.toLocaleString("pt-BR") : "—"}</td>
                          <td className="px-4 py-2.5 text-right font-medium text-[var(--fg)]" title="Valor por lead × leads de tráfego pago e Notion do mês">{pj ? formatarMoeda(r.custoLeadMes) : "—"}</td>
                          <td className={`px-4 py-2.5 text-right font-bold ${semContrato ? "text-[var(--fg-muted)]" : r.emAberto > 0 ? "text-[var(--fg)]" : "text-[var(--success)]"}`}>
                            {semContrato ? "—" : formatarMoeda(r.emAberto)}
                          </td>
                          <td className="px-4 py-2.5">
                            <div className="flex flex-wrap items-center gap-1">
                              {r.aguardandoConferencia > 0 && <Badge variant="info">{r.aguardandoConferencia} p/ conferir</Badge>}
                              {r.cobrancasAtrasadas > 0 && <Badge variant="danger">{r.cobrancasAtrasadas} atrasada(s)</Badge>}
                              {r.cobrancasAtrasadas === 0 && r.cobrancasEmAberto > 0 && <Badge variant="warning">{r.cobrancasEmAberto} em aberto</Badge>}
                              {r.avisosAbertos > 0 && <Badge variant="warning">{r.avisosAbertos} aviso(s)</Badge>}
                              {r.cobrancasEmAberto === 0 && r.avisosAbertos === 0 && (semContrato ? <Badge variant="neutral">Sem contrato</Badge> : <Badge variant="success">Em dia</Badge>)}
                              {r.proximaCobranca && (
                                <span className="inline-flex items-center gap-1 text-xs text-[var(--fg-muted)]" title="Uma nova cobrança começa nesta data">
                                  <CalendarClock className="size-3.5 text-[var(--warning)]" aria-hidden /> {formatarData(r.proximaCobranca)}
                                </span>
                              )}
                            </div>
                          </td>
                          <td className="px-4 py-2.5 text-right">
                            <ChevronRight className="inline size-4 text-[var(--fg-muted)] transition-transform group-hover:translate-x-0.5" aria-hidden />
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </Card>
              <Pagination pagina={pagina.pagina} totalPaginas={pagina.totalPaginas} onChange={pagina.setPagina} />
            </>
          )}
        </>
      )}
    </div>
  );
}
