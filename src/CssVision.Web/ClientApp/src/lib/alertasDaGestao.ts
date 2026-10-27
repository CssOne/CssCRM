import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "./api";
import type { AlertaDistribuicao, EsperaTrafegoPainel } from "./types";
import { useCrmEventos } from "./useCrmEventos";

/**
 * Alertas que o servidor também manda por push para administradores, gestores e supervisores: leads do tráfego pago parados (limite ou horário) e
 * consultor que demora com frequência para pegar lead. Aqui eles viram itens do sino de notificações e tocam um som quando aparecem.
 */
export interface AlertaDaGestao {
  id: string;
  titulo: string;
  texto: string;
  /** Para onde o item leva ao clicar. */
  url: string;
  gravidade: "alerta" | "perigo";
}

const SOM = "/sounds/alerta-gestao.mp3";
let audio: HTMLAudioElement | null = null;
let ultimoSomEm = 0;

/** Toca o toque dos alertas da gestão. Sem interação prévia o navegador pode barrar: nesse caso toca no próximo clique. Não repete em menos de 8 s. */
export function tocarAlertaGestao() {
  const agora = Date.now();
  if (agora - ultimoSomEm < 8000) return;
  ultimoSomEm = agora;
  audio ??= new Audio(SOM);
  const a = audio;
  a.currentTime = 0;
  a.play().catch(() => {
    document.addEventListener("pointerdown", () => a.play().catch(() => {}), { once: true });
  });
}

/** Monta os alertas a partir do estado da distribuição e do painel de espera dos leads do tráfego pago. */
export function montarAlertas(distribuicao: AlertaDistribuicao | null, espera: EsperaTrafegoPainel | null): AlertaDaGestao[] {
  const alertas: AlertaDaGestao[] = [];
  if (distribuicao?.bloqueada) {
    const porLimite = distribuicao.leadsBloqueadosPorLimite > 0;
    const porHorario = distribuicao.leadsBloqueadosPorHorario > 0;
    const motivo = porLimite && porHorario ? "limite atingido e consultores fora do horário" : porHorario ? "consultores fora do horário de recebimento" : "limite de leads atingido";
    alertas.push({
      id: `distribuicao:${distribuicao.leadsBloqueados}`,
      titulo: "Leads parados na distribuição",
      texto: `${distribuicao.leadsBloqueados} lead(s) do tráfego pago sem responsável — ${motivo}.`,
      url: "/app/crm/gestao",
      gravidade: "perigo",
    });
  }
  for (const c of espera?.consultores ?? []) {
    if (!c.reincidente) continue;
    alertas.push({
      id: `demora:${c.consultorId}:${c.passaramDoPrazoForte}`,
      titulo: "Consultor demora para pegar leads",
      texto: `${c.nome}${c.regionalNome ? ` (${c.regionalNome})` : ""}: ${c.passaramDoPrazoForte} lead(s) acima de ${espera!.minutosPrazoForte >= 60 ? `${espera!.minutosPrazoForte / 60} h` : `${espera!.minutosPrazoForte} min`} sem atendimento em ${espera!.dias} dias.`,
      url: "/app/crm/gestao",
      gravidade: "alerta",
    });
  }
  return alertas;
}

/**
 * Os alertas da gestão do usuário (só se `ativo`): atualiza de minuto em minuto e quando o quadro muda, toca o som quando surge alerta novo (a primeira
 * leitura não toca, para não soar a cada abertura do CRM) e também quando chega o push desses alertas com o CRM aberto (mensagem do service worker).
 */
export function useAlertasDaGestao(ativo: boolean): AlertaDaGestao[] {
  const [alertas, setAlertas] = useState<AlertaDaGestao[]>([]);
  const conhecidos = useRef<Set<string> | null>(null);

  const carregar = useCallback(() => {
    if (!ativo) return;
    Promise.all([
      api.get<AlertaDistribuicao>("/crm/management/alerta-distribuicao").catch(() => null),
      api.get<EsperaTrafegoPainel>("/crm/management/espera-trafego").catch(() => null),
    ]).then(([distribuicao, espera]) => {
      const lista = montarAlertas(distribuicao, espera);
      const ids = new Set(lista.map((a) => a.id));
      if (conhecidos.current !== null && lista.some((a) => !conhecidos.current!.has(a.id))) tocarAlertaGestao();
      conhecidos.current = ids;
      setAlertas(lista);
    });
  }, [ativo]);

  useEffect(() => {
    carregar();
  }, [carregar]);
  useCrmEventos(carregar, 500);

  useEffect(() => {
    if (!ativo || !("serviceWorker" in navigator)) return;
    const aoReceber = (e: MessageEvent) => {
      if (e.data?.tipo !== "alerta-gestao") return;
      tocarAlertaGestao();
      carregar();
    };
    navigator.serviceWorker.addEventListener("message", aoReceber);
    return () => navigator.serviceWorker.removeEventListener("message", aoReceber);
  }, [ativo, carregar]);

  return alertas;
}
