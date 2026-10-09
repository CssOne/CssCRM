import { Wallet } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { useAuth } from "../../context/AuthContext";
import { api, isAbortError } from "../../lib/api";
import { formatarDataHora, formatarMoeda } from "../../lib/format";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import type { MeuAviso } from "../../lib/types";
import { Card } from "../ui";

/**
 * "Avisos importantes" de pagamento em aberto, na página inicial do consultor. Só aparece para quem é consultor e tem aviso aberto;
 * some sozinho quando o financeiro/gestão resolve. O mesmo aviso chega por push e, para quem vinculou a conta, no Discord.
 */
export function PagamentosEmAberto() {
  const { temPapel } = useAuth();
  const ehConsultor = temPapel("Comercial");
  const [avisos, setAvisos] = useState<MeuAviso[]>([]);
  const [recarregar, setRecarregar] = useState(0);

  useEffect(() => {
    if (!ehConsultor) return;
    const controller = new AbortController();
    api
      .get<MeuAviso[]>("/crm/avisos-pagamento/meus", controller.signal)
      .then(setAvisos)
      .catch((e) => {
        if (!isAbortError(e)) setAvisos([]);
      });
    return () => controller.abort();
  }, [ehConsultor, recarregar]);

  useAtualizarAoVivo(useCallback(() => setRecarregar((n) => n + 1), []));

  if (!ehConsultor || avisos.length === 0) return null;

  return (
    <Card className="border-[var(--warning)] bg-[var(--warning-soft)] p-4">
      <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold text-[var(--fg)]">
        <Wallet className="size-4 text-[var(--warning)]" />
        Avisos importantes · pagamentos em aberto
      </h2>
      <ul className="space-y-2" aria-label="Pagamentos em aberto">
        {avisos.map((p) => (
          <li key={p.id} className="rounded-lg border border-[var(--border)] bg-[var(--surface)] p-3">
            <p className="text-sm font-semibold text-[var(--fg)]">{p.titulo}</p>
            <p className="whitespace-pre-wrap break-words text-xs text-[var(--fg)]">{p.mensagem}</p>
            {(p.valor != null || p.referencia) && (
              <p className="mt-1 text-xs text-[var(--fg-muted)]">
                {p.valor != null && <span className="font-medium text-[var(--fg)]">{formatarMoeda(p.valor)}</span>}
                {p.valor != null && p.referencia && " · "}
                {p.referencia}
              </p>
            )}
            <p className="mt-1 text-xs text-[var(--fg-muted)]">
              {p.enviadoPorNome} · {formatarDataHora(p.criadoEm)}
            </p>
            {!p.lidoEm && (
              <button
                type="button"
                className="mt-1 cursor-pointer text-xs font-medium text-[var(--brand)] hover:underline"
                onClick={() => {
                  api.put(`/crm/avisos-pagamento/meus/${p.id}/ciente`, {}).then(() => setRecarregar((n) => n + 1)).catch(() => {});
                }}
              >
                Estou ciente
              </button>
            )}
          </li>
        ))}
      </ul>
    </Card>
  );
}
