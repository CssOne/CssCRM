import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarData, formatarMoeda } from "../../lib/format";
import { TipoEtapaPipeline, type Opportunity, type PagedResult } from "../../lib/types";
import { Badge, EmptyState, ErrorState, Pagination, Skeleton } from "../../components/ui";

export function PortalPropostasPage() {
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<PagedResult<Opportunity> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<Opportunity>>(`/crm/opportunities${toQueryString({ pagina, tamanhoPagina: 12 })}`, signal)
        .then(setDados)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar suas propostas.");
        })
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [pagina]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Minhas propostas</h1>
        <p className="text-sm text-[var(--fg-muted)]">{dados?.totalRegistros ?? 0} proposta(s)/oportunidade(s) em andamento.</p>
      </div>

      {carregando ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-16" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setPagina((p) => p)} />
      ) : !dados || dados.itens.length === 0 ? (
        <EmptyState title="Nenhuma proposta encontrada" description="Envie propostas pelo Pipeline para acompanhá-las aqui." />
      ) : (
        <>
          <div className="space-y-2">
            {dados.itens.map((op) => (
              <Link
                key={op.id}
                to={`/app/crm/leads/${op.leadId}`}
                className="focus-ring flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-sm hover:shadow-md"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium text-[var(--fg)]">{op.titulo}</p>
                  <p className="truncate text-xs text-[var(--fg-muted)]">
                    {op.leadNome} {op.produtoOuServico && `· ${op.produtoOuServico}`}
                  </p>
                </div>
                <Badge variant={op.etapaTipo === TipoEtapaPipeline.Ganho ? "success" : op.etapaTipo === TipoEtapaPipeline.Perdido ? "danger" : "brand"}>
                  {op.etapaNome}
                </Badge>
                <span className="w-28 shrink-0 text-right text-sm font-medium text-[var(--fg)]">{formatarMoeda(op.valorFinal ?? op.valorEstimado)}</span>
                <span className="w-24 shrink-0 text-right text-xs text-[var(--fg-muted)]">{formatarData(op.criadoEm)}</span>
              </Link>
            ))}
          </div>
          <Pagination pagina={dados.pagina} totalPaginas={dados.totalPaginas} onChange={setPagina} />
        </>
      )}
    </div>
  );
}
