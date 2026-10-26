import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { MailWarning } from "lucide-react";
import { api } from "../../lib/api";
import type { EmailSistema } from "../../lib/types";

/** Aviso para administradores: sem e-mail do sistema configurado, o "Esqueci a senha" não envia nada (e a tela diz que enviou). */
export function AlertaEmailDoSistema() {
  const [estado, setEstado] = useState<EmailSistema | null>(null);
  useEffect(() => {
    api.get<EmailSistema>("/crm/email-sistema").then(setEstado).catch(() => {});
  }, []);

  if (!estado || estado.configurado) return null;
  return (
    <div
      role="alert"
      className="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-[color-mix(in_srgb,var(--warning)_40%,transparent)] bg-[var(--warning-soft)] px-3 py-2 text-sm text-[var(--fg)]"
    >
      <span className="flex items-center gap-2">
        <MailWarning className="size-4 shrink-0 text-[var(--warning)]" aria-hidden />
        O e-mail do sistema não está configurado: quem clica em "Esqueci a senha" não recebe o link.
      </span>
      <Link to="/app/crm/usuarios?aba=email" className="focus-ring font-semibold text-[var(--brand)] hover:underline">
        Configurar agora
      </Link>
    </div>
  );
}
