import { Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "./context/AuthContext";
import { Shell } from "./components/Shell";
import { Spinner } from "./components/ui";
import { LoginPage } from "./pages/Login";
import { AdminPlaceholderPage } from "./pages/AdminPlaceholder";
import { OverviewPage } from "./pages/crm/Overview";
import { LeadsPage } from "./pages/crm/Leads";
import { LeadsKanbanPage } from "./pages/crm/LeadsKanban";
import { LeadDetailPage } from "./pages/crm/LeadDetail";
import { PipelinePage } from "./pages/crm/Pipeline";
import { ActivitiesPage } from "./pages/crm/Activities";
import { AgendaPage } from "./pages/crm/Agenda";
import { GoalsPage } from "./pages/crm/Goals";
import { ManagementPage } from "./pages/crm/Management";

function CarregandoTelaCheia() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-[var(--bg)]">
      <Spinner className="size-8" />
    </div>
  );
}

function RotaProtegida({ papeis, children }: { papeis?: string[]; children: React.ReactNode }) {
  const { sessao, carregando, temPapel } = useAuth();

  if (carregando) return <CarregandoTelaCheia />;
  if (!sessao) return <Navigate to="/login" replace />;
  if (papeis && !temPapel(...papeis)) return <Navigate to={sessao.areaInicial} replace />;

  return <Shell>{children}</Shell>;
}

const PAPEIS_GESTAO = ["Admin", "GestorMaster", "GestorComercial"];
const PAPEIS_ADMIN = ["Admin", "GestorMaster"];

export default function App() {
  const { sessao, carregando } = useAuth();

  if (carregando) return <CarregandoTelaCheia />;

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route path="/app" element={<RotaProtegida papeis={PAPEIS_ADMIN}><AdminPlaceholderPage /></RotaProtegida>} />

      <Route path="/app/crm" element={<RotaProtegida><OverviewPage /></RotaProtegida>} />
      <Route path="/app/crm/leads" element={<RotaProtegida><LeadsPage /></RotaProtegida>} />
      <Route path="/app/crm/leads/kanban" element={<RotaProtegida><LeadsKanbanPage /></RotaProtegida>} />
      <Route path="/app/crm/leads/:id" element={<RotaProtegida><LeadDetailPage /></RotaProtegida>} />
      <Route path="/app/crm/pipeline" element={<RotaProtegida><PipelinePage /></RotaProtegida>} />
      <Route path="/app/crm/activities" element={<RotaProtegida><ActivitiesPage /></RotaProtegida>} />
      <Route path="/app/crm/agenda" element={<RotaProtegida><AgendaPage /></RotaProtegida>} />
      <Route path="/app/crm/goals" element={<RotaProtegida><GoalsPage /></RotaProtegida>} />
      <Route
        path="/app/crm/gestao"
        element={
          <RotaProtegida papeis={PAPEIS_GESTAO}>
            <ManagementPage />
          </RotaProtegida>
        }
      />

      <Route path="*" element={<Navigate to={sessao ? sessao.areaInicial : "/login"} replace />} />
    </Routes>
  );
}
