import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import { Card } from "../components/ui";

/**
 * Este módulo contém apenas o CRM comercial. A área administrativa (dashboards, importações,
 * auditoria, backups etc.) vive no projeto AplicacaoDashboard/CSS Vision e será integrada aqui
 * futuramente sob a mesma rota "/app". Até lá, Admin/GestorMaster usam esta página como ponte
 * para a supervisão comercial.
 */
export function AdminPlaceholderPage() {
  return (
    <div className="mx-auto max-w-xl py-12 text-center">
      <Card className="p-8">
        <h1 className="mb-2 text-lg font-semibold text-[var(--fg)]">Área administrativa</h1>
        <p className="mb-6 text-sm text-[var(--fg-muted)]">
          A área administrativa (dashboards, importações, auditoria, usuários e configurações) faz parte da
          AplicacaoDashboard existente e ainda não foi integrada a este módulo. Enquanto isso, use o CRM para
          supervisionar leads, oportunidades e a equipe comercial.
        </p>
        <Link
          to="/app/crm"
          className="focus-ring inline-flex items-center gap-2 rounded-lg bg-[var(--brand)] px-4 py-2 text-sm font-medium text-[var(--brand-fg)] hover:opacity-90"
        >
          Ir para o CRM
          <ArrowRight className="size-4" />
        </Link>
      </Card>
    </div>
  );
}
