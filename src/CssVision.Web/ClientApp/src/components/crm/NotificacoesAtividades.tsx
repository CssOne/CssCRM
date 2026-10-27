import { AlertTriangle, Bell, Check, Clock, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import { api, ApiRequestError, toQueryString } from "../../lib/api";
import { useAlertasDaGestao } from "../../lib/alertasDaGestao";
import { formatarDataHora } from "../../lib/format";
import { useAbrirLead } from "../../lib/painelLead";
import { VisaoAtividade, type Activity, type PagedResult } from "../../lib/types";
import { IconButton, useToast } from "../ui";
import { tipoLabel } from "./ActivityForm";
import { ControleNotificacoesPush } from "./NotificacoesPush";

/** Avisado quando uma atividade muda (concluída, reagendada, criada): o sino e a agenda se atualizam na hora. */
export const EVENTO_ATIVIDADES_ALTERADAS = "crm:atividades-alteradas";

function lerDispensadas(chave: string): Set<string> {
  try {
    return new Set(JSON.parse(localStorage.getItem(chave) ?? "[]") as string[]);
  } catch {
    return new Set();
  }
}

function gravarDispensadas(chave: string, ids: Set<string>) {
  try {
    localStorage.setItem(chave, JSON.stringify([...ids].slice(-300)));
  } catch {
    /* sem storage: a notificação volta ao recarregar */
  }
}

/** X de fechar uma notificação (aparece ao passar o mouse; sempre visível no toque). */
function BotaoFechar({ rotulo, aoClicar, className = "" }: { rotulo: string; aoClicar: () => void; className?: string }) {
  return (
    <button
      type="button"
      title="Fechar"
      aria-label={rotulo}
      onClick={aoClicar}
      className={`focus-ring flex size-6 shrink-0 cursor-pointer items-center justify-center rounded-full text-[var(--fg-muted)] opacity-60 transition hover:bg-[var(--border)] hover:text-[var(--fg)] group-hover:opacity-100 ${className}`}
    >
      <X className="size-3.5" aria-hidden />
    </button>
  );
}

export function avisarAtividadesAlteradas() {
  window.dispatchEvent(new Event(EVENTO_ATIVIDADES_ALTERADAS));
}

/** Conclui uma atividade agora (mesmo endpoint da tela de Atividades). */
export async function concluirAtividade(atividade: Activity) {
  await api.post(`/crm/activities/${atividade.id}/complete`, {
    resultado: null,
    dataHoraConclusao: new Date().toISOString(),
    rowVersion: atividade.rowVersion,
  });
  avisarAtividadesAlteradas();
}

/**
 * Sino do topo: as atividades atrasadas e as de hoje DO PRÓPRIO USUÁRIO — o número é só das
 * atrasadas. Antes o número contava as atrasadas de todos que ele enxerga e o clique levava para
 * "Minhas atividades", onde elas não apareciam ("tem 1, mas não tem nada").
 */
export function NotificacoesAtividades() {
  const { sessao, temPapel } = useAuth();
  // Administradores, gestores e supervisores também recebem aqui os alertas que chegam por push (leads parados, consultor que demora), com som.
  const alertasTodos = useAlertasDaGestao(temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial"));
  const location = useLocation();
  const abrirLead = useAbrirLead();
  const { notificar } = useToast();
  const [aberto, setAberto] = useState(false);
  const [atrasadasTodas, setAtrasadas] = useState<Activity[]>([]);
  const [hojeTodas, setHoje] = useState<Activity[]>([]);
  // Notificações que a pessoa fechou (só some do sino, nada é concluído nem apagado): guardado neste navegador, por usuário. "saindo" = animação de saída.
  const chaveDispensadas = `sino-dispensadas:${sessao?.id ?? ""}`;
  const [dispensadas, setDispensadas] = useState<Set<string>>(() => lerDispensadas(chaveDispensadas));
  const [saindo, setSaindo] = useState<Set<string>>(new Set());
  const [balancando, setBalancando] = useState(false);
  const [concluindo, setConcluindo] = useState<string | null>(null);
  const raizRef = useRef<HTMLDivElement>(null);

  const visivel = (id: string) => !dispensadas.has(id);
  const alertasDaGestao = alertasTodos.filter((a) => visivel(a.id));
  const atrasadas = atrasadasTodas.filter((a) => visivel(`ativ:${a.id}`));
  const hoje = hojeTodas.filter((a) => visivel(`ativ:${a.id}`));
  const ocultas = alertasTodos.filter((a) => !visivel(a.id)).length + atrasadasTodas.filter((a) => !visivel(`ativ:${a.id}`)).length + hojeTodas.filter((a) => !visivel(`ativ:${a.id}`)).length;

  /** Fecha uma ou várias notificações: toca a animação de saída e depois some do sino. */
  function dispensar(ids: string[]) {
    setSaindo((s) => new Set([...s, ...ids]));
    setTimeout(() => {
      setDispensadas((atual) => {
        const novo = new Set([...atual, ...ids]);
        gravarDispensadas(chaveDispensadas, novo);
        return novo;
      });
      setSaindo((s) => new Set([...s].filter((id) => !ids.includes(id))));
    }, 220);
  }

  function mostrarOcultas() {
    setDispensadas(new Set());
    gravarDispensadas(chaveDispensadas, new Set());
  }

  // O sino balança quando surge um alerta novo da gestão.
  const quantosAlertas = useRef(alertasDaGestao.length);
  useEffect(() => {
    if (alertasDaGestao.length > quantosAlertas.current) {
      setBalancando(true);
      const t = setTimeout(() => setBalancando(false), 1200);
      quantosAlertas.current = alertasDaGestao.length;
      return () => clearTimeout(t);
    }
    quantosAlertas.current = alertasDaGestao.length;
  }, [alertasDaGestao.length]);

  const carregar = useCallback(
    async (signal?: AbortSignal) => {
      if (!sessao) return;
      const buscar = (visao: VisaoAtividade) =>
        api.get<PagedResult<Activity>>(
          `/crm/activities${toQueryString({ visao, responsavelId: sessao.id, tamanhoPagina: 20 })}`,
          signal
        );
      try {
        const [a, h] = await Promise.all([buscar(VisaoAtividade.Atrasadas), buscar(VisaoAtividade.Hoje)]);
        setAtrasadas(a.itens);
        // "Hoje" inclui as que já passaram da hora — essas aparecem em Atrasadas.
        setHoje(h.itens.filter((x) => !a.itens.some((y) => y.id === x.id)));
      } catch {
        // sem notificações se a busca falhar (ex.: usuário sem acesso às atividades)
      }
    },
    [sessao]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    const intervalo = setInterval(() => carregar(), 60_000);
    const aoAlterar = () => carregar();
    window.addEventListener(EVENTO_ATIVIDADES_ALTERADAS, aoAlterar);
    return () => {
      controller.abort();
      clearInterval(intervalo);
      window.removeEventListener(EVENTO_ATIVIDADES_ALTERADAS, aoAlterar);
    };
  }, [carregar, location.pathname]);

  useEffect(() => {
    if (!aberto) return;
    const fora = (e: MouseEvent) => {
      if (!raizRef.current?.contains(e.target as Node)) setAberto(false);
    };
    const esc = (e: KeyboardEvent) => e.key === "Escape" && setAberto(false);
    document.addEventListener("mousedown", fora);
    document.addEventListener("keydown", esc);
    return () => {
      document.removeEventListener("mousedown", fora);
      document.removeEventListener("keydown", esc);
    };
  }, [aberto]);

  async function concluir(atividade: Activity) {
    setConcluindo(atividade.id);
    try {
      await concluirAtividade(atividade);
      setAtrasadas((l) => l.filter((x) => x.id !== atividade.id));
      setHoje((l) => l.filter((x) => x.id !== atividade.id));
      notificar("success", "Atividade concluída.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível concluir a atividade.");
    } finally {
      setConcluindo(null);
    }
  }

  const Item = ({ a }: { a: Activity }) => (
    <li className={`group flex items-start gap-2 rounded-lg px-2 py-2 hover:bg-[var(--surface-hover)] ${saindo.has(`ativ:${a.id}`) ? "sino-sai" : ""}`}>
      <button
        type="button"
        title="Marcar como feita"
        aria-label={`Marcar "${a.assunto}" como feita`}
        disabled={concluindo === a.id}
        onClick={() => concluir(a)}
        className="focus-ring mt-0.5 flex size-5 shrink-0 cursor-pointer items-center justify-center rounded-full border-2 border-[var(--border)] text-transparent transition hover:border-[var(--success)] hover:text-[var(--success)] disabled:opacity-50"
      >
        <Check className="size-3" strokeWidth={3} />
      </button>
      <button
        type="button"
        className="min-w-0 flex-1 cursor-pointer text-left"
        onClick={() => {
          setAberto(false);
          if (a.leadId) abrirLead(a.leadId);
        }}
      >
        <p className="truncate text-sm font-medium text-[var(--fg)]">{a.assunto}</p>
        <p className="truncate text-xs text-[var(--fg-muted)]">
          {tipoLabel[a.tipo]} · {a.leadNome ?? "Pessoal"}
        </p>
        <p className={`flex items-center gap-1 text-xs ${a.atrasada ? "text-[var(--danger)]" : "text-[var(--fg-muted)]"}`}>
          <Clock className="size-3" /> {formatarDataHora(a.dataHoraPrevista)}
        </p>
      </button>
      <BotaoFechar rotulo={`Fechar a notificação "${a.assunto}"`} aoClicar={() => dispensar([`ativ:${a.id}`])} />
    </li>
  );

  return (
    <div ref={raizRef} className="relative">
      <IconButton label="Notificações" onClick={() => setAberto((v) => !v)}>
        <Bell className={`size-4 ${balancando ? "sino-balanca" : ""}`} />
      </IconButton>
      {atrasadas.length + alertasDaGestao.length > 0 && (
        <span key={atrasadas.length + alertasDaGestao.length} className="sino-pop pointer-events-none absolute -right-0.5 -top-0.5 flex size-4 items-center justify-center rounded-full bg-[var(--danger)] text-[10px] font-bold text-white">
          {atrasadas.length + alertasDaGestao.length > 9 ? "9+" : atrasadas.length + alertasDaGestao.length}
        </span>
      )}

      {aberto && (
        <div className="sino-abre absolute right-0 top-11 z-30 w-[min(22rem,calc(100vw-2rem))] rounded-xl border border-[var(--border)] bg-[var(--surface-solid)] p-2 shadow-xl">
          <div className="flex items-center justify-between px-2 pb-1 pt-1">
            <p className="text-sm font-semibold text-[var(--fg)]">Notificações</p>
            {alertasDaGestao.length + atrasadas.length + hoje.length > 0 && (
              <button
                type="button"
                onClick={() => dispensar([...alertasDaGestao.map((a) => a.id), ...atrasadas.map((a) => `ativ:${a.id}`), ...hoje.map((a) => `ativ:${a.id}`)])}
                className="focus-ring cursor-pointer rounded-md px-1.5 py-0.5 text-xs font-medium text-[var(--brand)] hover:bg-[var(--surface-hover)]"
              >
                Limpar tudo
              </button>
            )}
          </div>
          {alertasDaGestao.length > 0 && (
            <>
              <p className="px-2 pt-1 text-xs font-semibold uppercase text-[var(--danger)]">Alertas da gestão ({alertasDaGestao.length})</p>
              <ul className="mb-1">
                {alertasDaGestao.map((a) => (
                  <li key={a.id} className={`group flex items-start rounded-lg hover:bg-[var(--surface-hover)] ${saindo.has(a.id) ? "sino-sai" : "sino-entra"}`}>
                    <Link to={a.url} onClick={() => setAberto(false)} className="flex min-w-0 flex-1 items-start gap-2 rounded-lg px-2 py-2">
                      <AlertTriangle className={`mt-0.5 size-4 shrink-0 ${a.gravidade === "perigo" ? "text-[var(--danger)]" : "text-[var(--warning)]"}`} aria-hidden />
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-medium text-[var(--fg)]">{a.titulo}</span>
                        <span className="block text-xs text-[var(--fg-muted)]">{a.texto}</span>
                      </span>
                    </Link>
                    <BotaoFechar rotulo={`Fechar o alerta "${a.titulo}"`} aoClicar={() => dispensar([a.id])} className="mr-1 mt-2" />
                  </li>
                ))}
              </ul>
            </>
          )}
          {atrasadas.length === 0 && hoje.length === 0 && alertasDaGestao.length === 0 ? (
            <p className="px-2 py-6 text-center text-sm text-[var(--fg-muted)]">Nenhuma atividade atrasada ou para hoje. 🎉</p>
          ) : atrasadas.length === 0 && hoje.length === 0 ? null : (
            <div className="max-h-96 overflow-y-auto">
              {atrasadas.length > 0 && (
                <>
                  <p className="px-2 pt-2 text-xs font-semibold uppercase text-[var(--danger)]">Atrasadas ({atrasadas.length})</p>
                  <ul>
                    {atrasadas.map((a) => (
                      <Item key={a.id} a={a} />
                    ))}
                  </ul>
                </>
              )}
              {hoje.length > 0 && (
                <>
                  <p className="px-2 pt-2 text-xs font-semibold uppercase text-[var(--fg-muted)]">Hoje ({hoje.length})</p>
                  <ul>
                    {hoje.map((a) => (
                      <Item key={a.id} a={a} />
                    ))}
                  </ul>
                </>
              )}
            </div>
          )}
          {ocultas > 0 && (
            <button type="button" onClick={mostrarOcultas} className="focus-ring mt-1 w-full cursor-pointer rounded-lg px-2 py-1.5 text-center text-xs text-[var(--fg-muted)] hover:bg-[var(--surface-hover)]">
              Mostrar {ocultas} {ocultas === 1 ? "notificação fechada" : "notificações fechadas"}
            </button>
          )}
          <ControleNotificacoesPush />
          <Link
            to="/app/crm/agenda"
            onClick={() => setAberto(false)}
            className="mt-1 block rounded-lg px-2 py-2 text-center text-xs font-medium text-[var(--brand)] hover:bg-[var(--surface-hover)]"
          >
            Ver agenda
          </Link>
        </div>
      )}
    </div>
  );
}
