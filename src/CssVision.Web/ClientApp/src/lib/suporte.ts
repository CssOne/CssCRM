import { tempoDecorrido } from "./format";

/** Depois deste tempo sem resposta do suporte, o chamado fica em destaque (vermelho). */
export const LIMITE_SEM_RESPOSTA_HORAS = 24;

export const TAMANHO_MAXIMO_ANEXO = 5 * 1024 * 1024;
const EXTENSOES_ANEXO = /\.(png|jpe?g|webp|pdf)$/i;
const EXTENSOES_IMAGEM = /\.(png|jpe?g|webp)$/i;

export const ehImagem = (urlOuNome: string | null | undefined) => !!urlOuNome && EXTENSOES_IMAGEM.test(urlOuNome);

/** Mensagem de erro se o arquivo não pode ser anexado (mesmas regras do servidor); nulo se está ok. */
export function erroDoAnexo(arquivo: { name: string; size: number }): string | null {
  if (arquivo.size <= 0) return "O arquivo está vazio.";
  if (arquivo.size > TAMANHO_MAXIMO_ANEXO) return "O anexo deve ter no máximo 5 MB.";
  if (!EXTENSOES_ANEXO.test(arquivo.name)) return "Anexe uma imagem (PNG, JPG ou WEBP) ou um PDF.";
  return null;
}

/** Aviso "sem resposta há X" do chamado que espera o suporte; nulo se não está esperando. */
export function avisoDeEspera(aguardando: boolean, desde: string | null | undefined, atende: boolean, agora: number = Date.now()): { texto: string; urgente: boolean } | null {
  if (!aguardando || !desde) return null;
  const horas = (agora - new Date(desde).getTime()) / 3_600_000;
  const tempo = tempoDecorrido(desde, agora);
  return { texto: atende ? `Sem resposta há ${tempo}` : `Aguardando o suporte há ${tempo}`, urgente: horas >= LIMITE_SEM_RESPOSTA_HORAS };
}
