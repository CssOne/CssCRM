import {
  CalendarDays,
  CheckSquare,
  Gauge,
  KanbanSquare,
  LayoutDashboard,
  LogOut,
  Menu as MenuIcon,
  Moon,
  Sun,
  Target,
  Users,
  UsersRound,
  X,
} from "lucide-react";
import { useState, type ReactNode } from "react";
import { NavLink, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { useTheme } from "../context/ThemeContext";
import { IconButton } from "./ui";

const iconesPorChave: Record<string, typeof Gauge> = {
  gauge: Gauge,
  users: Users,
  "kanban-square": KanbanSquare,
  "check-square": CheckSquare,
  "calendar-days": CalendarDays,
  target: Target,
  "users-round": UsersRound,
  "layout-dashboard": LayoutDashboard,
};

export function Shell({ children }: { children: ReactNode }) {
  const { sessao, logout } = useAuth();
  const { tema, alternar } = useTheme();
  const navigate = useNavigate();
  const [menuAberto, setMenuAberto] = useState(false);

  async function handleLogout() {
    await logout();
    navigate("/login");
  }

  if (!sessao) return null;

  return (
    <div className="flex min-h-screen bg-[var(--bg)]">
      {menuAberto && (
        <div className="fixed inset-0 z-30 bg-black/40 lg:hidden" onClick={() => setMenuAberto(false)} />
      )}

      <aside
        className={`fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r border-[var(--border)] bg-[var(--surface)] transition-transform lg:static lg:translate-x-0 ${
          menuAberto ? "translate-x-0" : "-translate-x-full"
        }`}
      >
        <div className="flex h-14 items-center justify-between border-b border-[var(--border)] px-4">
          <span className="text-sm font-bold tracking-tight text-[var(--fg)]">CSS Vision CRM</span>
          <IconButton label="Fechar menu" className="lg:hidden" onClick={() => setMenuAberto(false)}>
            <X className="size-4" />
          </IconButton>
        </div>

        <nav className="flex-1 space-y-1 overflow-y-auto p-3" aria-label="Navegação principal">
          {sessao.menu.map((item) => {
            const Icone = iconesPorChave[item.icone] ?? Gauge;
            return (
              <NavLink
                key={item.chave}
                to={item.rota}
                end={item.rota === "/app" || item.rota === "/app/crm"}
                onClick={() => setMenuAberto(false)}
                className={({ isActive }) =>
                  `focus-ring flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                    isActive
                      ? "bg-[var(--brand-soft)] text-[var(--brand)]"
                      : "text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
                  }`
                }
              >
                <Icone className="size-4 shrink-0" aria-hidden />
                {item.rotulo}
              </NavLink>
            );
          })}
        </nav>

        <div className="border-t border-[var(--border)] p-3">
          <div className="mb-2 px-2">
            <p className="truncate text-sm font-medium text-[var(--fg)]">{sessao.nomeCompleto}</p>
            <p className="truncate text-xs text-[var(--fg-muted)]">{sessao.papeis.join(", ")}</p>
          </div>
          <button
            onClick={handleLogout}
            className="focus-ring flex w-full items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--danger)] cursor-pointer"
          >
            <LogOut className="size-4" aria-hidden />
            Sair
          </button>
        </div>
      </aside>

      <div className="flex min-h-screen flex-1 flex-col lg:pl-0">
        <header className="sticky top-0 z-20 flex h-14 items-center justify-between border-b border-[var(--border)] bg-[var(--surface)] px-4">
          <IconButton label="Abrir menu" className="lg:hidden" onClick={() => setMenuAberto(true)}>
            <MenuIcon className="size-5" />
          </IconButton>
          <div className="hidden lg:block" />
          <IconButton label={tema === "dark" ? "Usar tema claro" : "Usar tema escuro"} onClick={alternar}>
            {tema === "dark" ? <Sun className="size-4" /> : <Moon className="size-4" />}
          </IconButton>
        </header>

        <main className="flex-1 p-4 lg:p-6">{children}</main>
      </div>
    </div>
  );
}
