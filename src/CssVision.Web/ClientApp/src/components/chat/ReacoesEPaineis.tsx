import { Pin, X } from "lucide-react";
import type { DiscordChatMensagem, DiscordChatReacao } from "../../lib/types";
import { formatarDataHora } from "../../lib/format";

/** Reações mais usadas, à mão ao lado da mensagem. */
export const REACOES_RAPIDAS = ["👍", "❤️", "😂", "🎉", "😮", "🙏"];

/** Chave que o servidor usa para o emoji comum: o próprio caractere. As do servidor (personalizadas) já chegam como <c>nome:id</c>. */
export const chaveDoEmoji = (emoji: string) => emoji;

/** Reações de uma mensagem: cada emoji com a contagem; clicar reage (ou tira a sua). */
export function ReacoesDaMensagem({ reacoes, aoReagir }: { reacoes: DiscordChatReacao[]; aoReagir: (chave: string) => void }) {
  if (reacoes.length === 0) return null;
  return (
    <div className="mt-1 flex flex-wrap gap-1" role="group" aria-label="Reações">
      {reacoes.map((r) => (
        <button
          key={r.chave}
          type="button"
          onClick={() => aoReagir(r.chave)}
          aria-pressed={r.reagi}
          title={r.reagi ? "Tirar a minha reação" : "Reagir também"}
          className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs ${
            r.reagi ? "border-[var(--brand)] bg-[var(--brand)]/15 text-[var(--fg)]" : "border-[var(--border)] bg-[var(--bg)] text-[var(--fg-muted)] hover:bg-[var(--surface-hover)]"
          }`}
        >
          {r.url ? <img src={r.url} alt={r.texto} className="size-4" loading="lazy" /> : <span aria-hidden>{r.texto}</span>}
          <span className="sr-only">{r.url ? r.texto : `Reação ${r.texto}`}</span>
          {r.contagem}
        </button>
      ))}
    </div>
  );
}

/** Barrinha de reações rápidas (abre ao lado da mensagem). */
export function ReacoesRapidas({ aoEscolher }: { aoEscolher: (emoji: string) => void }) {
  return (
    <div role="menu" aria-label="Reagir" className="absolute bottom-full left-0 z-10 mb-1 flex gap-0.5 rounded-full border border-[var(--border)] bg-[var(--bg)] p-1 shadow-lg">
      {REACOES_RAPIDAS.map((e) => (
        <button key={e} type="button" role="menuitem" onClick={() => aoEscolher(chaveDoEmoji(e))} className="rounded-full p-1 text-lg leading-none hover:bg-[var(--surface-hover)]" aria-label={`Reagir com ${e}`}>
          {e}
        </button>
      ))}
    </div>
  );
}

/** Lista de mensagens achadas (busca) ou fixadas, acima da conversa. */
export function PainelDeMensagens({
  titulo,
  mensagens,
  carregando,
  vazio,
  aoFechar,
}: {
  titulo: string;
  mensagens: DiscordChatMensagem[] | null;
  carregando: boolean;
  vazio: string;
  aoFechar: () => void;
}) {
  return (
    <div className="max-h-56 overflow-y-auto border-b border-[var(--border)] bg-[var(--bg-muted)]/40 px-4 py-2" role="region" aria-label={titulo}>
      <div className="mb-1 flex items-center justify-between">
        <p className="flex items-center gap-1.5 text-xs font-semibold text-[var(--fg)]">
          {titulo.startsWith("Fixadas") && <Pin className="size-3.5" aria-hidden />} {titulo}
        </p>
        <button type="button" onClick={aoFechar} aria-label="Fechar" className="rounded p-0.5 hover:bg-[var(--bg-muted)]">
          <X className="size-3.5" />
        </button>
      </div>
      {carregando ? (
        <p className="py-2 text-xs text-[var(--fg-muted)]">Carregando…</p>
      ) : !mensagens || mensagens.length === 0 ? (
        <p className="py-2 text-xs text-[var(--fg-muted)]">{vazio}</p>
      ) : (
        <ul className="space-y-1.5">
          {mensagens.map((m) => (
            <li key={m.id} className="rounded-lg bg-[var(--bg)] px-2.5 py-1.5 text-xs">
              <p className="flex flex-wrap gap-x-2 text-[var(--fg-muted)]">
                <span className="font-semibold text-[var(--fg)]">{m.autorNome}</span>
                <span>{formatarDataHora(m.criadaEm)}</span>
              </p>
              <p className="whitespace-pre-wrap break-words text-[var(--fg)]">{m.conteudo.replace(/<a?:(\w+):\d+>/g, ":$1:") || (m.anexos.length > 0 ? "📎 anexo" : "")}</p>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
