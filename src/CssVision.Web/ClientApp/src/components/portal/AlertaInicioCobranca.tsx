import { useCallback, useEffect, useState } from "react";
import { CalendarClock, X } from "lucide-react";
import { Link } from "react-router-dom";
import { api } from "../../lib/api";
import { formatarMoeda } from "../../lib/format";
import { Button, Select, useToast } from "../ui";

interface Alerta {
  consultorId: string;
  consultorNome: string;
  parametro: string;
  rotulo: string;
  valor: number;
  inicioCobranca: string;
  diasRestantes: number;
  carenciaMeses: number;
  podeEditar: boolean;
}

const MESES = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];
const mesAno = (iso: string) => `${MESES[Number(iso.slice(5, 7)) - 1]}/${iso.slice(0, 4)}`;
const chaveDe = (a: Alerta) => `${a.consultorId}:${a.parametro}:${a.inicioCobranca}`;
const hoje = () => new Date().toISOString().slice(0, 10);

function lerFechados(usuarioId: string): Record<string, string> {
  try {
    return JSON.parse(localStorage.getItem(`alerta-cobranca-fechado:${usuarioId}`) ?? "{}") as Record<string, string>;
  } catch {
    return {};
  }
}

/**
 * Avisa o consultor, o financeiro, os gestores e os administradores de que um custo fixo (tráfego pago, custo de lead...) está para começar a
 * ser cobrado. Quem gere pode estender o prazo ou deixar começar; o consultor só vê. Fechar esconde o aviso só por hoje.
 */
export function AlertaInicioCobranca({ usuarioId }: { usuarioId: string }) {
  const { notificar } = useToast();
  const [alertas, setAlertas] = useState<Alerta[]>([]);
  const [fechados, setFechados] = useState<Record<string, string>>(() => lerFechados(usuarioId));
  const [ocupado, setOcupado] = useState<string | null>(null);
  const [extensao, setExtensao] = useState<Record<string, string>>({});

  const carregar = useCallback(() => {
    api.get<Alerta[]>("/portal/financeiro/contrato/alertas").then(setAlertas).catch(() => {});
  }, []);

  useEffect(() => {
    carregar();
    const t = window.setInterval(carregar, 10 * 60 * 1000);
    window.addEventListener("contrato-financeiro-mudou", carregar);
    return () => {
      window.clearInterval(t);
      window.removeEventListener("contrato-financeiro-mudou", carregar);
    };
  }, [carregar]);

  const visiveis = alertas.filter((a) => fechados[chaveDe(a)] !== hoje()).slice(0, 3);
  if (visiveis.length === 0) return null;

  const fechar = (a: Alerta) => {
    const proximos = { ...fechados, [chaveDe(a)]: hoje() };
    setFechados(proximos);
    try {
      localStorage.setItem(`alerta-cobranca-fechado:${usuarioId}`, JSON.stringify(proximos));
    } catch {
      /* sem storage: o aviso volta ao recarregar */
    }
  };

  async function agir(a: Alerta, acao: () => Promise<unknown>, ok: string) {
    setOcupado(chaveDe(a));
    try {
      await acao();
      notificar("success", ok);
      carregar();
      window.dispatchEvent(new Event("contrato-financeiro-mudou"));
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar.");
    } finally {
      setOcupado(null);
    }
  }

  return (
    <div className="fixed bottom-4 right-4 z-[94] w-[calc(100vw-2rem)] max-w-sm space-y-2" role="alert">
      {visiveis.map((a) => {
        const k = chaveDe(a);
        const estender = Number(extensao[k] ?? "1");
        return (
          <div key={k} className="rounded-2xl border border-[var(--warning)] bg-[var(--surface-solid)] p-3 text-sm shadow-lg">
            <div className="flex items-start gap-2">
              <CalendarClock className="mt-0.5 size-4 shrink-0 text-[var(--warning)]" aria-hidden />
              <div className="min-w-0 flex-1">
                <p className="font-semibold text-[var(--fg)]">
                  {a.rotulo} começa a ser cobrado {a.diasRestantes === 1 ? "amanhã" : `em ${a.diasRestantes} dias`}
                </p>
                <p className="text-[var(--fg-muted)]">
                  {a.podeEditar ? <strong className="text-[var(--fg)]">{a.consultorNome}</strong> : "Você"} passa a pagar{" "}
                  <strong className="text-[var(--fg)]">{formatarMoeda(a.valor)}</strong> {a.parametro === "CustoLead" ? "por lead" : "por mês"} a partir de {mesAno(a.inicioCobranca)}.
                </p>
                {a.podeEditar ? (
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <Button size="sm" variant="secondary" loading={ocupado === k} onClick={() => agir(a, () => api.post(`/portal/financeiro/contrato/${a.consultorId}/prazos/${a.parametro}/confirmar`), "Pronto: a cobrança começa na data prevista.")}>
                      Deixar começar
                    </Button>
                    <span className="flex items-center gap-1">
                      <Select aria-label="Estender o prazo" value={extensao[k] ?? "1"} onChange={(e) => setExtensao((x) => ({ ...x, [k]: e.target.value }))} className="!h-8 !w-auto !py-0 text-xs">
                        {[1, 2, 3, 6].map((m) => <option key={m} value={m}>+{m} {m === 1 ? "mês" : "meses"}</option>)}
                      </Select>
                      <Button size="sm" loading={ocupado === k} onClick={() => agir(a, () => api.put(`/portal/financeiro/contrato/${a.consultorId}/prazos`, { parametro: a.parametro, carenciaMeses: a.carenciaMeses + estender }), "Prazo estendido.")}>
                        Estender
                      </Button>
                    </span>
                  </div>
                ) : (
                  <Link to="/app/portal/controle-financeiro" onClick={() => fechar(a)} className="mt-2 inline-block text-xs font-medium text-[var(--brand)] hover:underline">
                    Ver meu contrato
                  </Link>
                )}
              </div>
              <button type="button" aria-label="Fechar aviso" className="cursor-pointer rounded p-0.5 text-[var(--fg-muted)] hover:text-[var(--fg)]" onClick={() => fechar(a)}>
                <X className="size-4" />
              </button>
            </div>
          </div>
        );
      })}
    </div>
  );
}
