import { CalendarDays, Mail, MapPin, Phone, Plus, Search, UserX, X } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarData, formatarTelefone } from "../../lib/format";
import type { LeadListItem, PagedResult } from "../../lib/types";
import { Avatar, EmptyState, ErrorState, Pagination, Skeleton } from "../../components/ui";

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
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold tracking-tight text-[var(--fg)]">Meus clientes</h1>
          <p className="text-sm text-[var(--fg-muted)]">
            <strong className="font-semibold text-[var(--fg)]">{(dados?.totalRegistros ?? 0).toLocaleString("pt-BR")}</strong> cliente(s) na sua carteira.
          </p>
        </div>
        <Link
          to="/app/crm/leads"
          className="focus-ring group inline-flex h-10 items-center gap-2 rounded-full bg-gradient-to-b from-[color-mix(in_srgb,var(--brand)_86%,white)] to-[var(--brand)] pl-1.5 pr-4 text-sm font-semibold text-[var(--brand-fg)] shadow-[0_8px_20px_-8px_var(--brand)] transition-[transform,box-shadow] hover:-translate-y-px hover:shadow-[0_12px_26px_-8px_var(--brand)]"
        >
          <span className="flex size-7 items-center justify-center rounded-full bg-white/20 transition-transform duration-200 group-hover:rotate-90">
            <Plus className="size-4" />
          </span>
          Novo cliente
        </Link>
      </div>

      <div className="ui-surface rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-3 shadow-[var(--shadow-card)]">
        <div className="relative max-w-md">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-3.5 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
          <input
            type="search"
            aria-label="Buscar clientes"
            className="focus-ring h-8 w-full rounded-full border-0 bg-[var(--surface-hover)] pl-8 pr-8 text-[13px] text-[var(--fg)] placeholder:text-[var(--fg-muted)] [&::-webkit-search-cancel-button]:hidden"
            placeholder="Nome, documento, telefone ou e-mail"
            value={busca}
            onChange={(e) => {
              setBusca(e.target.value);
              setPagina(1);
            }}
          />
          {busca && (
            <button
              type="button"
              aria-label="Limpar busca"
              onClick={() => {
                setBusca("");
                setPagina(1);
              }}
              className="focus-ring absolute right-2 top-1/2 flex size-4 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full bg-[var(--fg-muted)]/30 text-[var(--fg)] hover:bg-[var(--fg-muted)]/50"
            >
              <X className="size-2.5" />
            </button>
          )}
        </div>
      </div>

      {carregando ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-36 rounded-2xl" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados || dados.itens.length === 0 ? (
        <EmptyState title="Nenhum cliente encontrado" description="Ajuste a busca ou cadastre um novo cliente." />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {dados.itens.map((cliente) => {
              const cor = cliente.etapaCor ?? "#64748b";
              const local = [cliente.cidade, cliente.estado].filter(Boolean).join(" - ");
              const acao =
                "focus-ring relative z-10 flex size-8 items-center justify-center rounded-full bg-[var(--surface-hover)] text-[var(--fg-muted)] transition hover:bg-[var(--brand-soft)] hover:text-[var(--brand)]";
              return (
                <div
                  key={cliente.id}
                  style={{ "--cor": cor } as React.CSSProperties}
                  className="group relative overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] transition-[transform,box-shadow,border-color] duration-200 hover:-translate-y-0.5 hover:border-[color-mix(in_srgb,var(--cor)_50%,var(--border))] hover:shadow-[0_14px_28px_-16px_var(--cor)]"
                >
                  <div className="flex items-start gap-3">
                    <Avatar nome={cliente.nomeOuRazaoSocial} className="size-10 shrink-0 text-sm" />
                    <div className="min-w-0 flex-1">
                      <Link to={`/app/crm/leads/${cliente.id}`} className="focus-ring block truncate font-semibold text-[var(--fg)] after:absolute after:inset-0">
                        {cliente.nomeOuRazaoSocial}
                      </Link>
                      {cliente.etapaNome ? (
                        <span
                          className="mt-1 inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-semibold"
                          style={{ backgroundColor: `color-mix(in srgb, ${cor} 14%, transparent)`, color: `color-mix(in srgb, ${cor} 72%, var(--fg))` }}
                        >
                          <span className="size-1.5 rounded-full" style={{ backgroundColor: cor }} />
                          {cliente.etapaNome}
                        </span>
                      ) : (
                        <span className="mt-1 inline-block text-[11px] text-[var(--fg-muted)]">Sem etapa</span>
                      )}
                    </div>
                    <div className="flex shrink-0 gap-1.5">
                      {cliente.telefone && (
                        <a href={`tel:${cliente.telefone}`} title="Ligar" aria-label={`Ligar para ${cliente.nomeOuRazaoSocial}`} className={acao}>
                          <Phone className="size-3.5" />
                        </a>
                      )}
                      {cliente.email && (
                        <a href={`mailto:${cliente.email}`} title="Enviar e-mail" aria-label={`Enviar e-mail para ${cliente.nomeOuRazaoSocial}`} className={acao}>
                          <Mail className="size-3.5" />
                        </a>
                      )}
                    </div>
                  </div>

                  <div className="mt-3 space-y-1 text-xs text-[var(--fg-muted)]">
                    <p className="flex items-center gap-1.5 truncate">
                      <Phone className="size-3 shrink-0" aria-hidden /> {cliente.telefone ? formatarTelefone(cliente.telefone) : "sem telefone"}
                    </p>
                    <p className="flex items-center gap-1.5 truncate">
                      <Mail className="size-3 shrink-0" aria-hidden /> {cliente.email ?? "sem e-mail"}
                    </p>
                  </div>

                  <div className="mt-3 flex items-center justify-between gap-2 border-t border-[var(--border)] pt-2.5 text-[11px] text-[var(--fg-muted)]">
                    <span className="flex min-w-0 items-center gap-1 truncate">
                      <MapPin className="size-3 shrink-0" aria-hidden /> {local || "—"}
                    </span>
                    <span className="flex shrink-0 items-center gap-2">
                      {cliente.semContato && (
                        <span className="flex items-center gap-1 rounded-full bg-[var(--warning-soft)] px-1.5 py-0.5 font-semibold text-[var(--warning)]">
                          <UserX className="size-3" aria-hidden /> sem contato
                        </span>
                      )}
                      <span className="flex items-center gap-1">
                        <CalendarDays className="size-3" aria-hidden /> {formatarData(cliente.criadoEm)}
                      </span>
                    </span>
                  </div>
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
