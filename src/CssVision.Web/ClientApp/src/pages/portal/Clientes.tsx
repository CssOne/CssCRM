import { CalendarDays, Mail, MapPin, Phone, Plus, UserX } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarData, formatarTelefone } from "../../lib/format";
import type { LeadListItem, LeadStage, PagedResult } from "../../lib/types";
import { Avatar, EmptyState, ErrorState, Pagination, Skeleton } from "../../components/ui";
import { BarraDeFiltros, CampoDeFiltro, type ChipDeFiltro } from "../../components/Filtros";
import { MultiSelect } from "../../components/MultiSelect";
import { PeriodoInput } from "../../components/PeriodoInput";

const SEM_ETAPA = "00000000-0000-0000-0000-000000000000";

export function PortalClientesPage() {
  const [searchParams] = useSearchParams();
  const [busca, setBusca] = useState(searchParams.get("busca") ?? "");
  const [pagina, setPagina] = useState(1);
  const [etapaIds, setEtapaIds] = useState<string[]>([]);
  const [chegadaDe, setChegadaDe] = useState("");
  const [chegadaAte, setChegadaAte] = useState("");
  const [etapas, setEtapas] = useState<LeadStage[]>([]);
  const [dados, setDados] = useState<PagedResult<LeadListItem> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<LeadListItem>>(`/crm/leads${toQueryString({ busca: busca || undefined, leadEtapaIds: etapaIds, dataInicio: chegadaDe || undefined, dataFim: chegadaAte || undefined, pagina, tamanhoPagina: 24 })}`, signal)
        .then(setDados)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar seus clientes.");
        })
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [busca, etapaIds, chegadaDe, chegadaAte, pagina]
  );

  useEffect(() => {
    api.get<LeadStage[]>("/crm/settings/lead-stages").then(setEtapas).catch(() => setEtapas([]));
  }, []);

  const comPagina1 = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPagina(1);
  };
  const dataCurta = (d: string) => (d ? d.split("-").reverse().slice(0, 2).join("/") : "…");
  const chips: ChipDeFiltro[] = [];
  if (etapaIds.length) chips.push({ rotulo: "Etapa", valor: etapaIds.map((id) => etapas.find((e) => e.id === id)?.nome ?? "Sem etapa").join(", "), limpar: () => comPagina1(setEtapaIds)([]) });
  if (chegadaDe || chegadaAte) chips.push({ rotulo: "Chegada", valor: `${dataCurta(chegadaDe)} → ${dataCurta(chegadaAte)}`, limpar: () => { setChegadaDe(""); setChegadaAte(""); setPagina(1); } });
  function limparFiltros() {
    setBusca("");
    setEtapaIds([]);
    setChegadaDe("");
    setChegadaAte("");
    setPagina(1);
  }

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

      <BarraDeFiltros busca={busca} onBusca={comPagina1(setBusca)} placeholder="Nome, documento, telefone ou e-mail" chips={chips} onLimparTudo={limparFiltros}>
        <CampoDeFiltro rotulo="Etapa">
          <MultiSelect
            ariaLabel="Etapa"
            rotuloTodos="Todas"
            opcoes={[
              ...etapas.filter((e) => e.ativa !== false && e.id).map((e) => ({ valor: e.id!, rotulo: e.nome })),
              { valor: SEM_ETAPA, rotulo: "Sem etapa" },
            ]}
            valores={etapaIds}
            onChange={comPagina1(setEtapaIds)}
          />
        </CampoDeFiltro>
        <CampoDeFiltro rotulo="Chegada">
          <PeriodoInput ariaLabel="Chegada" de={chegadaDe} ate={chegadaAte} onChange={(x, y) => { setChegadaDe(x); setChegadaAte(y); setPagina(1); }} />
        </CampoDeFiltro>
      </BarraDeFiltros>

      {carregando ? (
        <div className="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4 2xl:grid-cols-6">
          {Array.from({ length: 12 }).map((_, i) => (
            <Skeleton key={i} className="h-24 rounded-xl" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados || dados.itens.length === 0 ? (
        <EmptyState title="Nenhum cliente encontrado" description="Ajuste a busca ou cadastre um novo cliente." />
      ) : (
        <>
          <div className="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4 2xl:grid-cols-6">
            {dados.itens.map((cliente) => {
              const cor = cliente.etapaCor ?? "#64748b";
              const local = [cliente.cidade, cliente.estado].filter(Boolean).join(" - ");
              const acao =
                "focus-ring relative z-10 flex size-6 items-center justify-center rounded-full bg-[var(--surface-hover)] text-[var(--fg-muted)] transition hover:bg-[var(--brand-soft)] hover:text-[var(--brand)]";
              return (
                <div
                  key={cliente.id}
                  style={{ "--cor": cor } as React.CSSProperties}
                  className="group relative overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 py-2.5 shadow-[var(--shadow-card)] transition-[transform,box-shadow,border-color] duration-200 hover:-translate-y-0.5 hover:border-[color-mix(in_srgb,var(--cor)_50%,var(--border))] hover:shadow-[0_14px_28px_-16px_var(--cor)]"
                >
                  <div className="flex items-start gap-2.5">
                    <Avatar nome={cliente.nomeOuRazaoSocial} className="size-8 shrink-0 text-xs" />
                    <div className="min-w-0 flex-1">
                      <Link to={`/app/crm/leads/${cliente.id}`} className="focus-ring block truncate text-sm font-semibold leading-tight text-[var(--fg)] after:absolute after:inset-0">
                        {cliente.nomeOuRazaoSocial}
                      </Link>
                      {cliente.etapaNome ? (
                        <span
                          className="mt-0.5 inline-flex items-center gap-1 rounded-full px-1.5 py-px text-[10px] font-semibold"
                          style={{ backgroundColor: `color-mix(in srgb, ${cor} 14%, transparent)`, color: `color-mix(in srgb, ${cor} 72%, var(--fg))` }}
                        >
                          <span className="size-1.5 rounded-full" style={{ backgroundColor: cor }} />
                          {cliente.etapaNome}
                        </span>
                      ) : (
                        <span className="mt-0.5 inline-block text-[10px] text-[var(--fg-muted)]">Sem etapa</span>
                      )}
                    </div>
                    <div className="flex shrink-0 gap-1.5">
                      {cliente.telefone && (
                        <a href={`tel:${cliente.telefone}`} title="Ligar" aria-label={`Ligar para ${cliente.nomeOuRazaoSocial}`} className={acao}>
                          <Phone className="size-3" />
                        </a>
                      )}
                      {cliente.email && (
                        <a href={`mailto:${cliente.email}`} title="Enviar e-mail" aria-label={`Enviar e-mail para ${cliente.nomeOuRazaoSocial}`} className={acao}>
                          <Mail className="size-3" />
                        </a>
                      )}
                    </div>
                  </div>

                  <div className="mt-2 space-y-0.5 text-[11px] text-[var(--fg-muted)]">
                    <p className="flex items-center gap-1.5 truncate">
                      <Phone className="size-3 shrink-0" aria-hidden /> {cliente.telefone ? formatarTelefone(cliente.telefone) : "sem telefone"}
                    </p>
                    <p className="flex items-center gap-1.5 truncate">
                      <Mail className="size-3 shrink-0" aria-hidden /> {cliente.email ?? "sem e-mail"}
                    </p>
                  </div>

                  <div className="mt-2 flex items-center justify-between gap-2 border-t border-[var(--border)] pt-1.5 text-[10px] text-[var(--fg-muted)]">
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
