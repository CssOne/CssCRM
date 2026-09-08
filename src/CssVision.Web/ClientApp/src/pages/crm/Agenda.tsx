import { ChevronLeft, ChevronRight } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarDataHora } from "../../lib/format";
import { VisaoAtividade, type Activity, type PagedResult } from "../../lib/types";
import { Badge, Button, Card, EmptyState, ErrorState, Skeleton } from "../../components/ui";
import { tipoLabel } from "../../components/crm/ActivityForm";

const nomesDias = ["Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo"];

function inicioDaSemana(data: Date): Date {
  const copia = new Date(data);
  const dia = (copia.getDay() + 6) % 7;
  copia.setDate(copia.getDate() - dia);
  copia.setHours(0, 0, 0, 0);
  return copia;
}

function formatarChaveDia(data: Date): string {
  return data.toISOString().slice(0, 10);
}

export function AgendaPage() {
  const [referencia, setReferencia] = useState(() => new Date());
  const [dados, setDados] = useState<PagedResult<Activity> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);

  const inicioSemana = useMemo(() => inicioDaSemana(referencia), [referencia]);
  const dias = useMemo(() => Array.from({ length: 7 }, (_, i) => new Date(inicioSemana.getTime() + i * 86_400_000)), [inicioSemana]);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<Activity>>(
          `/crm/activities${toQueryString({ visao: VisaoAtividade.Semana, dataReferencia: formatarChaveDia(inicioSemana), tamanhoPagina: 200 })}`,
          signal
        )
        .then(setDados)
        .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar a agenda."); })
        .finally(() => setCarregando(false));
    },
    [inicioSemana]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  const porDia = useMemo(() => {
    const mapa = new Map<string, Activity[]>();
    for (const atividade of dados?.itens ?? []) {
      const chave = atividade.dataHoraPrevista.slice(0, 10);
      mapa.set(chave, [...(mapa.get(chave) ?? []), atividade]);
    }
    return mapa;
  }, [dados]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Agenda semanal</h1>
          <p className="text-sm text-[var(--fg-muted)]">
            {inicioSemana.toLocaleDateString("pt-BR")} — {dias[6].toLocaleDateString("pt-BR")}
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" size="sm" onClick={() => setReferencia((d) => new Date(d.getTime() - 7 * 86_400_000))}>
            <ChevronLeft className="size-4" /> Semana anterior
          </Button>
          <Button variant="secondary" size="sm" onClick={() => setReferencia(new Date())}>
            Hoje
          </Button>
          <Button variant="secondary" size="sm" onClick={() => setReferencia((d) => new Date(d.getTime() + 7 * 86_400_000))}>
            Próxima semana <ChevronRight className="size-4" />
          </Button>
        </div>
      </div>

      {carregando ? (
        <div className="grid gap-3 lg:grid-cols-7">
          {Array.from({ length: 7 }).map((_, i) => (
            <Skeleton key={i} className="h-48" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => carregar()} />
      ) : (
        <div className="grid gap-3 lg:grid-cols-7">
          {dias.map((dia, i) => {
            const chave = formatarChaveDia(dia);
            const atividadesDoDia = (porDia.get(chave) ?? []).sort((a, b) => a.dataHoraPrevista.localeCompare(b.dataHoraPrevista));
            const hoje = formatarChaveDia(new Date()) === chave;
            return (
              <Card key={chave} className={`flex flex-col p-3 ${hoje ? "ring-2 ring-[var(--brand)]" : ""}`}>
                <p className="mb-2 text-xs font-semibold uppercase text-[var(--fg-muted)]">
                  {nomesDias[i]} · {dia.getDate().toString().padStart(2, "0")}/{(dia.getMonth() + 1).toString().padStart(2, "0")}
                </p>
                {atividadesDoDia.length === 0 ? (
                  <p className="text-xs text-[var(--fg-muted)]">Sem atividades</p>
                ) : (
                  <ul className="space-y-2">
                    {atividadesDoDia.map((a) => (
                      <li key={a.id}>
                        <Link to={`/app/crm/leads/${a.leadId}`} className="focus-ring block rounded-lg bg-[var(--surface-hover)] p-2 text-xs hover:opacity-80">
                          <div className="flex items-center justify-between gap-1">
                            <Badge variant={a.atrasada ? "danger" : "brand"}>{tipoLabel[a.tipo]}</Badge>
                            <span className="text-[var(--fg-muted)]">{formatarDataHora(a.dataHoraPrevista).split(" ")[1]}</span>
                          </div>
                          <p className="mt-1 truncate font-medium text-[var(--fg)]">{a.assunto}</p>
                          <p className="truncate text-[var(--fg-muted)]">{a.leadNome}</p>
                        </Link>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>
            );
          })}
        </div>
      )}

      {dados && dados.itens.length === 0 && !carregando && (
        <EmptyState title="Nenhuma atividade nesta semana" description="Agende ligações, reuniões ou tarefas para seus leads." />
      )}
    </div>
  );
}
