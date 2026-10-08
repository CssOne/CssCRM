import { useCallback, useEffect, useState } from "react";
import { useAuth } from "../../context/AuthContext";
import { api } from "../../lib/api";
import type { Regional } from "../../lib/types";
import { Select } from "../ui";

const CHAVE = "crm-painel-regional";

/**
 * Regional escolhida no painel (Portal do Consultor e Visão geral). Só quem tem visão total (administrador e gestor master) pode escolher; os demais
 * já veem a própria regional. A escolha fica guardada neste navegador, para não precisar escolher de novo a cada visita.
 * <c>regionalId</c> vazio = todas as regionais.
 */
export function useRegionalDoPainel() {
  const { temPapel } = useAuth();
  const podeFiltrar = temPapel("Admin", "GestorMaster");
  const [escolhida, setEscolhida] = useState<string>(() => {
    try {
      return localStorage.getItem(CHAVE) ?? "";
    } catch {
      return "";
    }
  });

  const escolher = useCallback((id: string) => {
    setEscolhida(id);
    try {
      if (id) localStorage.setItem(CHAVE, id);
      else localStorage.removeItem(CHAVE);
    } catch {
      // navegador sem armazenamento: vale só até recarregar
    }
  }, []);

  return { podeFiltrar, regionalId: podeFiltrar ? escolhida : "", escolher };
}

/** Caixa "Regional" do painel: "Todas as regionais" ou uma das regionais ativas. */
export function FiltroRegionalDoPainel({ valor, aoEscolher }: { valor: string; aoEscolher: (id: string) => void }) {
  const [regionais, setRegionais] = useState<Regional[]>([]);

  useEffect(() => {
    api.get<Regional[]>("/crm/settings/regionals").then((lista) => setRegionais(lista.filter((r) => r.ativa))).catch(() => setRegionais([]));
  }, []);

  return (
    <div className="w-52">
      <label htmlFor="painel-regional" className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">
        Regional
      </label>
      <Select id="painel-regional" value={valor} onChange={(e) => aoEscolher(e.target.value)}>
        <option value="">Todas as regionais</option>
        {regionais.map((r) => (
          <option key={r.id} value={r.id}>
            {r.nome}
          </option>
        ))}
      </Select>
    </div>
  );
}
