import { Hourglass } from "lucide-react";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { EsperaTrafegoPainel } from "../../lib/types";
import { Badge, Card } from "../ui";

function formatarEspera(desde: string, agora: number) {
  const minutos = Math.max(0, Math.floor((agora - new Date(desde).getTime()) / 60000));
  if (minutos < 60) return `${minutos} min`;
  const h = Math.floor(minutos / 60);
  return h >= 24 ? `${Math.floor(h / 24)}d ${h % 24}h` : `${h}h${String(minutos % 60).padStart(2, "0")}`;
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

  const consultores = painel.consultores.filter((c) => nomeFiltro(c.nome));
  const reincidentes = consultores.filter((c) => c.reincidente).length;
  const forte = painel.minutosPrazoForte >= 60 ? `${painel.minutosPrazoForte / 60} h` : `${painel.minutosPrazoForte} min`;

  return (
    <Card className="!rounded-2xl p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2.5">
          <span className="flex size-8 items-center justify-center rounded-xl bg-[var(--surface-hover)] text-[var(--fg-muted)]">
            <Hourglass className="size-4" aria-hidden />
          </span>
          <div>
            <h2 className="text-[15px] font-bold leading-tight tracking-tight text-[var(--fg)]">Leads esperando atendimento</h2>
            <p className="text-xs text-[var(--fg-muted)]">Tráfego pago parado em Sem etapa, por consultor. Últimos {painel.dias} dias.</p>
          </div>
        </div>
        {reincidentes > 0 && <Badge variant="danger">{reincidentes} {reincidentes === 1 ? "consultor demora" : "consultores demoram"} com frequência</Badge>}
      </div>

      {consultores.length === 0 ? (
        <p className="rounded-xl bg-[var(--surface-hover)] px-3 py-4 text-sm text-[var(--fg-muted)]">Nenhum lead do tráfego pago esperando e nenhum atraso nos últimos {painel.dias} dias.</p>
      ) : (
        <div className="overflow-x-auto">
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
              {consultores.map((c) => {
                const desdeMinutos = c.maisAntigoDesde ? (agora - new Date(c.maisAntigoDesde).getTime()) / 60000 : 0;
                return (
                  <tr key={c.consultorId} className="border-b border-[var(--border)] last:border-0">
                    <td className="py-2 font-medium text-[var(--fg)]">{c.nome}</td>
                    <td className="py-2 text-right text-[var(--fg)] [font-variant-numeric:tabular-nums]">
                      {c.esperandoAgora > 0 ? (
                        <Link to="/app/crm/leads/kanban" className="font-semibold hover:text-[var(--brand)]" title="Abrir o quadro de leads">{c.esperandoAgora}</Link>
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
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      <p className="mt-3 text-[11px] text-[var(--fg-muted)]">
        O consultor recebe um aviso aos {painel.minutosPrimeiroPrazo} min e outro aos {forte}. Com {painel.leadsParaReincidencia} ou mais leads acima de {forte} em {painel.dias} dias, a gestão também é avisada.
      </p>
    </Card>
  );
}
