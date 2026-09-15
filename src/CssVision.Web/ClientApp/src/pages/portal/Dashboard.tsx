import {
  Bot,
  Calendar,
  CalendarClock,
  Coins,
  FileText,
  FolderOpen,
  GraduationCap,
  Headset,
  Medal,
  ShoppingCart,
  Target,
  Trophy,
  UserPlus,
  Users,
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarDataHora, formatarMoeda } from "../../lib/format";
import { TipoAnuncio, TipoAtividade, type Activity, type Announcement, type Dashboard } from "../../lib/types";
import { useAuth } from "../../context/AuthContext";
import { Badge, Card, ErrorState, Skeleton, useToast } from "../../components/ui";
import { PortalStatCard } from "../../components/portal/PortalStatCard";
import { DesempenhoChart } from "../../components/portal/DesempenhoChart";
import { tipoLabel } from "../../components/crm/ActivityForm";

function saudacao(): string {
  const hora = new Date().getHours();
  if (hora < 12) return "Bom dia";
  if (hora < 18) return "Boa tarde";
  return "Boa noite";
}

export function PortalDashboardPage() {
  const { sessao } = useAuth();
  const { notificar } = useToast();

  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [proximas, setProximas] = useState<Activity[]>([]);
  const [avisos, setAvisos] = useState<Announcement[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    Promise.all([
      api.get<Dashboard>("/crm/dashboard", controller.signal),
      api.get<{ itens: Activity[] }>("/crm/activities?visao=3&tamanhoPagina=10", controller.signal),
      api.get<Announcement[]>(`/crm/portal/announcements?tipo=${TipoAnuncio.Aviso}`, controller.signal),
    ])
      .then(([dash, ativ, av]) => {
        setDashboard(dash);
        setProximas(ativ.itens);
        setAvisos(av);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o painel.");
      })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, []);

  const proximosEventos = useMemo(
    () => proximas.filter((a) => a.tipo === TipoAtividade.Reuniao || a.tipo === TipoAtividade.Visita).slice(0, 3),
    [proximas]
  );
  const pendencias = proximas.slice(0, 4);

  if (carregando) {
    return (
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {Array.from({ length: 8 }).map((_, i) => (
          <Skeleton key={i} className="h-28" />
        ))}
      </div>
    );
  }

  if (erro || !dashboard) {
    return <ErrorState message={erro ?? "Não foi possível carregar o painel."} />;
  }

  const { indicadores, meta, evolucaoVendas, desempenhoPorVendedor } = dashboard;
  const primeiroNome = sessao?.nomeCompleto.split(" ")[0] ?? "";
  const comissaoPrevista = indicadores.vendasGanhasAdesaoValor;
  const ranking = [...desempenhoPorVendedor].sort((a, b) => b.valorAdesao - a.valorAdesao).slice(0, 5);
  const medalhas = [Trophy, Medal, Medal];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-[var(--fg)]">
          {saudacao()}, {primeiroNome}! 👋
        </h1>
        <p className="text-sm text-[var(--fg-muted)]">Aqui está o resumo do seu desempenho e atividades de hoje.</p>
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <PortalStatCard
          titulo="Meta do mês"
          valor={`${Math.round(meta.percentualAtingido)}%`}
          icone={Target}
          progresso={meta.percentualAtingido}
          subtitulo={`${meta.realizadoQuantidade} de ${meta.metaQuantidade} vendas`}
        />
        <PortalStatCard
          titulo="Propostas enviadas"
          valor={String(indicadores.oportunidadesAbertas + indicadores.vendasGanhasQuantidade)}
          icone={FileText}
          subtitulo="Oportunidades criadas no período"
        />
        <PortalStatCard titulo="Vendas realizadas" valor={String(indicadores.vendasGanhasQuantidade)} icone={ShoppingCart} subtitulo="No período atual" />
        <PortalStatCard
          titulo="Comissão prevista"
          valor={formatarMoeda(comissaoPrevista)}
          icone={Coins}
          subtitulo="Estimativa (100% do valor da adesão)"
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="p-5 lg:col-span-2">
          <h2 className="mb-1 text-sm font-semibold text-[var(--fg)]">Desempenho no mês</h2>
          <p className="mb-3 text-xs text-[var(--fg-muted)]">Vendas ganhas por mês comparadas à meta atual (últimos 6 meses)</p>
          <DesempenhoChart dados={evolucaoVendas} metaAtual={meta.metaValor} />
        </Card>

        <Card className="p-5">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Pendências</h2>
          {pendencias.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Nenhuma pendência no momento. 🎉</p>
          ) : (
            <ul className="space-y-3">
              {pendencias.map((a) => (
                <li key={a.id}>
                  <Link to={`/app/crm/leads/${a.leadId}`} className="focus-ring flex items-start justify-between gap-2 hover:opacity-80">
                    <div className="min-w-0">
                      <p className="truncate text-sm font-medium text-[var(--fg)]">{a.assunto}</p>
                      <p className="truncate text-xs text-[var(--fg-muted)]">Cliente: {a.leadNome}</p>
                    </div>
                    <span className={`shrink-0 text-xs font-medium ${a.atrasada ? "text-[var(--danger)]" : "text-[var(--warning)]"}`}>
                      {formatarDataHora(a.dataHoraPrevista).split(" ")[0]}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
          <Link to="/app/crm/activities" className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-[var(--brand)] hover:underline">
            Ver todas pendências →
          </Link>
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="p-5 lg:col-span-2">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold text-[var(--fg)]">Ranking de consultores</h2>
            <Link to="/app/crm/gestao" className="text-sm font-medium text-[var(--brand)] hover:underline">
              Ver ranking completo →
            </Link>
          </div>
          {ranking.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Ainda não há vendas registradas neste período.</p>
          ) : (
            <ul className="space-y-2">
              {ranking.map((v, i) => {
                const Medalha = medalhas[i];
                const destaque = sessao && v.vendedorNome === sessao.nomeCompleto;
                const maiorValor = ranking[0]?.valorAdesao || 1;
                return (
                  <li key={v.vendedorId} className={`flex items-center gap-3 rounded-lg px-2 py-2 ${destaque ? "bg-[var(--brand-soft)]" : ""}`}>
                    <span className="flex w-5 shrink-0 items-center justify-center text-sm font-semibold text-[var(--fg-muted)]">
                      {Medalha ? <Medalha className={`size-4 ${i === 0 ? "text-amber-500" : "text-slate-400"}`} /> : i + 1}
                    </span>
                    <span className="min-w-32 shrink-0 truncate text-sm font-medium text-[var(--fg)]">
                      {destaque ? `${v.vendedorNome} (você)` : v.vendedorNome}
                    </span>
                    <span className="w-28 shrink-0 text-sm text-[var(--fg-muted)]">{formatarMoeda(v.valorAdesao)}</span>
                    <div className="h-2 flex-1 overflow-hidden rounded-full bg-[var(--surface-hover)]">
                      <div className="h-full rounded-full bg-[var(--brand)]" style={{ width: `${(v.valorAdesao / maiorValor) * 100}%` }} />
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </Card>

        <Card className="p-5">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Avisos importantes</h2>
          {avisos.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Nenhum aviso no momento.</p>
          ) : (
            <ul className="space-y-3">
              {avisos.map((a) => (
                <li key={a.id} className="flex items-start gap-3">
                  <div className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand)]">
                    <FileText className="size-4" />
                  </div>
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-[var(--fg)]">{a.titulo}</p>
                    <p className="text-xs text-[var(--fg-muted)]">{a.descricao}</p>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="p-5">
          <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold text-[var(--fg)]">
            <CalendarClock className="size-4" /> Próximos eventos
          </h2>
          {proximosEventos.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Nenhum evento agendado. Agende reuniões e visitas na Agenda.</p>
          ) : (
            <ul className="space-y-3">
              {proximosEventos.map((e) => (
                <li key={e.id} className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-2 min-w-0">
                    <Calendar className="size-4 shrink-0 text-[var(--fg-muted)]" />
                    <div className="min-w-0">
                      <p className="truncate text-sm font-medium text-[var(--fg)]">{tipoLabel[e.tipo]}: {e.assunto}</p>
                      <p className="truncate text-xs text-[var(--fg-muted)]">{e.leadNome}</p>
                    </div>
                  </div>
                  <Badge variant="brand">{formatarDataHora(e.dataHoraPrevista)}</Badge>
                </li>
              ))}
            </ul>
          )}
          <Link to="/app/crm/agenda" className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-[var(--brand)] hover:underline">
            Ver agenda completa →
          </Link>
        </Card>

        <Card className="p-5">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Atalhos rápidos</h2>
          <div className="grid grid-cols-3 gap-3">
            {[
              { icone: UserPlus, rotulo: "Novo cliente", rota: "/app/crm/leads" },
              { icone: FileText, rotulo: "Nova proposta", rota: "/app/crm/pipeline" },
              { icone: Users, rotulo: "Meus clientes", rota: "/app/portal/clientes" },
              { icone: FolderOpen, rotulo: "Materiais", rota: "/app/portal/materiais" },
              { icone: GraduationCap, rotulo: "Treinamentos", rota: "/app/portal/treinamentos" },
              { icone: Headset, rotulo: "Fale com suporte", rota: "/app/portal/suporte" },
            ].map((atalho) => (
              <Link
                key={atalho.rotulo}
                to={atalho.rota}
                className="focus-ring flex flex-col items-center gap-2 rounded-xl border border-[var(--border)] p-3 text-center hover:bg-[var(--surface-hover)]"
              >
                <div className="flex size-9 items-center justify-center rounded-full bg-[var(--brand-soft)] text-[var(--brand)]">
                  <atalho.icone className="size-4" />
                </div>
                <span className="text-xs font-medium text-[var(--fg)]">{atalho.rotulo}</span>
              </Link>
            ))}
          </div>
        </Card>
      </div>

      <button
        onClick={() => notificar("info", "Assistente IA chegando em breve.")}
        className="focus-ring fixed bottom-6 right-6 z-10 flex items-center gap-2 rounded-full bg-[var(--brand)] px-5 py-3 text-sm font-semibold text-white shadow-lg hover:opacity-90"
      >
        <Bot className="size-5" />
        Assistente IA
      </button>
    </div>
  );
}
