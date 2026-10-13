import { useEffect, useState, type Dispatch, type SetStateAction } from "react";
import { useAuth } from "../context/AuthContext";

/** O valor guardado só vale se tiver o mesmo formato do valor inicial (texto, número, lista de textos...). */
function formaCompativel(valor: unknown, inicial: unknown): boolean {
  if (Array.isArray(inicial)) return Array.isArray(valor) && valor.every((v) => typeof v === "string");
  return typeof valor === typeof inicial;
}

function ler<T>(chave: string, inicial: T): T {
  try {
    const bruto = localStorage.getItem(chave);
    if (bruto === null) return inicial;
    const valor = JSON.parse(bruto);
    return formaCompativel(valor, inicial) ? (valor as T) : inicial;
  } catch {
    return inicial;
  }
}

/**
 * Igual a `useState`, mas o valor fica guardado no navegador (por usuário e por tela) e volta quando a pessoa reabre a tela, muda de aba, fecha a guia ou fecha o
 * sistema. Serve para os filtros: só saem quando alguém clica em "Limpar filtros" (que volta o estado ao valor inicial). Se o navegador não deixar guardar
 * (modo privado, armazenamento cheio), a tela funciona normalmente, só sem lembrar.
 */
export function useEstadoSalvo<T>(chave: string, inicial: T): [T, Dispatch<SetStateAction<T>>] {
  const { sessao } = useAuth();
  const chaveCompleta = `filtro:${sessao?.id ?? ""}:${chave}`;
  const [valor, setValor] = useState<T>(() => ler(chaveCompleta, inicial));

  useEffect(() => {
    try {
      localStorage.setItem(chaveCompleta, JSON.stringify(valor));
    } catch {
      // sem armazenamento: segue sem lembrar
    }
  }, [chaveCompleta, valor]);

  return [valor, setValor];
}
