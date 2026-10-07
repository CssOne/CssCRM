/**
 * Avisos de mensagem nova do chat enquanto o CRM está aberto: um "plim" e, se a pessoa permitir, uma notificação do navegador (só quando a aba
 * está em segundo plano). As escolhas ficam neste navegador (localStorage) — são preferências de cada pessoa, em cada aparelho.
 */
const CHAVE_SOM = "crm-chat-som";
const CHAVE_NOTIFICACAO = "crm-chat-notificacao";

function ler(chave: string, padrao: boolean): boolean {
  try {
    const v = localStorage.getItem(chave);
    return v === null ? padrao : v === "1";
  } catch {
    return padrao;
  }
}

function gravar(chave: string, valor: boolean) {
  try {
    localStorage.setItem(chave, valor ? "1" : "0");
  } catch {
    // navegador sem armazenamento: a escolha vale só até recarregar
  }
}

export const somLigado = () => ler(CHAVE_SOM, true);
export const definirSom = (ligado: boolean) => gravar(CHAVE_SOM, ligado);

/** Notificação do navegador: precisa estar escolhida E permitida pelo navegador. */
export const notificacaoLigada = () => ler(CHAVE_NOTIFICACAO, false) && typeof Notification !== "undefined" && Notification.permission === "granted";
export const notificacaoSuportada = () => typeof Notification !== "undefined";

/** Liga pedindo permissão ao navegador (precisa vir de um clique). Devolve se ficou ligada. */
export async function ligarNotificacao(): Promise<boolean> {
  if (!notificacaoSuportada()) return false;
  const permissao = Notification.permission === "default" ? await Notification.requestPermission() : Notification.permission;
  const ok = permissao === "granted";
  gravar(CHAVE_NOTIFICACAO, ok);
  return ok;
}

export const desligarNotificacao = () => gravar(CHAVE_NOTIFICACAO, false);

let contexto: AudioContext | null = null;

/** Dois toques curtos e suaves. Se o navegador não deixar tocar som sem interação, simplesmente não toca. */
export function tocarPlim() {
  try {
    contexto ??= new AudioContext();
    const ctx = contexto;
    if (ctx.state === "suspended") void ctx.resume();
    const agora = ctx.currentTime;
    [660, 880].forEach((frequencia, i) => {
      const osc = ctx.createOscillator();
      const ganho = ctx.createGain();
      osc.type = "sine";
      osc.frequency.value = frequencia;
      ganho.gain.setValueAtTime(0.0001, agora + i * 0.14);
      ganho.gain.exponentialRampToValueAtTime(0.12, agora + i * 0.14 + 0.02);
      ganho.gain.exponentialRampToValueAtTime(0.0001, agora + i * 0.14 + 0.2);
      osc.connect(ganho).connect(ctx.destination);
      osc.start(agora + i * 0.14);
      osc.stop(agora + i * 0.14 + 0.22);
    });
  } catch {
    // sem áudio disponível
  }
}

/** Mostra a notificação do navegador (só com a aba em segundo plano). Clicar leva ao chat. */
export function mostrarNotificacao(novas: number, aoClicar: () => void) {
  if (!notificacaoLigada() || document.visibilityState === "visible") return;
  try {
    const n = new Notification("CSS Brasil · Chat", {
      body: novas === 1 ? "Você tem 1 mensagem nova." : `Você tem ${novas} mensagens novas.`,
      tag: "crm-chat", // várias mensagens seguidas trocam a mesma notificação, não empilham
    });
    n.onclick = () => {
      window.focus();
      aoClicar();
      n.close();
    };
  } catch {
    // alguns navegadores móveis só permitem notificação pelo service worker
  }
}
