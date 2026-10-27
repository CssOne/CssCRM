import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle } from "lucide-react";
import { api } from "../../lib/api";
import { useCrmEventos } from "../../lib/useCrmEventos";
import type { AlertaDistribuicao } from "../../lib/types";
import { useToast } from "../ui";

/**
 * Aviso para administradores, gestores e supervisores: todos os consultores aptos bateram o limite
 * diário/mensal e ainda há leads do tráfego pago sem responsável. O gestor decide continuar a
 * distribuição mesmo com o limite (vale até o fim do dia) ou deixar os leads parados. O servidor
 * também manda a mesma notificação por push (AlertaDistribuicaoService).
 */
export function AlertaDistribuicaoLimites() {
  const { notificar } = useToast();
  const [estado, setEstado] = useState<AlertaDistribuicao | null>(null);
  const [dispensadoEm, setDispensadoEm] = useState<number | null>(null);
  const [enviando, setEnviando] = useState(false);

  const verificar = useCallback(() => {
    api.get<AlertaDistribuicao>("/crm/management/alerta-distribuicao").then(setEstado).catch(() => {});
  }, []);

  useEffect(() => {
    verificar();
    // A conexão em tempo real só avisa mudanças no quadro: confere também de minuto em minuto.
    const intervalo = setInterval(() => {
      if (document.visibilityState === "visible") verificar();
    }, 60_000);
    return () => clearInterval(intervalo);
  }, [verificar]);
  useCrmEventos(verificar, 500);

  async function continuar(valor: boolean) {
    setEnviando(true);
    try {
      const novo = await api.put<AlertaDistribuicao>("/crm/management/continuar-distribuicao", { continuar: valor });
      setEstado(novo);
      notificar(
        "success",
        valor ? "Distribuição liberada até o fim do dia, ignorando os limites e, se ninguém estiver no horário, também o horário dos consultores." : "Os limites e horários voltaram a valer na distribuição."
      );
    } catch {
      notificar("error", "Não foi possível alterar a distribuição.");
    } finally {
      setEnviando(false);
    }
  }

  if (!estado) return null;

  const bannerContinuar = estado.continuarAteEm ? (
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm">
        <span className="text-[var(--fg-muted)]">
          A distribuição automática está liberada até o fim de hoje: ignora os limites de leads e, quando ninguém está no dia/horário de recebimento, também o horário.
        </span>
        <button
          type="button"
          disabled={enviando}
          onClick={() => continuar(false)}
          className="focus-ring cursor-pointer font-medium text-[var(--brand)] hover:underline disabled:opacity-50"
        >
          Voltar a respeitar limites e horários
        </button>
      </div>
  ) : null;

  if (!estado.bloqueada || (dispensadoEm !== null && dispensadoEm === estado.leadsBloqueados)) return bannerContinuar;

  const porLimite = estado.leadsBloqueadosPorLimite > 0;
  const porHorario = estado.leadsBloqueadosPorHorario > 0;

  return (
    <>
    {bannerContinuar}
    <div role="alert" className="mb-4 rounded-lg border border-[var(--warning)] bg-[var(--surface)] p-3 text-sm">
      <div className="flex items-start gap-2">
        <AlertTriangle className="mt-0.5 size-4 shrink-0 text-[var(--warning)]" aria-hidden />
        <div className="min-w-0 flex-1">
          <p className="font-semibold text-[var(--fg)]">
            {porLimite && porHorario
              ? "Leads parados: limite atingido e consultores fora do horário"
              : porHorario
                ? "Todos os consultores estão fora do horário de recebimento"
                : "Todos os consultores atingiram o limite de leads"}
          </p>
          <p className="text-[var(--fg-muted)]">
            {estado.leadsBloqueados} lead(s) do tráfego pago estão sem responsável.
            {porLimite && (
              <>
                {" "}
                {estado.noLimiteDiario} de {estado.consultores} consultor(es) no limite do dia e {estado.noLimiteMensal} no do mês.
              </>
            )}
            {porHorario && <> {estado.consultoresForaDoHorario} consultor(es) fora do dia/horário de recebimento agora.</>}{" "}
            {porLimite
              ? "Deseja continuar a distribuição automática mesmo com os limites atingidos?"
              : "Eles serão entregues sozinhos quando um consultor entrar no dia/horário de recebimento. Se não quiser esperar, continue a distribuição hoje: ela passa por cima do horário."}
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-3 text-xs font-medium">
            {!estado.continuarAteEm && (
              <button
                type="button"
                disabled={enviando}
                onClick={() => continuar(true)}
                className="focus-ring cursor-pointer rounded-md bg-[var(--brand)] px-3 py-1.5 text-white disabled:opacity-50"
              >
                Continuar distribuindo hoje
              </button>
            )}
            <button
              type="button"
              onClick={() => setDispensadoEm(estado.leadsBloqueados)}
              className="focus-ring cursor-pointer text-[var(--fg-muted)] hover:underline"
            >
              {estado.continuarAteEm ? "Dispensar aviso" : "Manter parado"}
            </button>
            <Link to="/app/crm/gestao" className="text-[var(--brand)] hover:underline">
              Ajustar limites e horários na Gestão comercial
            </Link>
          </div>
        </div>
      </div>
    </div>
    </>
  );
}
