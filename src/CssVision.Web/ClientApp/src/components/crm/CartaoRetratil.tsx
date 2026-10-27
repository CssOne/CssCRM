import { ChevronRight, type LucideIcon } from "lucide-react";
import { useState, type HTMLAttributes, type ReactNode } from "react";
import { useEstadoSalvo } from "../../lib/useEstadoSalvo";
import { Badge, Card } from "../ui";

/**
 * Card de lista que recolhe como o "Leads esperando atendimento": o cabeçalho abre e fecha o card (animado, escolha guardada neste navegador) e a lista mostra só
 * uma parte, com o botão "Ver mais" para abrir o resto.
 */
export function CartaoRetratil<T>({
  chave, icone: Icone, titulo, descricao, total, itens, parte = 3, vazio, chaveDoItem, renderItem, cardProps,
}: {
  /** Identifica o card para guardar se ficou aberto ou recolhido. */
  chave: string;
  icone: LucideIcon;
  titulo: string;
  descricao?: string;
  /** Número mostrado no selo do cabeçalho (se omitido, usa o tamanho da lista). */
  total?: number;
  itens: T[];
  /** Quantos itens aparecem antes do "Ver mais". */
  parte?: number;
  vazio: string;
  chaveDoItem: (item: T) => string;
  renderItem: (item: T) => ReactNode;
  cardProps?: HTMLAttributes<HTMLDivElement>;
}) {
  const [aberto, setAberto] = useEstadoSalvo(`visao:${chave}-aberto`, true);
  const [verTudo, setVerTudo] = useState(false);
  const inicio = itens.slice(0, parte);
  const resto = itens.slice(parte);
  const numero = total ?? itens.length;

  return (
    <Card {...cardProps} className="!rounded-2xl p-3">
      <button
        type="button"
        aria-expanded={aberto}
        onClick={() => setAberto(!aberto)}
        className="flex w-full flex-wrap items-center justify-between gap-x-3 gap-y-1.5 text-left"
      >
        <span className="flex items-center gap-2.5">
          <span className="flex size-7 items-center justify-center rounded-lg bg-[var(--surface-hover)] text-[var(--fg-muted)]">
            <Icone className="size-3.5" aria-hidden />
          </span>
          <span className="text-sm font-bold leading-tight tracking-tight text-[var(--fg)]">{titulo}</span>
        </span>
        <span className="flex items-center gap-1.5">
          <Badge variant={numero > 0 ? "warning" : "success"}>{numero.toLocaleString("pt-BR")}</Badge>
          <ChevronRight className={`size-4 text-[var(--fg-muted)] transition-transform ${aberto ? "rotate-90" : ""}`} aria-hidden />
        </span>
      </button>

      {/* Abre/fecha deslizando: a altura anima de 0fr a 1fr; fechado, o conteúdo sai do foco e da leitura por teclado/leitor de tela. */}
      <div
        className={`grid transition-[grid-template-rows,opacity] duration-300 ease-out motion-reduce:transition-none ${aberto ? "grid-rows-[1fr] opacity-100" : "grid-rows-[0fr] opacity-0"}`}
        aria-hidden={!aberto}
        inert={!aberto}
      >
        <div className="min-h-0 overflow-hidden">
          {descricao && <p className="mt-2 text-xs text-[var(--fg-muted)]">{descricao}</p>}
          {itens.length === 0 ? (
            <p className="mt-3 rounded-xl bg-[var(--surface-hover)] px-3 py-3 text-sm text-[var(--fg-muted)]">{vazio}</p>
          ) : (
            <>
              <ul className="mt-3 space-y-2">
                {inicio.map((item) => (
                  <li key={chaveDoItem(item)}>{renderItem(item)}</li>
                ))}
              </ul>
              {resto.length > 0 && (
                <>
                  {/* O resto da lista desliza ao clicar em "Ver mais". */}
                  <div
                    className={`grid transition-[grid-template-rows,opacity] duration-300 ease-out motion-reduce:transition-none ${verTudo ? "grid-rows-[1fr] opacity-100" : "grid-rows-[0fr] opacity-0"}`}
                    aria-hidden={!verTudo}
                    inert={!verTudo}
                  >
                    <ul className="min-h-0 space-y-2 overflow-hidden">
                      {resto.map((item, i) => (
                        <li key={chaveDoItem(item)} className={i === 0 ? "pt-2" : ""}>
                          {renderItem(item)}
                        </li>
                      ))}
                    </ul>
                  </div>
                  <button
                    type="button"
                    aria-expanded={verTudo}
                    onClick={() => setVerTudo(!verTudo)}
                    className="focus-ring mt-2 w-full cursor-pointer rounded-xl px-3 py-1.5 text-center text-xs font-medium text-[var(--brand)] transition hover:bg-[var(--surface-hover)]"
                  >
                    {verTudo ? "Ver menos" : `Ver mais (${resto.length})`}
                  </button>
                </>
              )}
            </>
          )}
        </div>
      </div>
    </Card>
  );
}
