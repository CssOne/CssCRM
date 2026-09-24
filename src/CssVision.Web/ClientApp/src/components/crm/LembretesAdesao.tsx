import { useCallback, useEffect, useRef, useState } from "react";
import { BellRing, X } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { api } from "../../lib/api";
import { formatarMoeda } from "../../lib/format";
import type { LembreteAdesao } from "../../lib/types";

const INTERVALO_MS = 5 * 60_000;
const SOM = "/sounds/lembrete-adesao.mp3";

function hojeLocal() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** "2026-09-24" → "24/09/2026" sem passar por Date (que converteria de UTC e poderia voltar um dia). */
function formatarDia(data: string) {
  const [a, m, d] = data.slice(0, 10).split("-");
  return `${d}/${m}/${a}`;
}

// Chaves no localStorage, por dia: o toque só soa uma vez por venda por dia, e "Dispensar" esconde
// o aviso até o dia seguinte (se o comprovante ainda não tiver sido anexado, ele volta).
const chaveTocado = (id: string) => `lembrete-adesao-tocado:${id}:${hojeLocal()}`;
const chaveDispensado = (id: string) => `lembrete-adesao-dispensado:${id}:${hojeLocal()}`;

function lerMarca(chave: string) {
  try {
    return localStorage.getItem(chave) === "1";
  } catch {
    return false;
  }
}

function gravarMarca(chave: string) {
  try {
    localStorage.setItem(chave, "1");
  } catch {
    /* sem storage: o lembrete só toca de novo na próxima verificação */
  }
}

/**
 * Lembrete de pagamento da adesão: quando chega a data marcada no "Concluir venda" (venda feita sem
 * o comprovante), o consultor responsável vê este aviso em qualquer tela e ouve o toque. Some quando
 * o comprovante é anexado (ou é dispensado até o dia seguinte).
 */
export function LembretesAdesao() {
  const navigate = useNavigate();
  const [lembretes, setLembretes] = useState<LembreteAdesao[]>([]);
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const tocar = useCallback(() => {
    audioRef.current ??= new Audio(SOM);
    const audio = audioRef.current;
    audio.currentTime = 0;
    audio.play().catch(() => {
      // Navegador bloqueou o som (página aberta sem nenhum clique ainda): toca no primeiro clique.
      const aoInteragir = () => {
        audio.play().catch(() => {});
      };
      document.addEventListener("pointerdown", aoInteragir, { once: true });
    });
  }, []);

  const verificar = useCallback(
    (signal?: AbortSignal) => {
      api
        .get<LembreteAdesao[]>("/crm/opportunities/lembretes-adesao", signal)
        .then((todos) => {
          const visiveis = todos.filter((l) => !lerMarca(chaveDispensado(l.opportunityId)));
          setLembretes(visiveis);

          const novos = visiveis.filter((l) => !lerMarca(chaveTocado(l.opportunityId)));
          if (novos.length === 0) return;
          novos.forEach((l) => gravarMarca(chaveTocado(l.opportunityId)));
          tocar();
          if ("Notification" in window && Notification.permission === "granted") {
            new Notification("Lembrete: pagamento da adesão", {
              body: novos.map((l) => l.leadNome).join(", "),
              icon: "/logo-css.png",
            });
          }
        })
        .catch(() => {});
    },
    [tocar]
  );

  useEffect(() => {
    const controller = new AbortController();
    verificar(controller.signal);
    const intervalo = setInterval(() => verificar(), INTERVALO_MS);
    const aoVoltar = () => {
      if (document.visibilityState === "visible") verificar();
    };
    document.addEventListener("visibilitychange", aoVoltar);
    return () => {
      controller.abort();
      clearInterval(intervalo);
      document.removeEventListener("visibilitychange", aoVoltar);
    };
  }, [verificar]);

  if (lembretes.length === 0) return null;

  function dispensar(id: string) {
    gravarMarca(chaveDispensado(id));
    setLembretes((atual) => atual.filter((l) => l.opportunityId !== id));
  }

  const podePedirNotificacao = "Notification" in window && Notification.permission === "default";

  return (
    <div className="fixed bottom-4 left-4 z-[90] w-[calc(100vw-2rem)] max-w-sm space-y-2" role="alert">
      {lembretes.map((l) => {
        const atrasado = l.dataPagamentoAdesaoPrevista.slice(0, 10) < hojeLocal();
        return (
          <div
            key={l.opportunityId}
            className="rounded-lg border border-[var(--warning)] bg-[var(--surface)] p-3 text-sm shadow-lg"
          >
            <div className="flex items-start gap-2">
              <BellRing className="mt-0.5 size-4 shrink-0 text-[var(--warning)]" aria-hidden />
              <div className="min-w-0 flex-1">
                <p className="font-semibold text-[var(--fg)]">Pagamento da adesão {atrasado ? "atrasado" : "hoje"}</p>
                <p className="truncate text-[var(--fg-muted)]">
                  {l.leadNome}
                  {l.pagamentoAdesao != null && ` · ${formatarMoeda(l.pagamentoAdesao)}`}
                  {atrasado && ` · desde ${formatarDia(l.dataPagamentoAdesaoPrevista)}`}
                </p>
                <p className="mt-1 text-xs text-[var(--fg-muted)]">Anexe o comprovante na venda para encerrar o lembrete.</p>
                <div className="mt-2 flex gap-3 text-xs font-medium">
                  <button
                    type="button"
                    className="cursor-pointer text-[var(--brand)] hover:underline"
                    onClick={() => navigate(`/app/crm/leads/${l.leadId}`)}
                  >
                    Abrir venda
                  </button>
                  {podePedirNotificacao && (
                    <button
                      type="button"
                      className="cursor-pointer text-[var(--fg-muted)] hover:underline"
                      onClick={() => Notification.requestPermission().then(() => setLembretes((a) => [...a]))}
                    >
                      Avisar também fora da aba
                    </button>
                  )}
                </div>
              </div>
              <button
                type="button"
                aria-label="Dispensar até amanhã"
                title="Dispensar até amanhã"
                className="cursor-pointer rounded p-0.5 text-[var(--fg-muted)] hover:text-[var(--fg)]"
                onClick={() => dispensar(l.opportunityId)}
              >
                <X className="size-4" />
              </button>
            </div>
          </div>
        );
      })}
    </div>
  );
}
