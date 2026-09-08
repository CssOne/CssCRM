import { Search } from "lucide-react";
import { useEffect, useState } from "react";
import { api, toQueryString } from "../../lib/api";
import type { LeadListItem, PagedResult } from "../../lib/types";
import { Input } from "../ui";

export function LeadPicker({ onSelecionar }: { onSelecionar: (lead: LeadListItem) => void }) {
  const [busca, setBusca] = useState("");
  const [resultados, setResultados] = useState<LeadListItem[]>([]);
  const [buscando, setBuscando] = useState(false);

  useEffect(() => {
    if (busca.trim().length < 2) {
      setResultados([]);
      return;
    }
    const controller = new AbortController();
    setBuscando(true);
    const timeout = setTimeout(() => {
      api
        .get<PagedResult<LeadListItem>>(`/crm/leads${toQueryString({ busca, tamanhoPagina: 8 })}`, controller.signal)
        .then((res) => setResultados(res.itens))
        .finally(() => setBuscando(false));
    }, 300);
    return () => {
      clearTimeout(timeout);
      controller.abort();
    };
  }, [busca]);

  return (
    <div>
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" />
        <Input className="pl-9" placeholder="Buscar lead por nome, telefone ou e-mail" value={busca} onChange={(e) => setBusca(e.target.value)} />
      </div>
      {busca.trim().length >= 2 && (
        <div className="mt-2 max-h-48 overflow-y-auto rounded-lg border border-[var(--border)]">
          {buscando ? (
            <p className="p-3 text-sm text-[var(--fg-muted)]">Buscando...</p>
          ) : resultados.length === 0 ? (
            <p className="p-3 text-sm text-[var(--fg-muted)]">Nenhum lead encontrado.</p>
          ) : (
            resultados.map((lead) => (
              <button
                key={lead.id}
                type="button"
                onClick={() => onSelecionar(lead)}
                className="focus-ring block w-full px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)]"
              >
                <span className="font-medium text-[var(--fg)]">{lead.nomeOuRazaoSocial}</span>
                <span className="ml-2 text-xs text-[var(--fg-muted)]">{lead.email ?? lead.telefone}</span>
              </button>
            ))
          )}
        </div>
      )}
    </div>
  );
}
