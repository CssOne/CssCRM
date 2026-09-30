import { Bell, Check, Clock } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import { api, ApiRequestError, toQueryString } from "../../lib/api";
import { formatarDataHora } from "../../lib/format";
import { useAbrirLead } from "../../lib/painelLead";
import { VisaoAtividade, type Activity, type PagedResult } from "../../lib/types";
import { IconButton, useToast } from "../ui";
import { tipoLabel } from "./ActivityForm";

/** Avisado quando uma atividade muda (concluída, reagendada, criada): o sino e a agenda se atualizam na hora. */
export const EVENTO_ATIVIDADES_ALTERADAS = "crm:atividades-alteradas";

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
  const { sessao } = useAuth();
  const location = useLocation();
  const abrirLead = useAbrirLead();
  const { notificar } = useToast();
  const [aberto, setAberto] = useState(false);
  const [atrasadas, setAtrasadas] = useState<Activity[]>([]);
  const [hoje, setHoje] = useState<Activity[]>([]);
  const [concluindo, setConcluindo] = useState<string | null>(null);
  const raizRef = useRef<HTMLDivElement>(null);

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
    <li className="flex items-start gap-2 rounded-lg px-2 py-2 hover:bg-[var(--surface-hover)]">
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
          abrirLead(a.leadId);
        }}
      >
        <p className="truncate text-sm font-medium text-[var(--fg)]">{a.assunto}</p>
        <p className="truncate text-xs text-[var(--fg-muted)]">
          {tipoLabel[a.tipo]} · {a.leadNome}
        </p>
        <p className={`flex items-center gap-1 text-xs ${a.atrasada ? "text-[var(--danger)]" : "text-[var(--fg-muted)]"}`}>
          <Clock className="size-3" /> {formatarDataHora(a.dataHoraPrevista)}
        </p>
      </button>
    </li>
  );

  return (
    <div ref={raizRef} className="relative">
      <IconButton label="Notificações" onClick={() => setAberto((v) => !v)}>
        <Bell className="size-4" />
      </IconButton>
      {atrasadas.length > 0 && (
        <span className="pointer-events-none absolute -right-0.5 -top-0.5 flex size-4 items-center justify-center rounded-full bg-[var(--danger)] text-[10px] font-bold text-white">
          {atrasadas.length > 9 ? "9+" : atrasadas.length}
        </span>
      )}

      {aberto && (
        <div className="absolute right-0 top-11 z-30 w-[min(22rem,calc(100vw-2rem))] rounded-xl border border-[var(--border)] bg-[var(--surface)] p-2 shadow-xl">
          <p className="px-2 pb-1 pt-1 text-sm font-semibold text-[var(--fg)]">Notificações</p>
          {atrasadas.length === 0 && hoje.length === 0 ? (
            <p className="px-2 py-6 text-center text-sm text-[var(--fg-muted)]">Nenhuma atividade atrasada ou para hoje. 🎉</p>
          ) : (
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
