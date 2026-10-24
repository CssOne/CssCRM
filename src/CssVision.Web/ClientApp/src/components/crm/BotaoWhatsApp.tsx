import { MessageCircle } from "lucide-react";
import { useAuth } from "../../context/AuthContext";
import { api } from "../../lib/api";
import { linkWhatsApp } from "../../lib/format";
import { mensagemWhatsApp } from "../../lib/mensagensWhatsApp";

/**
 * Atalho que abre a conversa do cliente no WhatsApp (app ou WhatsApp Web) a partir do telefone, já com uma mensagem pronta conforme a etapa.
 * Some quando o número não serve (sem DDD, fixo, curto). Com o id do lead, o clique também registra o contato (último contato + nota na linha do tempo).
 * Fica dentro de cartões clicáveis e arrastáveis, por isso o clique não "vaza" para o cartão.
 */
export function BotaoWhatsApp({
  telefone,
  nome,
  leadId,
  etapa,
  className = "",
}: {
  telefone?: string | null;
  nome: string;
  leadId?: string | null;
  etapa?: string | null;
  className?: string;
}) {
  const { sessao } = useAuth();
  const href = linkWhatsApp(telefone, mensagemWhatsApp(etapa, nome, sessao?.nomeCompleto));
  if (!href) return null;
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      draggable={false}
      title="Chamar no WhatsApp"
      aria-label={`Chamar ${nome} no WhatsApp`}
      onClick={(e) => {
        e.stopPropagation();
        // Registrar o contato nunca atrapalha abrir a conversa.
        if (leadId) api.post(`/crm/leads/${leadId}/contato`, { canal: "WhatsApp" }).catch(() => undefined);
      }}
      onMouseDown={(e) => e.stopPropagation()}
      className={`focus-ring ${className.includes("absolute") ? "" : "relative"} z-10 inline-flex shrink-0 cursor-pointer items-center justify-center rounded-full bg-[#25D366]/20 text-[#1fa855] transition hover:bg-[#25D366] hover:text-white ${className}`}
    >
      <MessageCircle className="size-3.5" aria-hidden />
    </a>
  );
}
