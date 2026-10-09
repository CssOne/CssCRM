import { BellRing, CheckCircle2, Lock, X } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { useAuth } from "../../context/AuthContext";
import { ativarPush, desativarPush, estadoPush, sincronizarPush, type EstadoPush } from "../../lib/push";
import { api } from "../../lib/api";
import { Button, useToast } from "../ui";

function useEstadoPush() {
  const [estado, setEstado] = useState<EstadoPush | null>(null);
  const atualizar = useCallback(() => {
    estadoPush().then(setEstado).catch(() => setEstado("nao-suportado"));
  }, []);
  useEffect(atualizar, [atualizar]);
  return [estado, setEstado, atualizar] as const;
}

function useAtivar(setEstado: (e: EstadoPush) => void) {
  const { notificar } = useToast();
  const [ativando, setAtivando] = useState(false);
  const ativar = useCallback(async () => {
    setAtivando(true);
    try {
      const novo = await ativarPush();
      setEstado(novo);
      if (novo === "ativo") notificar("success", "Notificações ativadas neste navegador. Enviamos uma de teste.");
      else if (novo === "bloqueado") notificar("error", "O navegador bloqueou as notificações. Libere no cadeado ao lado do endereço do site.");
    } catch {
      notificar("error", "Não foi possível ativar as notificações neste navegador.");
    } finally {
      setAtivando(false);
    }
  }, [notificar, setEstado]);
  return { ativar, ativando };
}

const TEXTO_BLOQUEADO =
  "As notificações estão bloqueadas neste navegador. Clique no cadeado ao lado do endereço do site → Notificações → Permitir, e recarregue a página.";

/** Controle das notificações no painel do sino. */
export function ControleNotificacoesPush() {
  const [estado, setEstado] = useEstadoPush();
  const { ativar, ativando } = useAtivar(setEstado);
  const { notificar } = useToast();

  if (!estado || estado === "nao-suportado") return null;

  return (
    <div className="mx-1 mt-1 rounded-lg border border-[var(--border)] bg-[var(--surface-hover)]/50 p-2.5 text-xs">
      {estado === "ativo" ? (
        <div className="flex items-center justify-between gap-2">
          <span className="flex items-center gap-1.5 font-medium text-[var(--success)]">
            <CheckCircle2 className="size-3.5" /> Aviso de lead novo fora do CRM ativado
          </span>
          <span className="flex gap-2">
            <button
              type="button"
              className="cursor-pointer text-[var(--brand)] hover:underline"
              onClick={() => api.post("/crm/push/teste", {}).then(() => notificar("info", "Notificação de teste enviada."))}
            >
              Testar
            </button>
            <button
              type="button"
              className="cursor-pointer text-[var(--fg-muted)] hover:underline"
              onClick={() => desativarPush().then(() => setEstado("inativo"))}
            >
              Desativar
            </button>
          </span>
        </div>
      ) : estado === "bloqueado" ? (
        <p className="flex items-start gap-1.5 text-[var(--fg-muted)]">
          <Lock className="mt-0.5 size-3.5 shrink-0 text-[var(--warning)]" /> {TEXTO_BLOQUEADO}
        </p>
      ) : (
        <div className="flex items-center justify-between gap-2">
          <span className="text-[var(--fg-muted)]">Receba aviso de lead novo mesmo com o CRM fechado.</span>
          <Button size="sm" onClick={ativar} loading={ativando}>
            Ativar
          </Button>
        </div>
      )}
    </div>
  );
}

/** Quem fechou o convite durante este login (some quando a pessoa sai: o convite volta no próximo login). */
const dispensadosNesteLogin = new Set<string>();

