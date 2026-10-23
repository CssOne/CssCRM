import { useEffect, useSyncExternalStore } from "react";
import { api } from "./api";

/*
 * Fotos de perfil dos usuários por id, baixadas uma vez por aba (GET /crm/usuarios/fotos) e compartilhadas por todos os
 * círculos de avatar: onde a tela só tem o id/nome do consultor, o Avatar troca as iniciais pela foto quando ela existe.
 */
let fotos: Record<string, string> = {};
let carregou = false;
let carregando = false;
const ouvintes = new Set<() => void>();

function carregar() {
  if (carregou || carregando) return;
  carregando = true;
  api
    .get<Record<string, string>>("/crm/usuarios/fotos")
    .then((r) => {
      fotos = r;
      carregou = true;
      ouvintes.forEach((o) => o());
    })
    .catch(() => {
      // Sem permissão ou fora do ar: segue com as iniciais; tenta de novo na próxima vez que um avatar aparecer.
    })
    .finally(() => {
      carregando = false;
    });
}

const assinar = (o: () => void) => {
  ouvintes.add(o);
  return () => ouvintes.delete(o);
};

/** Foto de perfil do usuário (ou undefined enquanto não carregou / se ele não tem foto). */
export function useFotoDoUsuario(usuarioId?: string | null): string | undefined {
  useEffect(() => {
    if (usuarioId) carregar();
  }, [usuarioId]);
  const mapa = useSyncExternalStore(
    assinar,
    () => fotos,
    () => fotos
  );
  return usuarioId ? mapa[usuarioId] : undefined;
}
