import { api } from "./api";

/**
 * Notificações push do navegador (aviso de lead novo aparece no Windows/celular mesmo com o CRM
 * fechado, enquanto o navegador estiver aberto). O service worker é o public/sw-push.js.
 */
export type EstadoPush = "nao-suportado" | "bloqueado" | "ativo" | "inativo";

const SW = "/sw-push.js";

export function pushSuportado() {
  return typeof window !== "undefined" && "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;
}

/** true quando este navegador já recebe o push — aí a tela não mostra a notificação dela de novo. */
export let pushAtivoNesteNavegador = false;

function base64UrlParaBytes(base64: string) {
  const preenchido = (base64 + "=".repeat((4 - (base64.length % 4)) % 4)).replace(/-/g, "+").replace(/_/g, "/");
  const bruto = atob(preenchido);
  return Uint8Array.from(bruto, (c) => c.charCodeAt(0));
}

async function inscricaoAtual() {
  const registro = await navigator.serviceWorker.getRegistration(SW);
  return registro ? registro.pushManager.getSubscription() : null;
}

export async function estadoPush(): Promise<EstadoPush> {
  if (!pushSuportado()) return "nao-suportado";
  if (Notification.permission === "denied") return "bloqueado";
  if (Notification.permission !== "granted") return "inativo";
  const inscricao = await inscricaoAtual();
  pushAtivoNesteNavegador = !!inscricao;
  return inscricao ? "ativo" : "inativo";
}

async function enviarInscricao(inscricao: PushSubscription) {
  const json = inscricao.toJSON();
  await api.post("/crm/push/inscricao", {
    endpoint: inscricao.endpoint,
    p256dh: json.keys?.p256dh ?? "",
    auth: json.keys?.auth ?? "",
    navegador: navigator.userAgent,
  });
}

/** Pede a permissão, inscreve este navegador e manda uma notificação de teste. */
export async function ativarPush(): Promise<EstadoPush> {
  if (!pushSuportado()) return "nao-suportado";
  const permissao = await Notification.requestPermission();
  if (permissao !== "granted") return permissao === "denied" ? "bloqueado" : "inativo";

  const registro = await navigator.serviceWorker.register(SW);
  await navigator.serviceWorker.ready;
  const { chave } = await api.get<{ chave: string }>("/crm/push/chave-publica");
  const inscricao =
    (await registro.pushManager.getSubscription()) ??
    (await registro.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64UrlParaBytes(chave) }));
  await enviarInscricao(inscricao);
  pushAtivoNesteNavegador = true;
  await api.post("/crm/push/teste", {}).catch(() => {});
  return "ativo";
}

export async function desativarPush() {
  const inscricao = pushSuportado() ? await inscricaoAtual() : null;
  if (inscricao) {
    await api.post("/crm/push/inscricao/remover", { endpoint: inscricao.endpoint }).catch(() => {});
    await inscricao.unsubscribe();
  }
  pushAtivoNesteNavegador = false;
}

/**
 * Ao entrar no CRM: se este navegador já estava inscrito, reenvia a inscrição (ela passa a ser de
 * quem está logado agora e se recupera caso o servidor a tenha perdido).
 */
export async function sincronizarPush() {
  if ((await estadoPush()) !== "ativo") return;
  const inscricao = await inscricaoAtual();
  if (inscricao) await enviarInscricao(inscricao).catch(() => {});
}