/** Cartão das Configurações: liga/desliga as notificações deste navegador. */
export function CartaoNotificacoesPush() {
  const [estado, setEstado] = useEstadoPush();
  const { ativar, ativando } = useAtivar(setEstado);
  const { notificar } = useToast();

  return (
    <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5">
      <h2 className="flex items-center gap-2 text-base font-semibold text-[var(--fg)]">
        <BellRing className="size-4 text-[var(--brand)]" /> Notificações
      </h2>
      <p className="mt-1 text-sm text-[var(--fg-muted)]">
        Receba no computador os avisos do CRM (lead novo, pagamento em aberto, atividades) mesmo com o sistema fechado ou em outra aba. Vale para
        este navegador. Se não estiverem ativadas, pedimos de novo a cada login.
      </p>
      <div className="mt-3">
        {estado === null ? null : estado === "nao-suportado" ? (
          <p className="text-sm text-[var(--fg-muted)]">Este navegador não suporta notificações.</p>
        ) : estado === "bloqueado" ? (
          <p className="flex items-start gap-1.5 text-sm text-[var(--fg-muted)]">
            <Lock className="mt-0.5 size-4 shrink-0 text-[var(--warning)]" /> {TEXTO_BLOQUEADO}
          </p>
        ) : estado === "ativo" ? (
          <div className="flex flex-wrap items-center gap-3">
            <span className="flex items-center gap-1.5 text-sm font-medium text-[var(--success)]">
              <CheckCircle2 className="size-4" /> Notificações ativadas neste navegador
            </span>
            <Button size="sm" variant="secondary" onClick={() => api.post("/crm/push/teste", {}).then(() => notificar("info", "Notificação de teste enviada."))}>
              Enviar teste
            </Button>
            <Button size="sm" variant="ghost" onClick={() => desativarPush().then(() => setEstado("inativo"))}>
              Desativar
            </Button>
          </div>
        ) : (
          <Button onClick={ativar} loading={ativando}>
            Ativar notificações
          </Button>
        )}
      </div>
    </div>
  );
}

/**
 * Convite para ativar as notificações: aparece a cada login enquanto este navegador não as tiver ativadas ("Agora não" só vale até a
 * pessoa sair). Também reenvia a inscrição de quem já ativou (ver sincronizarPush). A ativação também fica em Configurações.
 */
export function ConviteNotificacoesPush() {
  const { sessao } = useAuth();
  const [estado, setEstado] = useEstadoPush();
  const { ativar, ativando } = useAtivar(setEstado);
  const [dispensado, setDispensado] = useState(() => !!sessao && dispensadosNesteLogin.has(sessao.id));

  useEffect(() => {
    sincronizarPush().catch(() => {});
  }, [sessao?.id]);

  // Ao sair (a casca da tela é desmontada), o "agora não" é esquecido.
  useEffect(() => () => dispensadosNesteLogin.clear(), []);

  // Só aparece quando ainda não está ativado (bloqueado pelo navegador não dá para pedir: ver Configurações).
  if (!sessao || dispensado || estado !== "inativo") return null;

  function dispensar() {
    if (sessao) dispensadosNesteLogin.add(sessao.id);
    setDispensado(true);
  }

  return (
    <div className="fixed bottom-4 left-4 z-[90] w-[calc(100vw-2rem)] max-w-sm rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-2xl lg:left-[17rem]">
      <div className="flex items-start gap-3">
        <div className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand)]">
          <BellRing className="size-5" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold text-[var(--fg)]">Não perca nenhum lead</p>
          <p className="mt-0.5 text-xs text-[var(--fg-muted)]">
            Ative o aviso no computador: quando chegar um lead novo para você, aparece uma notificação mesmo com o CRM fechado ou em outra aba.
          </p>
          <div className="mt-3 flex gap-2">
            <Button size="sm" onClick={ativar} loading={ativando}>
              Ativar notificações
            </Button>
            <Button size="sm" variant="ghost" onClick={dispensar}>
              Agora não
            </Button>
          </div>
        </div>
        <button type="button" aria-label="Fechar" onClick={dispensar} className="cursor-pointer rounded p-0.5 text-[var(--fg-muted)] hover:text-[var(--fg)]">
          <X className="size-4" />
        </button>
      </div>
    </div>
  );
}
