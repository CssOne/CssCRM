import { mesmaTag } from "../../lib/opcoesLead";

/**
 * Seleção única em formato de tags (como o select do Notion): clicar marca, clicar de novo na tag
 * marcada limpa. Um valor atual fora da lista (ex.: origem automática "Meta ads") aparece como tag
 * extra, para não ser apagado sem querer.
 */
export function TagSelect({
  id,
  opcoes,
  valor,
  onChange,
  rotulo,
}: {
  id?: string;
  opcoes: string[];
  valor: string;
  onChange: (valor: string) => void;
  rotulo: string;
}) {
  const extras = valor && !opcoes.some((o) => mesmaTag(o, valor)) ? [valor] : [];

  return (
    <div id={id} role="radiogroup" aria-label={rotulo} className="flex flex-wrap gap-1.5 pt-1">
      {[...opcoes, ...extras].map((opcao) => {
        const selecionada = mesmaTag(opcao, valor);
        return (
          <button
            key={opcao}
            type="button"
            role="radio"
            aria-checked={selecionada}
            onClick={() => onChange(selecionada ? "" : opcao)}
            className={`focus-ring rounded-full border px-2.5 py-0.5 text-xs font-medium transition-colors ${
              selecionada
                ? "border-[var(--brand)] bg-[var(--brand)] text-white"
                : "border-[var(--border)] bg-[var(--surface)] text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
            }`}
          >
            {opcao}
          </button>
        );
      })}
    </div>
  );
}
