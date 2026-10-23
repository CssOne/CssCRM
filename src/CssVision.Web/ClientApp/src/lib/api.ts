import type { ApiError } from "./types";

export class ApiRequestError extends Error {
  status: number;
  codigo?: string | null;
  detalhes?: unknown;

  constructor(status: number, erro: ApiError) {
    super(erro.mensagem);
    this.status = status;
    this.codigo = erro.codigo;
    this.detalhes = erro.detalhes;
  }
}

/** true quando o usuário está autenticado mas não tem permissão — trate diferente de 401 (não autenticado). */
export function isForbidden(erro: unknown): erro is ApiRequestError {
  return erro instanceof ApiRequestError && erro.status === 403;
}

export function isUnauthorized(erro: unknown): erro is ApiRequestError {
  return erro instanceof ApiRequestError && erro.status === 401;
}

export function isConflict(erro: unknown): erro is ApiRequestError {
  return erro instanceof ApiRequestError && (erro.status === 409 || erro.status === 400);
}

/** true quando o erro veio de um AbortController (ex: StrictMode desmontando o efeito) — nunca deve virar mensagem de erro na tela. */
export function isAbortError(erro: unknown): boolean {
  return erro instanceof DOMException && erro.name === "AbortError";
}

function mensagemPorStatus(status: number): string {
  switch (status) {
    case 400: return "Os dados enviados são inválidos. Confira os campos e tente novamente.";
    case 401: return "Sua sessão expirou. Entre novamente.";
    case 403: return "Você não tem permissão para fazer isso.";
    case 404: return "Registro não encontrado. Ele pode ter sido removido: atualize a página.";
    case 409: return "Este registro foi alterado por outra pessoa. Atualize a página e tente novamente.";
    case 413: return "O arquivo é grande demais.";
    case 429: return "Muitas tentativas seguidas. Aguarde um instante e tente novamente.";
    default: return status >= 500 ? "O servidor não conseguiu concluir a operação. Tente novamente em instantes." : `Erro inesperado (${status}).`;
  }
}

/** Traduz a resposta de erro da API (mensagem do CRM, erros de validação do ASP.NET ou corpo vazio) numa mensagem específica. */
function erroDaResposta(status: number, payload: unknown): ApiError {
  const p = payload as { mensagem?: string; codigo?: string | null; detalhes?: unknown; errors?: Record<string, string[]>; title?: string } | undefined;
  if (p?.mensagem) return p as ApiError;
  if (p?.errors) {
    const msgs = Object.values(p.errors).flat().filter(Boolean);
    if (msgs.length > 0) return { mensagem: msgs.join(" "), codigo: "validacao", detalhes: p.errors };
  }
  return { mensagem: mensagemPorStatus(status), codigo: p?.codigo ?? null };
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      credentials: "include",
      headers: {
        ...(typeof init?.body === "string" ? { "Content-Type": "application/json" } : {}),
        ...init?.headers,
      },
      ...init,
    });
  } catch (erro) {
    // Alguns navegadores degradam um fetch cancelado (AbortController) para um TypeError genérico
    // "Failed to fetch" em vez do DOMException "AbortError" padrão — normaliza aqui para que
    // isAbortError() reconheça de forma confiável em todas as páginas que usam esse padrão.
    if (init?.signal?.aborted) {
      throw new DOMException("Aborted", "AbortError");
    }
    throw new ApiRequestError(0, { mensagem: "Sem conexão com o servidor. Verifique sua internet e tente novamente.", codigo: "sem_conexao" });
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const isJson = response.headers.get("content-type")?.includes("application/json");
  const payload = isJson ? await response.json() : undefined;

  if (!response.ok) {
    throw new ApiRequestError(response.status, erroDaResposta(response.status, isJson ? payload : undefined));
  }

  return payload as T;
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: "GET", signal }),
  post: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: "POST", body: body !== undefined ? JSON.stringify(body) : undefined, signal }),
  put: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: "PUT", body: body !== undefined ? JSON.stringify(body) : undefined, signal }),
  del: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: "DELETE", signal }),
};

/** Monta uma query string a partir de um objeto, ignorando valores vazios/undefined/null. */
export function toQueryString(params: Record<string, unknown>): string {
  const search = new URLSearchParams();
  for (const [chave, valor] of Object.entries(params)) {
    if (valor === undefined || valor === null || valor === "") continue;
    if (Array.isArray(valor)) {
      valor.forEach((v) => search.append(chave, String(v)));
    } else {
      search.append(chave, String(valor));
    }
  }
  const texto = search.toString();
  return texto ? `?${texto}` : "";
}

export async function uploadFile<T>(path: string, file: File | null, campos: Record<string, string> = {}): Promise<T> {
  const formData = new FormData();
  if (file) formData.append("arquivo", file);
  for (const [chave, valor] of Object.entries(campos)) formData.append(chave, valor);
  return request<T>(path, { method: "POST", body: formData });
}

export function downloadUrl(path: string, params: Record<string, unknown> = {}): string {
  return `/api${path}${toQueryString(params)}`;
}
