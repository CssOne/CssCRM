import { useState } from "react";
import type { DiscordChatExtras } from "../../lib/types";

/** Emojis comuns, por categoria. Os do servidor e as figurinhas vêm do Discord (ver DiscordChatExtras). */
const CATEGORIAS: { id: string; rotulo: string; icone: string; emojis: string[] }[] = [
  {
    id: "rostos", rotulo: "Rostos", icone: "😀",
    emojis: ["😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣", "🙂", "😉", "😊", "😇", "🥰", "😍", "🤩", "😘", "😋", "😛", "😜", "🤪", "🤗", "🤔", "🤨", "😐", "😑", "😶", "🙄", "😏", "😌", "😴", "🤤", "😎", "🤓", "🥳", "😕", "😟", "🙁", "😮", "😲", "😳", "🥺", "😢", "😭", "😤", "😠", "😡", "🤯", "😱", "😰", "🥵", "🥶", "🤒", "🤕", "🤧", "🤫", "🤐"],
  },
  {
    id: "gestos", rotulo: "Gestos", icone: "👍",
    emojis: ["👍", "👎", "👌", "✌️", "🤞", "🤟", "🤘", "🤙", "👈", "👉", "👆", "👇", "☝️", "✋", "🤚", "🖐️", "👋", "🤝", "🙏", "👏", "🙌", "👐", "💪", "🫶", "🫡", "✍️", "🤳"],
  },
  {
    id: "coracoes", rotulo: "Corações e símbolos", icone: "❤️",
    emojis: ["❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍", "🤎", "💔", "❣️", "💕", "💞", "💓", "💗", "💖", "💘", "💝", "💯", "✅", "❌", "⭕", "❗", "❓", "‼️", "⚠️", "🚫", "➕", "➖", "✔️", "🔥", "⭐", "🌟", "✨", "⚡", "💥", "💫", "🎯"],
  },
  {
    id: "celebracao", rotulo: "Comemoração", icone: "🎉",
    emojis: ["🎉", "🎊", "🥳", "🎈", "🎁", "🏆", "🥇", "🥈", "🥉", "🏅", "🎖️", "👑", "🍾", "🥂", "🍻", "🎂", "🍰", "🎵", "🎶", "📣", "📢", "🔔", "🚀", "💰", "💵", "💸", "💎", "📈", "📊", "🤑"],
  },
  {
    id: "objetos", rotulo: "Trabalho e objetos", icone: "📱",
    emojis: ["📱", "☎️", "📞", "💻", "🖥️", "⌨️", "🖱️", "📧", "✉️", "📩", "📝", "📋", "📌", "📍", "📎", "📅", "🗓️", "⏰", "⏳", "🕐", "🔍", "🔒", "🔓", "🔑", "🛠️", "⚙️", "📦", "🚗", "🚙", "🚚", "🏍️", "🛵", "🔧", "🧾", "📷", "🎥"],
  },
  {
    id: "natureza", rotulo: "Natureza e comida", icone: "🌎",
    emojis: ["☀️", "🌤️", "⛅", "🌧️", "⛈️", "🌈", "❄️", "🌙", "🌎", "🌳", "🌴", "🌵", "🌹", "🌻", "🐶", "🐱", "🦁", "🐯", "🐴", "🦄", "🐝", "🦋", "☕", "🍺", "🍕", "🍔", "🍟", "🌭", "🍎", "🍌", "🍇", "🍉", "🍓", "🥗", "🍫", "🍩"],
  },
];

type Aba = "emojis" | "servidor" | "figurinhas";

/**
 * Seletor do chat: emojis comuns (inserem o próprio caractere), emojis personalizados do servidor (viram <c>&lt;:nome:id&gt;</c>, que o Discord e a tela
 * mostram como imagem) e as figurinhas do servidor (enviam na hora).
 */
