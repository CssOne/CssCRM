import {
  Briefcase,
  CalendarDays,
  ChartColumn,
  CheckSquare,
  ChevronLeft,
  Database,
  ChevronRight,
  FileText,
  Gauge,
  IdCard,
  KanbanSquare,
  LayoutDashboard,
  LayoutGrid,
  LifeBuoy,
  LogOut,
  Megaphone,
  Menu as MenuIcon,
  MessageCircle,
  Monitor,
  Moon,
  Search,
  Settings,
  Sun,
  Target,
  UserCog,
  Users,
  UsersRound,
  Wallet,
  X,
} from "lucide-react";
import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { NavLink, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { useTheme } from "../context/ThemeContext";
import { Avatar, IconButton, useToast } from "./ui";
import { LembretesAdesao } from "./crm/LembretesAdesao";
import { NotificacaoNovosLeads } from "./crm/NotificacaoNovosLeads";
import { AlertaDistribuicaoLimites } from "./crm/AlertaDistribuicaoLimites";
import { NotificacoesAtividades } from "./crm/NotificacoesAtividades";
import { PainelLead } from "./crm/PainelLead";
import { ConviteNotificacoesPush } from "./crm/NotificacoesPush";
import { NotificacaoAvisosPagamento } from "./crm/NotificacaoAvisosPagamento";

const iconesPorChave: Record<string, typeof Gauge> = {
  gauge: Gauge,
  users: Users,
  "kanban-square": KanbanSquare,
  "check-square": CheckSquare,
  "calendar-days": CalendarDays,
  target: Target,
  "users-round": UsersRound,
  "chart-column": ChartColumn,
  "layout-dashboard": LayoutDashboard,
  "layout-grid": LayoutGrid,
  "user-cog": UserCog,
  briefcase: Briefcase,
  "id-card": IdCard,
  megaphone: Megaphone,
  database: Database,
  "life-buoy": LifeBuoy,
  wallet: Wallet,
  monitor: Monitor,
};

export const PAPEL_LABEL: Record<string, string> = {
  Admin: "Administrador",
  GestorMaster: "Gestor master",
  GestorComercial: "Gestor regional",
  SupervisorComercial: "Supervisor comercial",
  Comercial: "Consultor",
  Marketing: "Marketing",
  Financeiro: "Financeiro",
};

const PORTAL_NAV = [
  { chave: "portal-voltar", rotulo: "Voltar ao CRM", icone: LayoutDashboard, rota: "/app/crm", divisor: true },
  { chave: "portal-dashboard", rotulo: "Dashboard", icone: Gauge, rota: "/app/portal" },
  { chave: "portal-clientes", rotulo: "Clientes", icone: Users, rota: "/app/portal/clientes" },
  { chave: "portal-propostas", rotulo: "Propostas", icone: FileText, rota: "/app/portal/propostas" },
  { chave: "portal-config", rotulo: "Configurações", icone: Settings, rota: "/app/portal/configuracoes" },
];

function Logo({ subtitulo, colapsado = false }: { subtitulo: string; colapsado?: boolean }) {
  return (
    <div className="flex min-w-0 items-center gap-2.5">
      <img src="/logo-css.svg" alt="CSS Brasil" className="size-9 shrink-0 object-contain" />
      {!colapsado && (
        <div className="min-w-0 leading-tight">
          <p className="truncate text-sm font-extrabold tracking-tight text-white">CSS Brasil</p>
          <p className="truncate text-[10px] font-semibold uppercase tracking-wider text-[var(--sidebar-fg-muted)]">{subtitulo}</p>
        </div>
      )}
    </div>
  );
}

export function Shell({ children }: { children: ReactNode }) {
  const { sessao, logout, temPapel } = useAuth();
  const { tema, alternar } = useTheme();
  const { notificar } = useToast();
  const navigate = useNavigate();
  const location = useLocation();
  const [menuAberto, setMenuAberto] = useState(false);
  const [colapsado, setColapsado] = useState(() => localStorage.getItem("crm-sidebar-colapsado") === "1");
  const [busca, setBusca] = useState("");

  useEffect(() => {
    localStorage.setItem("crm-sidebar-colapsado", colapsado ? "1" : "0");
  }, [colapsado]);

  const emPortal = location.pathname.startsWith("/app/portal");
  // Financeiro "puro": só gestão da carteira, usuários e avisos — nada de leads, atividades nem avisos de lead novo.
  const soFinanceiro = !!sessao && sessao.papeis.length === 1 && sessao.papeis[0] === "Financeiro";
  const itensNav = emPortal ? PORTAL_NAV : sessao?.menu.map((m) => ({ ...m, icone: iconesPorChave[m.icone] ?? Gauge, divisor: false }));


  async function handleLogout() {
    await logout();
    navigate("/login");
  }

  function handleBuscar(e: FormEvent) {
    e.preventDefault();
    if (!busca.trim()) return;
    navigate(`/app/portal/clientes?busca=${encodeURIComponent(busca.trim())}`);
  }

  if (!sessao) return null;

  return (
    <div className="flex min-h-screen bg-[var(--bg)]">
      {menuAberto && <div className="fixed inset-0 z-30 bg-black/40 lg:hidden" onClick={() => setMenuAberto(false)} />}

      <aside
        className={`fixed inset-y-0 left-0 z-40 flex w-64 flex-col bg-[var(--sidebar-bg)] transition-[transform,width] lg:translate-x-0 ${
          menuAberto ? "translate-x-0" : "-translate-x-full"
        } ${colapsado ? "lg:w-20" : "lg:w-64"}`}
      >
        <div className={`flex h-16 items-center justify-between px-4 ${colapsado ? "lg:justify-center lg:px-0" : ""}`}>
          <Logo subtitulo={emPortal ? "Portal do consultor" : "CRM comercial"} colapsado={colapsado} />
          <IconButton label="Fechar menu" className="text-white lg:hidden" onClick={() => setMenuAberto(false)}>
            <X className="size-4" />
          </IconButton>
        </div>

        <button
          aria-label={colapsado ? "Expandir menu" : "Recolher menu"}
          title={colapsado ? "Expandir menu" : "Recolher menu"}
          onClick={() => setColapsado((v) => !v)}
          className="focus-ring absolute -right-3 top-14 z-10 hidden size-6 cursor-pointer items-center justify-center rounded-full border border-[var(--sidebar-border)] bg-[var(--sidebar-bg)] text-[var(--sidebar-fg-muted)] shadow-sm hover:text-white lg:flex"
        >
          {colapsado ? <ChevronRight className="size-3.5" /> : <ChevronLeft className="size-3.5" />}
        </button>

        <nav className="flex-1 space-y-1 overflow-y-auto px-3 pb-3" aria-label="Navegação principal">
          {itensNav?.map((item) => (
            <div key={item.chave}>
              {item.rota.startsWith("/tv/") ? (
                <a
                  href={item.rota}
                  target="_blank"
                  rel="noopener"
                  title={colapsado ? `${item.rotulo} (abre em outra aba)` : "Abre em outra aba"}
                  className={`focus-ring flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium text-[var(--sidebar-fg-muted)] transition-colors hover:bg-[var(--sidebar-bg-active)]/60 hover:text-[var(--sidebar-fg)] ${
                    colapsado ? "lg:justify-center lg:px-2" : ""
                  }`}
                >
                  <item.icone className="size-4 shrink-0" aria-hidden />
                  <span className={colapsado ? "lg:hidden" : undefined}>{item.rotulo}</span>
                </a>
              ) : (
              <NavLink
                to={item.rota}
                end={item.rota === "/app" || item.rota === "/app/crm" || item.rota === "/app/portal"}
                onClick={() => setMenuAberto(false)}
                title={colapsado ? item.rotulo : undefined}
                className={({ isActive }) =>
                  `focus-ring flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors ${
                    colapsado ? "lg:justify-center lg:px-2" : ""
                  } ${
                    isActive
                      ? "bg-[var(--sidebar-bg-active)] text-white"
                      : "text-[var(--sidebar-fg-muted)] hover:bg-[var(--sidebar-bg-active)]/60 hover:text-[var(--sidebar-fg)]"
                  }`
                }
              >
                <item.icone className="size-4 shrink-0" aria-hidden />
                <span className={colapsado ? "lg:hidden" : undefined}>{item.rotulo}</span>
              </NavLink>
              )}
              {item.divisor && <div className="my-2 border-t border-[var(--sidebar-border)]" />}
            </div>
          ))}
        </nav>

        <div className="border-t border-[var(--sidebar-border)] p-3">
          <div className={`mb-2 flex items-center gap-3 px-2 py-1 ${colapsado ? "lg:justify-center lg:px-0" : ""}`}>
            <Avatar nome={sessao.nomeCompleto} fotoUrl={sessao.fotoUrl} />
            <div className={`min-w-0 ${colapsado ? "lg:hidden" : ""}`}>
              <p className="truncate text-sm font-medium text-white">{sessao.nomeCompleto}</p>
              <p className="truncate text-xs text-[var(--sidebar-fg-muted)]">{PAPEL_LABEL[sessao.papeis[0]] ?? sessao.papeis[0]}</p>
            </div>
          </div>
          <button
            onClick={handleLogout}
            title={colapsado ? "Sair" : undefined}
            className={`focus-ring flex w-full items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-[var(--sidebar-fg-muted)] hover:bg-[var(--sidebar-bg-active)]/60 hover:text-white cursor-pointer ${
              colapsado ? "lg:justify-center lg:px-2" : ""
            }`}
          >
            <LogOut className="size-4 shrink-0" aria-hidden />
            <span className={colapsado ? "lg:hidden" : undefined}>Sair</span>
          </button>
        </div>
      </aside>

      <div className={`flex min-h-screen min-w-0 flex-1 flex-col transition-[padding] ${colapsado ? "lg:pl-20" : "lg:pl-64"}`}>
        <header className="sticky top-0 z-20 flex h-16 items-center justify-between gap-3 border-b border-[var(--border)] bg-[var(--surface)] px-4">
          <div className="flex items-center gap-2">
            <IconButton label="Abrir menu" className="lg:hidden" onClick={() => setMenuAberto(true)}>
              <MenuIcon className="size-5" />
            </IconButton>
            <form onSubmit={handleBuscar} className="relative hidden lg:block">
              <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" />
              <input
                value={busca}
                onChange={(e) => setBusca(e.target.value)}
                placeholder="Buscar clientes..."
                className="focus-ring w-72 rounded-lg border border-[var(--border)] bg-[var(--bg)] py-2 pl-9 pr-3 text-sm text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
              />
            </form>
          </div>

          <div className="flex items-center gap-1.5">
            <NotificacoesAtividades />
            <IconButton label="Mensagens (em breve)" onClick={() => notificar("info", "Chat interno chegando em breve.")}>
              <MessageCircle className="size-4" />
            </IconButton>
            <IconButton label={tema === "dark" ? "Usar tema claro" : "Usar tema escuro"} onClick={alternar}>
              {tema === "dark" ? <Sun className="size-4" /> : <Moon className="size-4" />}
            </IconButton>
          </div>
        </header>

        <main className="min-w-0 flex-1 p-4 lg:p-6">
          {!emPortal && temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial") && <AlertaDistribuicaoLimites />}
          {children}
        </main>
        {!emPortal && !soFinanceiro && <LembretesAdesao />}
        {!emPortal && !soFinanceiro && <NotificacaoNovosLeads usuarioId={sessao.id} />}
        {!soFinanceiro && temPapel("Comercial") && <NotificacaoAvisosPagamento usuarioId={sessao.id} />}
        {!emPortal && !soFinanceiro && <PainelLead />}
        {!emPortal && <ConviteNotificacoesPush />}
      </div>
    </div>
  );
}
