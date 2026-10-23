import { MessageCircle } from "lucide-react";
import { linkWhatsApp } from "../../lib/format";

/**
 * Atalho que abre a conversa do cliente no WhatsApp (app ou WhatsApp Web) a partir do telefone.
 * Some quando o número não serve (sem DDD, fixo curto etc.). Fica dentro de cartões clicáveis e arrastáveis,
 * por isso o clique não "vaza" para o cartão.
 */
export function BotaoWhatsApp({ telefone, nome, className = "" }: { telefone?: string | null; nome: string; className?: string }) {
  const href = linkWhatsApp(telefone);
  if (!href) return null;
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      draggable={false}
      title="Chamar no WhatsApp"
      aria-label={`Chamar ${nome} no WhatsApp`}
      onClick={(e) => e.stopPropagation()}
      onMouseDown={(e) => e.stopPropagation()}
      className={`focus-ring ${className.includes("absolute") ? "" : "relative"} z-10 inline-flex shrink-0 cursor-pointer items-center justify-center rounded-full bg-[#25D366]/20 text-[#1fa855] transition hover:bg-[#25D366] hover:text-white ${className}`}
    >
      <MessageCircle className="size-3.5" aria-hidden />
    </a>
  );
}
