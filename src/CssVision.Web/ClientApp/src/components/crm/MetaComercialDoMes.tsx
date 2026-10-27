import { Target } from "lucide-react";
import type { HTMLAttributes } from "react";
import { corDoTipo } from "../../lib/coresTipoVenda";
import { formatarMoeda, formatarPercentual } from "../../lib/format";
import type { MetaResultado } from "../../lib/types";
import { Card } from "../ui";

/** Onde a meta deveria estar hoje (proporcional aos dias do mês), só no mês corrente. */
export function ritmoDaMeta(meta: MetaResultado, hoje = new Date()) {
  const diasNoMes = new Date(hoje.getFullYear(), hoje.getMonth() + 1, 0).getDate();
  const esperado = (hoje.getDate() / diasNoMes) * 100;
  return { esperado, noRitmo: meta.percentualAtingido >= esperado };
}

/**
 * Meta comercial do mês: o percentual atingido, a barra dividida por tipo de venda (Lead, Indicação, Indicação Lead, Indicação pessoal...) e, em cada tipo, a
 * quantidade de vendas e o valor da adesão. Clicar num tipo (ou em "Ver todas") pede a lista das vendas por trás. Usada na Visão geral e no Relatório comercial.
 */
export function MetaComercialDoMes({
  meta, vendasGanhasValor, vendasGanhasQuantidade, ritmo, onAbrirTipo, cardProps, className = "",
}: {
  meta: MetaResultado;
  vendasGanhasValor: number;
  vendasGanhasQuantidade: number;
  ritmo: { esperado: number; noRitmo: boolean } | null;
  /** Tipo clicado ("" = todas as vendas do mês). */
  onAbrirTipo: (tipo: string) => void;
  cardProps?: HTMLAttributes<HTMLDivElement>;
  className?: string;
}) {
  const tiposDaMeta = meta.porTipo ?? [];
  return (
    <Card {...cardProps} className={`!rounded-2xl p-4 ${className}`}>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2.5">
          <span className="flex size-9 items-center justify-center rounded-xl bg-[var(--surface-hover)] text-[var(--fg-muted)]">
            <Target className="size-5" aria-hidden />
          </span>
          <div>
            <h2 className="text-sm font-bold text-[var(--fg)]">Meta comercial do mês</h2>
            <p className="text-xs text-[var(--fg-muted)]">
              {meta.realizadoQuantidade} de {meta.metaQuantidade} vendas
            </p>
          </div>
        </div>
        <div className="flex items-center gap-3">
          {ritmo && (
            <span className="inline-flex items-center gap-1.5 rounded-full border border-[var(--border)] px-2.5 py-1 text-xs font-medium text-[var(--fg-muted)]">
              <span aria-hidden className={`size-2 rounded-full ${ritmo.noRitmo ? "bg-[var(--success)]" : "bg-[var(--warning)]"}`} />
              {ritmo.noRitmo ? "No ritmo" : "Abaixo do ritmo"} · esperado {Math.round(ritmo.esperado)}%
            </span>
          )}
          <span className="text-4xl font-bold tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarPercentual(meta.percentualAtingido)}</span>
        </div>
      </div>
      <div className="relative h-3 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
        {tiposDaMeta.length > 0 && meta.metaQuantidade > 0 ? (
          // Cada tipo de venda numa cor, do tamanho das vendas dele; clicar abre as vendas (a soma dos trechos é o realizado).
          <div className="flex h-full" style={{ width: `${Math.min(100, meta.percentualAtingido)}%` }}>
            {tiposDaMeta.map((t) => (
              <button
                key={t.tipo}
                type="button"
                onClick={() => onAbrirTipo(t.tipo)}
                title={`${t.tipo}: ${t.quantidade} venda(s) — clique para ver`}
                aria-label={`${t.tipo}: ${t.quantidade} venda(s)`}
                className="focus-ring h-full min-w-0 cursor-pointer border-r border-[var(--surface)] transition-[filter,width] duration-700 last:border-r-0 hover:brightness-110"
                style={{ width: `${(100 * t.quantidade) / meta.realizadoQuantidade}%`, backgroundColor: corDoTipo(t.tipo) }}
              />
            ))}
          </div>
        ) : (
          <div className="h-full rounded-full bg-[var(--brand)] transition-all duration-700" style={{ width: `${Math.min(100, meta.percentualAtingido)}%` }} />
        )}
        {ritmo && <span aria-hidden className="pointer-events-none absolute top-0 h-full w-0.5 bg-[var(--fg)] opacity-60" style={{ left: `${Math.min(100, ritmo.esperado)}%` }} />}
      </div>
      {tiposDaMeta.length > 0 && (
        <ul className="mt-2 flex flex-wrap gap-1" aria-label="Vendas e adesão do mês por tipo">
          {tiposDaMeta.map((t) => (
            <li key={t.tipo}>
              <button
                type="button"
                onClick={() => onAbrirTipo(t.tipo)}
                title={`${t.tipo}: ${formatarMoeda(t.adesao ?? 0)} de adesão em ${t.quantidade} venda(s) — clique para ver`}
                className="focus-ring inline-flex cursor-pointer items-center gap-1 rounded-md bg-[var(--surface-hover)] px-1.5 py-0.5 text-[11px] leading-4 text-[var(--fg-muted)] transition hover:bg-[var(--brand-soft)]"
              >
                <span aria-hidden className="size-1.5 shrink-0 rounded-full" style={{ backgroundColor: corDoTipo(t.tipo) }} />
                {t.tipo} {t.quantidade} · <strong className="font-bold text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarMoeda(t.adesao ?? 0)}</strong>
              </button>
            </li>
          ))}
          <li>
            <button type="button" onClick={() => onAbrirTipo("")} className="focus-ring cursor-pointer px-1 py-0.5 text-[11px] font-medium text-[var(--brand)] hover:underline">
              Ver todas
            </button>
          </li>
        </ul>
      )}
      <p className="mt-2 text-xs text-[var(--fg-muted)]">
        Vendas ganhas: <strong className="text-[var(--fg)]">{formatarMoeda(vendasGanhasValor)}</strong> em {vendasGanhasQuantidade} negócio(s)
        {ritmo ? " · a marca vertical indica onde a meta deveria estar hoje." : "."}
      </p>
    </Card>
  );
}
