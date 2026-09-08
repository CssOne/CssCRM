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

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    credentials: "include",
    headers: {
      ...(typeof init?.body === "string" ? { "Content-Type": "application/json" } : {}),
      ...init?.headers,
    },
    ...init,
  });

  if (response.status === 204) {
    return undefined as T;
  }

  const isJson = response.headers.get("content-type")?.includes("application/json");
  const payload = isJson ? await response.json() : undefined;

  if (!response.ok) {
    const erro: ApiError = isJson ? payload : { mensagem: `Erro inesperado (${response.status}).` };
    throw new ApiRequestError(response.status, erro);
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

export async function uploadFile<T>(path: string, file: File): Promise<T> {
  const formData = new FormData();
  formData.append("arquivo", file);
  return request<T>(path, { method: "POST", body: formData });
}

export function downloadUrl(path: string, params: Record<string, unknown> = {}): string {
  return `/api${path}${toQueryString(params)}`;
}
