import { Calendar, CalendarClock, CircleDollarSign, FileText, Headset, LayoutGrid, Mail, UserPlus, Users, Wallet, type LucideIcon } from "lucide-react";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../../lib/api";
import { ATALHOS_RAPIDOS, PAPEIS_CONTROLE_FINANCEIRO } from "../../lib/atalhosRapidos";
import { useAuth } from "../../context/AuthContext";
import { Card } from "../ui";

const ICONES: Record<string, LucideIcon> = {
  "novo-cliente": UserPlus,
  quadro: LayoutGrid,
  "disparar-email": Mail,
  adesoes: CircleDollarSign,
  propostas: FileText,
  agenda: Calendar,
  clientes: Users,
  financeiro: Wallet,
  suporte: Headset,
};

/** Cartão "Atalhos rápidos" do Portal: leva direto à ação (novo cliente com o formulário aberto, adesões pendentes, disparo de e-mail...). */
export function AtalhosRapidos() {
  const { temPapel } = useAuth();
  // Os atalhos do Controle financeiro só aparecem para quem vê essa tela.
  const atalhos = ATALHOS_RAPIDOS.filter((a) => !["adesoes", "financeiro"].includes(a.chave) || temPapel(...PAPEIS_CONTROLE_FINANCEIRO));
  // Adesões que vencem hoje ou já venceram, sem comprovante: o mesmo aviso do lembrete, aqui como contador no atalho.
  const [adesoesVencidas, setAdesoesVencidas] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    api
      .get<unknown[]>("/crm/opportunities/lembretes-adesao", controller.signal)
      .then((lista) => setAdesoesVencidas(Array.isArray(lista) ? lista.length : 0))
      .catch(() => setAdesoesVencidas(0));
    return () => controller.abort();
  }, []);

  return (
    <Card className="p-5">
      <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Atalhos rápidos</h2>
      <div className="grid grid-cols-3 gap-3">
        {atalhos.map((atalho) => {
          const Icone = ICONES[atalho.chave] ?? CalendarClock;
          const contador = atalho.chave === "adesoes" ? adesoesVencidas : 0;
          return (
            <Link
              key={atalho.chave}
              to={atalho.rota}
              title={atalho.dica}
              className="focus-ring relative flex flex-col items-center gap-2 rounded-xl border border-[var(--border)] p-3 text-center hover:bg-[var(--surface-hover)]"
            >
              <div className="flex size-9 items-center justify-center rounded-full bg-[var(--brand-soft)] text-[var(--brand)]">
                <Icone className="size-4" aria-hidden />
              </div>
              <span className="text-xs font-medium text-[var(--fg)]">{atalho.rotulo}</span>
              {contador > 0 && (
                <span
                  aria-label={`${contador} adesão(ões) vencida(s) ou vencendo hoje`}
                  className="absolute right-1.5 top-1.5 flex min-w-5 items-center justify-center rounded-full bg-[var(--danger)] px-1.5 text-[11px] font-bold leading-5 text-white"
                >
                  {contador}
                </span>
              )}
            </Link>
          );
        })}
      </div>
    </Card>
  );
}
