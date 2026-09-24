import { useCallback, useEffect, useRef, useState } from "react";
import { UserPlus, X } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { api } from "../../lib/api";
import { useCrmEventos } from "../../lib/useCrmEventos";

const SOM = "/sounds/novo-lead.mp3";

interface NovoLead {
  leadId: string;
  nome: string;
  atribuidoEm: string;
}

interface NovosLeads {
  agora: string;
  leads: NovoLead[];
}

function lerCursor(chave: string) {
  try {
    return localStorage.getItem(chave);
  } catch {
    return null;
  }
}

function gravarCursor(chave: string, valor: string) {
  try {
    localStorage.setItem(chave, valor);
  } catch {
    /* sem storage: o cursor fica só em memória */
  }
}

/**
 * Avisa o consultor, com o toque do sino, quando um lead passa a ser dele (distribuído do tráfego
 * pago, transferido ou vindo do Notion). Verifica a cada evento do quadro, ao voltar para a aba e a
 * cada minuto (useCrmEventos). O cursor fica no navegador, então leads que chegaram com o CRM
 * fechado são avisados ao abrir.
 */
export function NotificacaoNovosLeads({ usuarioId }: { usuarioId: string }) {
  const navigate = useNavigate();
  const [novos, setNovos] = useState<NovoLead[]>([]);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const cursorRef = useRef<string | null>(null);
  const chave = `novos-leads-desde:${usuarioId}`;

  const tocar = useCallback(() => {
    audioRef.current ??= new Audio(SOM);
    const audio = audioRef.current;
    audio.currentTime = 0;
    audio.play().catch(() => {
      // Navegador bloqueou o som (nenhum clique na página ainda): toca no primeiro clique.
      document.addEventListener("pointerdown", () => audio.play().catch(() => {}), { once: true });
    });
  }, []);

  const verificar = useCallback(() => {
    const desde = cursorRef.current ?? lerCursor(chave);
    const query = desde ? `?desde=${encodeURIComponent(desde)}` : "";
    api
      .get<NovosLeads>(`/crm/notificacoes/novos-leads${query}`)
      .then((r) => {
        cursorRef.current = r.agora;
        gravarCursor(chave, r.agora);
        if (r.leads.length === 0) return;
        setNovos((atual) => {
          const ids = new Set(atual.map((l) => l.leadId));
          return [...r.leads.filter((l) => !ids.has(l.leadId)), ...atual].slice(0, 5);
        });
        tocar();
        if ("Notification" in window && Notification.permission === "granted") {
          new Notification(r.leads.length === 1 ? "Novo lead para você" : `${r.leads.length} novos leads para você`, {
            body: r.leads.map((l) => l.nome).join(", "),
            icon: "/logo-css.png",
          });
        }
      })
      .catch(() => {});
  }, [chave, tocar]);

  // useCrmEventos só dispara em eventos/intervalos: a primeira verificação é ao abrir.
  useEffect(() => {
    verificar();
  }, [verificar]);
  useCrmEventos(verificar, 500);

  if (novos.length === 0) return null;

  const fechar = (id: string) => setNovos((atual) => atual.filter((l) => l.leadId !== id));
  const podePedirNotificacao = "Notification" in window && Notification.permission === "default";

  return (
    <div className="fixed right-4 top-20 z-[95] w-[calc(100vw-2rem)] max-w-sm space-y-2" role="alert">
      {novos.map((l) => (
        <div key={l.leadId} className="rounded-lg border border-[var(--brand)] bg-[var(--surface)] p-3 text-sm shadow-lg">
          <div className="flex items-start gap-2">
            <UserPlus className="mt-0.5 size-4 shrink-0 text-[var(--brand)]" aria-hidden />
            <div className="min-w-0 flex-1">
              <p className="font-semibold text-[var(--fg)]">Novo lead para você</p>
              <p className="truncate text-[var(--fg-muted)]">{l.nome}</p>
              <div className="mt-2 flex gap-3 text-xs font-medium">
                <button
                  type="button"
                  className="cursor-pointer text-[var(--brand)] hover:underline"
                  onClick={() => {
                    fechar(l.leadId);
                    navigate(`/app/crm/leads/${l.leadId}`);
                  }}
                >
                  Abrir lead
                </button>
                {podePedirNotificacao && (
                  <button
                    type="button"
                    className="cursor-pointer text-[var(--fg-muted)] hover:underline"
                    onClick={() => Notification.requestPermission().then(() => setNovos((a) => [...a]))}
                  >
                    Avisar também fora da aba
                  </button>
                )}
              </div>
            </div>
            <button
              type="button"
              aria-label="Fechar"
              className="cursor-pointer rounded p-0.5 text-[var(--fg-muted)] hover:text-[var(--fg)]"
              onClick={() => fechar(l.leadId)}
            >
              <X className="size-4" />
            </button>
          </div>
        </div>
      ))}
    </div>
  );
}
