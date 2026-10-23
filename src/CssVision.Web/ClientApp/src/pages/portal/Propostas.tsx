import { CalendarDays, FileText, Trophy, XCircle } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarData, formatarMoeda } from "../../lib/format";
import { TipoEtapaPipeline, type Opportunity, type PagedResult } from "../../lib/types";

/** Em propostas ganhas vale o valor da adesão paga; nas demais, o valor final ou o estimado. */
const valorDaProposta = (o: Opportunity) =>
  o.etapaTipo === TipoEtapaPipeline.Ganho && o.pagamentoAdesao != null ? o.pagamentoAdesao : o.valorFinal ?? o.valorEstimado ?? 0;
import { BotaoWhatsApp } from "../../components/crm/BotaoWhatsApp";
import { Avatar, EmptyState, ErrorState, Pagination, Select, Skeleton } from "../../components/ui";
import { BarraDeFiltros, CampoDeFiltro, type ChipDeFiltro } from "../../components/Filtros";
import { PeriodoInput } from "../../components/PeriodoInput";

const STATUS: Record<string, string> = { "1": "Em andamento", "2": "Ganho", "3": "Perdido" };

export function PortalPropostasPage() {
  const [pagina, setPagina] = useState(1);
  const [status, setStatus] = useState("");
  const [de, setDe] = useState("");
  const [ate, setAte] = useState("");
  const [dados, setDados] = useState<PagedResult<Opportunity> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<Opportunity>>(`/crm/opportunities${toQueryString({ statusEtapa: status || undefined, dataInicio: de || undefined, dataFim: ate || undefined, pagina, tamanhoPagina: 12 })}`, signal)
        .then(setDados)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar suas propostas.");
        })
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [status, de, ate, pagina]
  );

  const dataCurta = (d: string) => (d ? d.split("-").reverse().slice(0, 2).join("/") : "…");
  const chips: ChipDeFiltro[] = [];
  if (status) chips.push({ rotulo: "Situação", valor: STATUS[status], limpar: () => { setStatus(""); setPagina(1); } });
  if (de || ate) chips.push({ rotulo: "Período", valor: `${dataCurta(de)} → ${dataCurta(ate)}`, limpar: () => { setDe(""); setAte(""); setPagina(1); } });
  function limparFiltros() {
    setStatus("");
    setDe("");
    setAte("");
    setPagina(1);
  }

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  const totalPagina = (dados?.itens ?? []).reduce((n, o) => n + valorDaProposta(o), 0);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold tracking-tight text-[var(--fg)]">Minhas propostas</h1>
          <p className="text-sm text-[var(--fg-muted)]">
            <strong className="font-semibold text-[var(--fg)]">{(dados?.totalRegistros ?? 0).toLocaleString("pt-BR")}</strong> proposta(s)/oportunidade(s) em andamento.
          </p>
        </div>
        {dados && dados.itens.length > 0 && (
          <div className="ui-surface rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-2 text-right shadow-[var(--shadow-card)]">
            <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Valor nesta página</p>
            <p className="text-lg font-bold leading-tight tracking-tight text-[var(--brand)] [font-variant-numeric:tabular-nums]">{formatarMoeda(totalPagina)}</p>
          </div>
        )}
      </div>

      <BarraDeFiltros chips={chips} onLimparTudo={limparFiltros}>
        <CampoDeFiltro rotulo="Situação">
          <Select aria-label="Situação" value={status} onChange={(e) => { setStatus(e.target.value); setPagina(1); }}>
            <option value="">Todas</option>
            {Object.entries(STATUS).map(([v, r]) => (
              <option key={v} value={v}>{r}</option>
            ))}
          </Select>
        </CampoDeFiltro>
        <CampoDeFiltro rotulo="Período">
          <PeriodoInput ariaLabel="Período" de={de} ate={ate} onChange={(x, y) => { setDe(x); setAte(y); setPagina(1); }} />
        </CampoDeFiltro>
      </BarraDeFiltros>

      {carregando ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-[4.5rem] rounded-2xl" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => carregar()} />
      ) : !dados || dados.itens.length === 0 ? (
        <EmptyState title="Nenhuma proposta encontrada" description="Envie propostas pelo Pipeline para acompanhá-las aqui." />
      ) : (
        <>
          <div className="space-y-2">
            {dados.itens.map((op) => {
              const ganho = op.etapaTipo === TipoEtapaPipeline.Ganho;
              const perdido = op.etapaTipo === TipoEtapaPipeline.Perdido;
              const cor = ganho ? "var(--success)" : perdido ? "var(--danger)" : "var(--brand)";
              const Icone = ganho ? Trophy : perdido ? XCircle : FileText;
              return (
                <div key={op.id} className="relative">
                <Link
                  to={`/app/crm/leads/${op.leadId}`}
                  style={{ "--cor": cor } as React.CSSProperties}
                  className="focus-ring group flex flex-wrap items-center gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-3 pr-12 shadow-[var(--shadow-card)] transition-[transform,box-shadow,border-color] duration-200 hover:-translate-y-0.5 hover:border-[color-mix(in_srgb,var(--cor)_50%,var(--border))] hover:shadow-[0_12px_26px_-16px_var(--cor)]"
                >
                  <span className="flex size-10 shrink-0 items-center justify-center rounded-xl" style={{ backgroundColor: `color-mix(in srgb, ${cor} 14%, transparent)`, color: cor }}>
                    <Icone className="size-5" aria-hidden />
                  </span>
                  <div className="min-w-0 flex-1 basis-48">
                    <p className="truncate font-semibold text-[var(--fg)]">{op.titulo}</p>
                    <p className="flex items-center gap-1.5 truncate text-xs text-[var(--fg-muted)]">
                      <Avatar nome={op.leadNome} className="size-4 text-[8px]" />
                      <span className="truncate">
                        {op.leadNome}
                        {op.produtoOuServico && ` · ${op.produtoOuServico}`}
                      </span>
                    </p>
                  </div>
                  <span
                    className="inline-flex shrink-0 items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-semibold"
                    style={{ backgroundColor: `color-mix(in srgb, ${cor} 14%, transparent)`, color: `color-mix(in srgb, ${cor} 75%, var(--fg))` }}
                  >
                    <span className="size-1.5 rounded-full" style={{ backgroundColor: cor }} />
                    {op.etapaNome}
                  </span>
                  <div className="w-32 shrink-0 text-right">
                    <p className="text-sm font-bold text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarMoeda(valorDaProposta(op))}</p>
                    {ganho && op.pagamentoAdesao != null && <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--success)]">adesão</p>}
                    <p className="flex items-center justify-end gap-1 text-[11px] text-[var(--fg-muted)]">
                      <CalendarDays className="size-3" aria-hidden /> {formatarData(op.criadoEm)}
                    </p>
                  </div>
                </Link>
                <BotaoWhatsApp telefone={op.leadTelefone} nome={op.leadNome} className="absolute right-3 top-1/2 size-7 -translate-y-1/2" />
                </div>
              );
            })}
          </div>
          <Pagination pagina={dados.pagina} totalPaginas={dados.totalPaginas} onChange={setPagina} />
        </>
      )}
    </div>
  );
}
