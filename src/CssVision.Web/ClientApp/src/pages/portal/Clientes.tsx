import { Plus, Search } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarData, formatarTelefone } from "../../lib/format";
import type { LeadListItem, PagedResult } from "../../lib/types";
import { Badge, EmptyState, ErrorState, Input, Pagination, Skeleton } from "../../components/ui";

export function PortalClientesPage() {
  const [searchParams] = useSearchParams();
  const [busca, setBusca] = useState(searchParams.get("busca") ?? "");
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<PagedResult<LeadListItem> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<LeadListItem>>(`/crm/leads${toQueryString({ busca, pagina, tamanhoPagina: 12 })}`, signal)
        .then(setDados)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar seus clientes.");
        })
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [busca, pagina]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Meus clientes</h1>
          <p className="text-sm text-[var(--fg-muted)]">{dados?.totalRegistros ?? 0} cliente(s) na sua carteira.</p>
        </div>
        <Link
          to="/app/crm/leads"
          className="focus-ring inline-flex items-center gap-2 rounded-lg bg-[var(--brand)] px-4 py-2 text-sm font-medium text-white hover:opacity-90"
        >
          <Plus className="size-4" /> Novo cliente
        </Link>
      </div>

      <div className="relative max-w-md">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" />
        <Input
          className="pl-9"
          placeholder="Nome, documento, telefone ou e-mail"
          value={busca}
          onChange={(e) => {
            setBusca(e.target.value);
            setPagina(1);
          }}
        />
      </div>

      {carregando ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-28" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados || dados.itens.length === 0 ? (
        <EmptyState title="Nenhum cliente encontrado" description="Ajuste a busca ou cadastre um novo cliente." />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {dados.itens.map((cliente) => (
              <Link
                key={cliente.id}
                to={`/app/crm/leads/${cliente.id}`}
                className="focus-ring block rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-sm hover:shadow-md"
              >
                <div className="flex items-start justify-between gap-2">
                  <p className="truncate font-medium text-[var(--fg)]">{cliente.nomeOuRazaoSocial}</p>
                  {cliente.etapaNome && (
                    <Badge variant="brand">
                      <span style={cliente.etapaCor ? { color: cliente.etapaCor } : undefined}>{cliente.etapaNome}</span>
                    </Badge>
                  )}
                </div>
                <p className="mt-1 truncate text-sm text-[var(--fg-muted)]">{formatarTelefone(cliente.telefone)}</p>
                <p className="truncate text-xs text-[var(--fg-muted)]">{cliente.email ?? "sem e-mail"}</p>
                <div className="mt-3 flex items-center justify-between text-xs text-[var(--fg-muted)]">
                  <span>{[cliente.cidade, cliente.estado].filter(Boolean).join(" - ") || "—"}</span>
                  <span>desde {formatarData(cliente.criadoEm)}</span>
                </div>
              </Link>
            ))}
          </div>
          <Pagination pagina={dados.pagina} totalPaginas={dados.totalPaginas} onChange={setPagina} />
        </>
      )}
    </div>
  );
}
