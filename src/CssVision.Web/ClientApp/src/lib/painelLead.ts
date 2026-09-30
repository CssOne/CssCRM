import { useCallback } from "react";
import { useSearchParams } from "react-router-dom";

/**
 * Painel lateral do lead (como a página lateral do Notion): abrir um lead não troca de tela —
 * põe ?lead=<id> no endereço e o PainelLead (no Shell) mostra o lead à direita. Fica no endereço
 * para sobreviver a um recarregamento e para o "voltar" do navegador fechar o painel.
 */
export const PARAM_PAINEL_LEAD = "lead";

export function useAbrirLead() {
  const [, setParams] = useSearchParams();
  return useCallback(
    (leadId: string) =>
      setParams((atual) => {
        const novo = new URLSearchParams(atual);
        novo.set(PARAM_PAINEL_LEAD, leadId);
        return novo;
      }),
    [setParams]
  );
}

export function useFecharPainelLead() {
  const [, setParams] = useSearchParams();
  return useCallback(
    () =>
      setParams((atual) => {
        const novo = new URLSearchParams(atual);
        novo.delete(PARAM_PAINEL_LEAD);
        return novo;
      }),
    [setParams]
  );
}
