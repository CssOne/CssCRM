/** Texto comparável numa pesquisa: minúsculo e sem acento ("São Paulo" casa com "sao pau"). */
export function paraBusca(texto: string): string {
  return texto.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().trim();
}