export function SeletorDeEmojis({
  extras,
  carregando,
  aoEscolherEmoji,
  aoEscolherEmojiDoServidor,
  aoEscolherFigurinha,
}: {
  extras: DiscordChatExtras | null;
  carregando: boolean;
  aoEscolherEmoji: (emoji: string) => void;
  aoEscolherEmojiDoServidor: (marca: string) => void;
  aoEscolherFigurinha: (id: string) => void;
}) {
  const [aba, setAba] = useState<Aba>("emojis");
  const [categoria, setCategoria] = useState(CATEGORIAS[0].id);
  const atual = CATEGORIAS.find((c) => c.id === categoria) ?? CATEGORIAS[0];

  const abas: { id: Aba; rotulo: string }[] = [
    { id: "emojis", rotulo: "Emojis" },
    { id: "servidor", rotulo: `Servidor${extras ? ` (${extras.emojis.length})` : ""}` },
    { id: "figurinhas", rotulo: `Figurinhas${extras ? ` (${extras.figurinhas.length})` : ""}` },
  ];

  return (
    <div role="dialog" aria-label="Emojis e figurinhas" className="absolute bottom-full left-0 z-20 mb-2 w-80 overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--bg)] shadow-xl">
      <div role="tablist" className="flex border-b border-[var(--border)] text-xs">
        {abas.map((a) => (
          <button
            key={a.id}
            type="button"
            role="tab"
            aria-selected={aba === a.id}
            onClick={() => setAba(a.id)}
            className={`flex-1 px-2 py-2 font-medium ${aba === a.id ? "border-b-2 border-[var(--brand)] text-[var(--brand)]" : "text-[var(--fg-muted)] hover:text-[var(--fg)]"}`}
          >
            {a.rotulo}
          </button>
        ))}
      </div>

      {aba === "emojis" && (
        <>
          <div className="flex gap-1 border-b border-[var(--border)] px-2 py-1">
            {CATEGORIAS.map((c) => (
              <button
                key={c.id}
                type="button"
                title={c.rotulo}
                aria-label={c.rotulo}
                aria-pressed={categoria === c.id}
                onClick={() => setCategoria(c.id)}
                className={`rounded p-1 text-base ${categoria === c.id ? "bg-[var(--brand-soft)]" : "hover:bg-[var(--surface-hover)]"}`}
              >
                {c.icone}
              </button>
            ))}
          </div>
          <div className="grid max-h-56 grid-cols-8 gap-0.5 overflow-y-auto p-2">
            {atual.emojis.map((e) => (
              <button key={e} type="button" onClick={() => aoEscolherEmoji(e)} className="rounded p-1 text-xl hover:bg-[var(--surface-hover)]" aria-label={e}>
                {e}
              </button>
            ))}
          </div>
        </>
      )}

      {aba === "servidor" && (
        <div className="max-h-64 overflow-y-auto p-2">
          {carregando && !extras ? (
            <p className="p-3 text-center text-xs text-[var(--fg-muted)]">Carregando…</p>
          ) : !extras || extras.emojis.length === 0 ? (
            <p className="p-3 text-center text-xs text-[var(--fg-muted)]">O servidor ainda não tem emojis personalizados. Quem administra o Discord pode adicioná-los em Configurações do servidor → Emoji.</p>
          ) : (
            <div className="grid grid-cols-7 gap-1">
              {extras.emojis.map((e) => (
                <button
                  key={e.id}
                  type="button"
                  title={`:${e.nome}:`}
                  onClick={() => aoEscolherEmojiDoServidor(`<${e.animado ? "a" : ""}:${e.nome}:${e.id}>`)}
                  className="rounded p-1 hover:bg-[var(--surface-hover)]"
                >
                  <img src={e.url} alt={`:${e.nome}:`} loading="lazy" className="size-7 object-contain" />
                </button>
              ))}
            </div>
          )}
        </div>
      )}

      {aba === "figurinhas" && (
        <div className="max-h-64 overflow-y-auto p-2">
          {carregando && !extras ? (
            <p className="p-3 text-center text-xs text-[var(--fg-muted)]">Carregando…</p>
          ) : !extras || extras.figurinhas.length === 0 ? (
            <p className="p-3 text-center text-xs text-[var(--fg-muted)]">O servidor ainda não tem figurinhas. Quem administra o Discord pode adicioná-las em Configurações do servidor → Figurinhas.</p>
          ) : (
            <div className="grid grid-cols-3 gap-2">
              {extras.figurinhas.map((f) => (
                <button key={f.id} type="button" title={f.nome} onClick={() => aoEscolherFigurinha(f.id)} className="rounded-lg p-1 hover:bg-[var(--surface-hover)]">
                  <img src={f.url} alt={f.nome} loading="lazy" className="aspect-square w-full object-contain" />
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
