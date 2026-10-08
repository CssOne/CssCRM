import { useCallback, useEffect, useState } from "react";
import { useAuth } from "../../context/AuthContext";
import { api } from "../../lib/api";
import type { Regional } from "../../lib/types";
import { Select } from "../ui";

const CHAVE = "crm-painel-regional";
const TODAS = "todas";

function lerEscolha(): string | null {
  try {
    return localStorage.getItem(CHAVE);
  } catch {
    return null;
  }
}

/**
 * Regional mostrada no painel (Portal do Consultor e Visão geral). Só quem tem visão total (administrador e gestor master) pode escolher; os demais
 * já veem a própria regional.
 *
 * Qual regional abre:
 * 1. a que a pessoa escolheu na caixa (fica guardada neste navegador), se ela ainda existir entre as que ela enxerga;
 * 2. senão, se ela enxerga **uma única** regional (as outras estão ocultas no cadastro dela, em Usuários → "Ocultar dados das regionais"), essa;
 * 3. senão, todas as que ela enxerga (<c>regionalId</c> vazio).
 *
 * <c>pronto</c> só fica verdadeiro quando já se sabe quais regionais ela enxerga: o painel espera isso para carregar uma vez só, já na regional certa.
 */
export function useRegionalDoPainel() {
  const { temPapel } = useAuth();
  const podeFiltrar = temPapel("Admin", "GestorMaster");
  const [regionais, setRegionais] = useState<Regional[] | null>(null);
  const [escolhida, setEscolhida] = useState<string | null>(lerEscolha);

  useEffect(() => {
    if (!podeFiltrar) return;
    let ativo = true;
    // A lista já vem sem as regionais que o administrador ocultou no cadastro dele.
    api
      .get<Regional[]>("/crm/settings/regionals")
      .then((lista) => ativo && setRegionais(lista.filter((r) => r.ativa)))
      .catch(() => ativo && setRegionais([]));
    return () => {
      ativo = false;
    };
  }, [podeFiltrar]);

  const escolher = useCallback((id: string) => {
    const valor = id || TODAS; // "Todas as regionais" é uma escolha também: não volta sozinho para a regional única
    setEscolhida(valor);
    try {
      localStorage.setItem(CHAVE, valor);
    } catch {
      // navegador sem armazenamento: vale só até recarregar
    }
  }, []);

  let regionalId = "";
  if (podeFiltrar && regionais) {
    if (escolhida === TODAS) regionalId = "";
    else if (escolhida && regionais.some((r) => r.id === escolhida)) regionalId = escolhida;
    else if (regionais.length === 1) regionalId = regionais[0].id;
  }

  return { podeFiltrar, regionais: regionais ?? [], regionalId, escolher, pronto: !podeFiltrar || regionais !== null };
}

/**
 * Caixa "Regional" do painel. Com duas ou mais regionais visíveis mostra "Todas as regionais" + cada uma; com uma só (as outras ocultas), só
 * informa qual é, porque não há o que escolher.
 */
export function FiltroRegionalDoPainel({ regionais, valor, aoEscolher }: { regionais: Regional[]; valor: string; aoEscolher: (id: string) => void }) {
  if (regionais.length === 0) return null;

  if (regionais.length === 1) {
    return (
      <div className="w-52">
        <p className="mb-1 text-xs font-medium text-[var(--fg-muted)]">Regional</p>
        <p className="rounded-lg border border-[var(--border)] px-3 py-2 text-sm text-[var(--fg)]">{regionais[0].nome}</p>
      </div>
    );
  }

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
