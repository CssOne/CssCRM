import { useEffect, useId, useMemo, useRef, useState } from "react";
import { paraBusca } from "../lib/busca";
import { Input } from "./ui";

type Cidade = { nome: string; uf: string; busca: string };
let cidadesCarregadas: Cidade[] | null = null;
let carregando: Promise<Cidade[]> | null = null;

/** Carrega a lista de municípios sob demanda (são ~90 KB; só entram no navegador quando alguém digita uma cidade). */
function carregarCidades(): Promise<Cidade[]> {
  if (cidadesCarregadas) return Promise.resolve(cidadesCarregadas);
  carregando ??= import("../lib/cidades").then(({ CIDADES_POR_UF }) => {
    cidadesCarregadas = Object.entries(CIDADES_POR_UF).flatMap(([uf, nomes]) => nomes.map((nome) => ({ nome, uf, busca: paraBusca(nome) })));
    return cidadesCarregadas;
  });
  return carregando;
}

const MAXIMO_SUGESTOES = 8;

/**
 * Campo de cidade com sugestões: ao digitar, lista os municípios que combinam (sem diferenciar acento nem maiúscula) e, ao escolher um, devolve
 * a cidade e o estado dela para o formulário preencher o campo Estado sozinho. Dá para digitar uma cidade fora da lista normalmente.
 * Com um estado já escolhido, as cidades dele aparecem primeiro.
 */
export function CidadeInput({
  value,
  onChange,
  onEscolher,
  estado,
  id,
  required,
}: {
  value: string;
  onChange: (cidade: string) => void;
  /** Chamado ao escolher uma sugestão (cidade e UF), para preencher o estado. */
  onEscolher: (cidade: string, uf: string) => void;
  /** UF já escolhida no formulário (as cidades dela vêm primeiro). */
  estado?: string;
  id?: string;
  required?: boolean;
}) {
  const [cidades, setCidades] = useState<Cidade[]>(cidadesCarregadas ?? []);
  const [aberto, setAberto] = useState(false);
  const [destaque, setDestaque] = useState(0);
  const raizRef = useRef<HTMLDivElement>(null);
  const listaId = useId();

  useEffect(() => {
    if (!aberto || cidades.length > 0) return;
    let ativo = true;
    void carregarCidades().then((c) => ativo && setCidades(c));
    return () => {
      ativo = false;
    };
  }, [aberto, cidades.length]);

  useEffect(() => {
    if (!aberto) return;
    const fora = (e: MouseEvent) => {
      if (!raizRef.current?.contains(e.target as Node)) setAberto(false);
    };
    document.addEventListener("mousedown", fora);
    return () => document.removeEventListener("mousedown", fora);
  }, [aberto]);

  const sugestoes = useMemo(() => {
    const termo = paraBusca(value);
    if (termo.length < 2) return [];
    const comecam: Cidade[] = [];
    const contem: Cidade[] = [];
    for (const c of cidades) {
      if (c.busca.startsWith(termo)) comecam.push(c);
      else if (c.busca.includes(termo)) contem.push(c);
    }
    const doEstado = (c: Cidade) => (estado && c.uf === estado ? 0 : 1);
    return [...comecam, ...contem].sort((a, b) => doEstado(a) - doEstado(b)).slice(0, MAXIMO_SUGESTOES);
  }, [value, cidades, estado]);

  function escolher(c: Cidade) {
    onEscolher(c.nome, c.uf);
    setAberto(false);
  }

  return (
    <div ref={raizRef} className="relative">
      <Input
        id={id}
        value={value}
        required={required}
        autoComplete="off"
        role="combobox"
        aria-expanded={aberto && sugestoes.length > 0}
        aria-controls={listaId}
        aria-autocomplete="list"
        onChange={(e) => {
          onChange(e.target.value);
          setAberto(true);
          setDestaque(0);
        }}
        onFocus={() => setAberto(true)}
        onKeyDown={(e) => {
          if (!aberto || sugestoes.length === 0) return;
          if (e.key === "ArrowDown") {
            e.preventDefault();
            setDestaque((d) => Math.min(d + 1, sugestoes.length - 1));
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            setDestaque((d) => Math.max(d - 1, 0));
          } else if (e.key === "Enter") {
            e.preventDefault();
            escolher(sugestoes[Math.min(destaque, sugestoes.length - 1)]);
          } else if (e.key === "Escape") {
            e.stopPropagation();
            setAberto(false);
          }
        }}
      />
      {aberto && sugestoes.length > 0 && (
        <ul
          id={listaId}
          role="listbox"
          className="absolute left-0 z-40 mt-1 max-h-64 w-full min-w-48 overflow-y-auto rounded-lg border border-[var(--border)] bg-[var(--surface)] p-1 shadow-lg"
        >
          {sugestoes.map((c, i) => (
            <li key={`${c.uf}-${c.nome}`} role="option" aria-selected={i === destaque}>
              <button
                type="button"
                tabIndex={-1}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => escolher(c)}
                onMouseEnter={() => setDestaque(i)}
                className={`flex w-full cursor-pointer items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm text-[var(--fg)] ${
                  i === destaque ? "bg-[var(--surface-hover)]" : ""
                }`}
              >
                <span className="truncate">{c.nome}</span>
                <span className="shrink-0 text-xs text-[var(--fg-muted)]">{c.uf}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
