import { useCallback, useEffect, useRef, useState } from "react";
import { Wallet, X } from "lucide-react";
import { Link } from "react-router-dom";
import { api } from "../../lib/api";
import { formatarMoeda } from "../../lib/format";
import { useCrmEventos } from "../../lib/useCrmEventos";
import type { MeuAviso } from "../../lib/types";

const SOM = "/sounds/lembrete-adesao.mp3";

function lerVistos(chave: string): Set<string> {
  try {
    return new Set(JSON.parse(localStorage.getItem(chave) ?? "[]") as string[]);
  } catch {
    return new Set();
  }
}

function gravarVistos(chave: string, vistos: Set<string>) {
  try {
    localStorage.setItem(chave, JSON.stringify([...vistos].slice(-200)));
  } catch {
    /* sem storage: o aviso só reaparece ao recarregar */
  }
}

/**
 * Avisa o consultor, dentro do CRM, quando o financeiro ou a gestão manda um aviso de pagamento em aberto (o push avisa fora da aba).
 * Cada aviso aparece uma vez como notificação; ele continua no card "Avisos importantes" do Portal até ser resolvido.
 */
export function NotificacaoAvisosPagamento({ usuarioId }: { usuarioId: string }) {
  const [novos, setNovos] = useState<MeuAviso[]>([]);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const chave = `avisos-pagamento-vistos:${usuarioId}`;

  const tocar = useCallback(() => {
    audioRef.current ??= new Audio(SOM);
    const audio = audioRef.current;
    audio.currentTime = 0;
    audio.play().catch(() => {
      document.addEventListener("pointerdown", () => audio.play().catch(() => {}), { once: true });
    });
  }, []);

  const verificar = useCallback(() => {
    api
      .get<MeuAviso[]>("/crm/avisos-pagamento/meus")
      .then((lista) => {
        const vistos = lerVistos(chave);
        const inedidos = lista.filter((a) => !vistos.has(a.id) && !a.lidoEm);
        if (inedidos.length === 0) return;
        inedidos.forEach((a) => vistos.add(a.id));
        gravarVistos(chave, vistos);
        setNovos((atual) => {
          const ids = new Set(atual.map((a) => a.id));
          return [...inedidos.filter((a) => !ids.has(a.id)), ...atual].slice(0, 4);
        });
        tocar();
        if ("Notification" in window && Notification.permission === "granted") {
          new Notification(inedidos[0].titulo, { body: inedidos[0].mensagem, icon: "/logo-css.png" });
        }
      })
      .catch(() => {});
  }, [chave, tocar]);

  useEffect(() => {
    verificar();
  }, [verificar]);
  useCrmEventos(verificar, 500);

  if (novos.length === 0) return null;

  const fechar = (id: string) => setNovos((atual) => atual.filter((a) => a.id !== id));

  return (
    <div className="fixed right-4 top-36 z-[95] w-[calc(100vw-2rem)] max-w-sm space-y-2" role="alert">
      {novos.map((a) => (
        <div key={a.id} className="rounded-lg border border-[var(--warning)] bg-[var(--surface)] p-3 text-sm shadow-lg">
          <div className="flex items-start gap-2">
            <Wallet className="mt-0.5 size-4 shrink-0 text-[var(--warning)]" aria-hidden />
            <div className="min-w-0 flex-1">
              <p className="font-semibold text-[var(--fg)]">{a.titulo}</p>
              <p className="text-[var(--fg-muted)]">{a.mensagem}</p>
              {(a.valor != null || a.referencia) && (
                <p className="mt-1 text-xs text-[var(--fg-muted)]">
                  {a.valor != null && <span className="font-medium text-[var(--fg)]">{formatarMoeda(a.valor)}</span>}
                  {a.valor != null && a.referencia && " · "}
                  {a.referencia}
                </p>
              )}
              <Link to="/app/portal" onClick={() => fechar(a.id)} className="mt-2 inline-block text-xs font-medium text-[var(--brand)] hover:underline">
                Ver nos avisos importantes
              </Link>
            </div>
            <button
              type="button"
              aria-label="Fechar"
              className="cursor-pointer rounded p-0.5 text-[var(--fg-muted)] hover:text-[var(--fg)]"
              onClick={() => fechar(a.id)}
            >
              <X className="size-4" />
            </button>
          </div>
        </div>
      ))}
    </div>
  );
}
