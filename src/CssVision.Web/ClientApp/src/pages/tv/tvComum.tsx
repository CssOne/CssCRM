import { useState } from "react";

/** Formatadores e a foto redonda, usados pelo painel da TV e pelas janelas de detalhe. */
export const moeda = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });
export const moedaExata = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", minimumFractionDigits: 2, maximumFractionDigits: 2 });
export const diaBrasilia = new Intl.DateTimeFormat("en-CA", { timeZone: "America/Sao_Paulo", year: "numeric", month: "2-digit", day: "2-digit" });
export const dataBr = new Intl.DateTimeFormat("pt-BR", { timeZone: "America/Sao_Paulo", day: "2-digit", month: "2-digit", year: "numeric" });
export const diaMesBr = new Intl.DateTimeFormat("pt-BR", { timeZone: "America/Sao_Paulo", day: "2-digit", month: "2-digit" });
export const horaBr = new Intl.DateTimeFormat("pt-BR", { timeZone: "America/Sao_Paulo", hour: "2-digit", minute: "2-digit" });

export const iniciais = (nome: string) => nome.split(" ").filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join("");

export function Foto({ nome, url, grande = false }: { nome: string; url?: string | null; grande?: boolean }) {
  const [falhou, setFalhou] = useState<string>();
  const mostrar = Boolean(url && falhou !== url);
  return (
    <div className={`avatar ${grande ? "avatar-large" : ""}`} title={mostrar ? nome : `${nome} · foto indisponível`} aria-label={nome}>
      {mostrar ? <img src={url!} alt={nome} width={grande ? 90 : 35} height={grande ? 90 : 35} referrerPolicy="no-referrer" onError={() => setFalhou(url ?? undefined)} /> : <span aria-hidden="true">{iniciais(nome)}</span>}
    </div>
  );
}
