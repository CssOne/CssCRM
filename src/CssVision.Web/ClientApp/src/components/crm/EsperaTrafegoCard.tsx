import { ChevronRight, Hourglass } from "lucide-react";
import { Fragment, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { EsperaTrafegoPainel } from "../../lib/types";
import { useEstadoSalvo } from "../../lib/useEstadoSalvo";
import { Badge, Card } from "../ui";

function formatarEspera(desde: string, agora: number) {
  const minutos = Math.max(0, Math.floor((agora - new Date(desde).getTime()) / 60000));
  if (minutos < 60) return `${minutos} min`;
  const h = Math.floor(minutos / 60);
  return h >= 24 ? `${Math.floor(h / 24)}d ${h % 24}h` : `${h}h${String(minutos % 60).padStart(2, "0")}`;
}

function formatarHora(iso: string) {
  return new Date(iso).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
}

function formatarDuracao(de: string, ate: string) {
  const minutos = Math.max(0, Math.round((new Date(ate).getTime() - new Date(de).getTime()) / 60000));
  if (minutos < 60) return `${minutos} min`;
  return `${Math.floor(minutos / 60)}h${String(minutos % 60).padStart(2, "0")}`;
}

/**
 * Gestão comercial: quem tem lead do tráfego pago esperando em "Sem etapa" e quem demora com frequência — a mesma regra dos avisos por push
 * (primeiro aviso ao consultor, segundo mais forte e aviso à gestão de quem acumula atrasos).
 */
export function EsperaTrafegoCard({ painel, nomeFiltro }: { painel: EsperaTrafegoPainel; nomeFiltro: (nome: string) => boolean }) {
  // Re-renderiza a cada minuto para o "há quanto tempo" não ficar parado entre as atualizações da tela.
  const [agora, setAgora] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setAgora(Date.now()), 30000);
    return () => clearInterval(t);
  }, []);

  // Começa recolhido (só o resumo) para não ocupar a tela; a escolha fica guardada no navegador.
  const [aberto, setAberto] = useEstadoSalvo("gestao:espera-aberta", false);
  const [regionalSel, setRegionalSel] = useState("");
  const [abertos, setAbertos] =useState<Set<string>>(new Set());
  const alternar = (id: string) =>
    setAbertos((atual) => {
      const novo = new Set(atual);
      if (!novo.delete(id)) novo.add(id);
      return novo;
    });

  const consultores = painel.consultores.filter((c) => nomeFiltro(c.nome));
  // Uma tabela por regional (quem é da 132 não vê a 134 misturada); "Sem regional" por último.
  const porRegional = new Map<string, typeof consultores>();
  for (const c of consultores) {
    const chave = c.regionalNome || "Sem regional";
    porRegional.set(chave, [...(porRegional.get(chave) ?? []), c]);
  }
  const regionais = [...porRegional].sort(([a], [b]) =>
    a === "Sem regional" ? 1 : b === "Sem regional" ? -1 : a.localeCompare(b, "pt-BR", { numeric: true }));
  const reincidentes = consultores.filter((c) => c.reincidente).length;
  const forte = painel.minutosPrazoForte >= 60 ? `${painel.minutosPrazoForte / 60} h` : `${painel.minutosPrazoForte} min`;
  const totalEsperando = consultores.reduce((s, c) => s + c.esperandoAgora, 0);
  const regionalAtiva = regionais.find(([nome]) => nome === regionalSel) ?? regionais[0];

  return (
    <Card className="!rounded-2xl p-3">
      <button
        type="button"
        aria-expanded={aberto}
        onClick={() => setAberto(!aberto)}
        className="flex w-full flex-wrap items-center justify-between gap-x-3 gap-y-1.5 text-left"
      >
        <span className="flex items-center gap-2.5">
          <span className="flex size-7 items-center justify-center rounded-lg bg-[var(--surface-hover)] text-[var(--fg-muted)]">
            <Hourglass className="size-3.5" aria-hidden />
          </span>
          <span>
            <span className="block text-sm font-bold leading-tight tracking-tight text-[var(--fg)]">Leads esperando atendimento</span>
            <span className="block text-[11px] text-[var(--fg-muted)]">Tráfego pago em Sem etapa · últimos {painel.dias} dias</span>
          </span>
        </span>
        <span className="flex flex-wrap items-center gap-1.5">
          <Badge variant={totalEsperando > 0 ? "warning" : "success"}>{totalEsperando} esperando agora</Badge>
          {reincidentes > 0 && <Badge variant="danger">{reincidentes} {reincidentes === 1 ? "consultor demora" : "consultores demoram"}</Badge>}
          <ChevronRight className={`size-4 text-[var(--fg-muted)] transition-transform ${aberto ? "rotate-90" : ""}`} aria-hidden />
        </span>
      </button>

      {/* Abre/fecha deslizando: a altura anima de 0fr a 1fr; fechado, o conteúdo sai do foco e da leitura por teclado/leitor de tela. */}
      <div className={`grid transition-[grid-template-rows,opacity] duration-300 ease-out motion-reduce:transition-none ${aberto ? "grid-rows-[1fr] opacity-100" : "grid-rows-[0fr] opacity-0"}`} aria-hidden={!aberto} inert={!aberto}>
      <div className="min-h-0 overflow-hidden">
      {consultores.length === 0 ? (
        <p className="mt-3 rounded-xl bg-[var(--surface-hover)] px-3 py-3 text-sm text-[var(--fg-muted)]">Nenhum lead do tráfego pago esperando e nenhum atraso nos últimos {painel.dias} dias.</p>
      ) : (
        <div className="mt-3">
        <div role="tablist" className="mb-2 flex flex-wrap gap-1.5">
          {regionais.map(([regional, doGrupo]) => {
            const ativa = regional === regionalAtiva[0];
            const qtd = doGrupo.reduce((s, c) => s + c.esperandoAgora, 0);
            return (
              <button
                key={regional}
                type="button"
                role="tab"
                aria-selected={ativa}
                onClick={() => setRegionalSel(regional)}
                className={`rounded-full border px-3 py-1 text-xs font-semibold transition-colors ${ativa ? "border-[var(--brand)] bg-[var(--brand)]/10 text-[var(--brand)]" : "border-[var(--border)] text-[var(--fg-muted)] hover:bg-[var(--surface-hover)]"}`}
              >
                {regional} · {qtd}
              </button>
            );
          })}
        </div>
        {[regionalAtiva].map(([regional, doGrupo]) => (
        <div key={regional} className="espera-anima max-h-72 overflow-auto">
          <table className="w-full min-w-[620px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-[10px] uppercase tracking-wide text-[var(--fg-muted)]">
                <th className="pb-2 font-semibold">Consultor</th>
                <th className="pb-2 text-right font-semibold">Esperando agora</th>
                <th className="pb-2 text-right font-semibold">Mais antigo</th>
                <th className="pb-2 text-right font-semibold" title={`Leads que passaram de ${painel.minutosPrimeiroPrazo} min sem ele pegar`}>+{painel.minutosPrimeiroPrazo} min ({painel.dias} dias)</th>
                <th className="pb-2 text-right font-semibold" title={`Leads que passaram de ${forte} sem ele pegar`}>+{forte} ({painel.dias} dias)</th>
                <th className="pb-2 pl-3 font-semibold">Situação</th>
              </tr>
            </thead>
            <tbody>
              {doGrupo.map((c) => {
                const desdeMinutos = c.maisAntigoDesde ? (agora - new Date(c.maisAntigoDesde).getTime()) / 60000 : 0;
                const aberto = abertos.has(c.consultorId);
                return (
                  <Fragment key={c.consultorId}>
                  <tr
                    className="cursor-pointer border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-hover)]"
                    onClick={() => alternar(c.consultorId)}
                  >
                    <td className="py-2 font-medium text-[var(--fg)]">
                      <button
                        type="button"
                        aria-expanded={aberto}
                        onClick={(e) => { e.stopPropagation(); alternar(c.consultorId); }}
                        className="flex items-center gap-1.5 text-left"
                        title="Ver os leads que chegaram para ele"
                      >
                        <ChevronRight className={`size-3.5 shrink-0 text-[var(--fg-muted)] transition-transform ${aberto ? "rotate-90" : ""}`} aria-hidden />
                        {c.nome}
                      </button>
                    </td>
                    <td className="py-2 text-right text-[var(--fg)] [font-variant-numeric:tabular-nums]">
                      {c.esperandoAgora > 0 ? (
                        <Link to="/app/crm/leads/kanban" onClick={(e) => e.stopPropagation()} className="font-semibold hover:text-[var(--brand)]" title="Abrir o quadro de leads">{c.esperandoAgora}</Link>
                      ) : (
                        <span className="text-[var(--fg-muted)]">0</span>
                      )}
                    </td>
                    <td className={`py-2 text-right [font-variant-numeric:tabular-nums] ${desdeMinutos >= painel.minutosPrazoForte ? "font-semibold text-[var(--danger)]" : desdeMinutos >= painel.minutosPrimeiroPrazo ? "font-semibold text-[var(--warning)]" : "text-[var(--fg-muted)]"}`}>
                      {c.maisAntigoDesde ? formatarEspera(c.maisAntigoDesde, agora) : "—"}
                    </td>
                    <td className="py-2 text-right text-[var(--fg-muted)] [font-variant-numeric:tabular-nums]">{c.passaramDoPrimeiroPrazo}</td>
                    <td className={`py-2 text-right [font-variant-numeric:tabular-nums] ${c.passaramDoPrazoForte > 0 ? "font-semibold text-[var(--fg)]" : "text-[var(--fg-muted)]"}`}>{c.passaramDoPrazoForte}</td>
                    <td className="py-2 pl-3">
                      {c.reincidente ? (
                        <Badge variant="danger">Demora com frequência</Badge>
                      ) : c.passaramDoPrazoForte > 0 ? (
                        <Badge variant="warning">Atenção</Badge>
                      ) : c.esperandoAgora > 0 ? (
                        <Badge variant="neutral">Em dia</Badge>
                      ) : (
                        <Badge variant="success">Em dia</Badge>
                      )}
                    </td>
                  </tr>
                  {aberto && (
                    <tr className="espera-anima border-b border-[var(--border)] bg-[var(--surface-hover)]/40">
                      <td colSpan={6} className="px-3 py-3">
                        {c.leads.length === 0 ? (
                          <p className="text-xs text-[var(--fg-muted)]">Nenhum lead do tráfego pago chegou para ele nos últimos {painel.dias} dias.</p>
                        ) : (
                          <ul className="divide-y divide-[var(--border)]">
                            {c.leads.map((l) => (
                              <li key={l.leadId} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 py-1.5 text-xs">
                                <Link to={`/app/crm/leads/${l.leadId}`} className="font-medium text-[var(--fg)] hover:text-[var(--brand)]">{l.nome}</Link>
                                <span className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[var(--fg-muted)] [font-variant-numeric:tabular-nums]">
                                  <span>Chegou: <span className="text-[var(--fg)]">{formatarHora(l.chegouEm)}</span></span>
                                  {l.atendidoEm ? (
                                    <span>Atendido: <span className="text-[var(--fg)]">{formatarHora(l.atendidoEm)}</span> ({formatarDuracao(l.chegouEm, l.atendidoEm)} depois)</span>
                                  ) : (
                                    <Badge variant="warning">Esperando há {formatarEspera(l.chegouEm, agora)}</Badge>
                                  )}
                                </span>
                              </li>
                            ))}
                          </ul>
                        )}
                      </td>
                    </tr>
                  )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
        ))}
        </div>
      )}
      <p className="mt-2 text-[11px] text-[var(--fg-muted)]">
        O consultor recebe um aviso aos {painel.minutosPrimeiroPrazo} min e outro aos {forte}. Com {painel.leadsParaReincidencia} ou mais leads acima de {forte} em {painel.dias} dias, a gestão também é avisada.
      </p>
      </div>
      </div>
    </Card>
  );
}
