import { useEffect, useMemo, useState } from "react";

/**
 * Paginação de uma lista que já está toda na tela (feita no navegador). Volta para a primeira
 * página quando a lista muda de tamanho (ex.: busca ou filtro) e nunca fica numa página que não
 * existe mais. Usar com o componente <Pagination> de ui.tsx.
 */
export function usePaginacao<T>(itens: readonly T[] | null | undefined, porPagina: number) {
  const lista = itens ?? [];
  const [pagina, setPagina] = useState(1);
  const totalPaginas = Math.max(1, Math.ceil(lista.length / porPagina));

  useEffect(() => {
    setPagina(1);
  }, [lista.length]);

  const paginaValida = Math.min(pagina, totalPaginas);
  const itensDaPagina = useMemo(
    () => lista.slice((paginaValida - 1) * porPagina, paginaValida * porPagina),
    [lista, paginaValida, porPagina]
  );

  return { pagina: paginaValida, setPagina, totalPaginas, itensDaPagina, total: lista.length };
}
