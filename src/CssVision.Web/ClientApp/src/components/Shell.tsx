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
  MessagesSquare,
  Monitor,
  Moon,
  Search,
  Sparkles,
  Settings,
  Sun,
  Target,
  UserCog,
  Users,
  UsersRound,
  Wallet,
  X,
} from "lucide-react";
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { NavLink, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { mostrarNotificacao, somLigado, tocarPlim } from "../lib/avisosDoChat";
import { usePresenca } from "../lib/usePresenca";
import { rotuloNaoLidas, useChatNaoLidas } from "../lib/useChatNaoLidas";
import { useTheme, type Tema } from "../context/ThemeContext";
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
  "message-circle": MessageCircle,
  "messages-square": MessagesSquare,
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
      <img src="/logo-css.svg" alt="CSS Brasil" className="size-7 shrink-0 object-contain" />
      {!colapsado && (
        <div className="min-w-0 leading-tight">
          <p className="truncate text-sm font-semibold tracking-tight text-[var(--sidebar-fg)]">CSS Brasil</p>
          <p className="truncate text-[10px] font-medium text-[var(--sidebar-fg-muted)]">{subtitulo}</p>
        </div>
      )}
    </div>
  );
}

const OPCOES_TEMA: { valor: Tema; rotulo: string; icone: typeof Sun }[] = [
  { valor: "light", rotulo: "Tema claro", icone: Sun },
  { valor: "dark", rotulo: "Tema escuro", icone: Moon },
  { valor: "glass", rotulo: "Tema translúcido", icone: Sparkles },
];

export function SeletorDeTema() {
  const { tema, definir } = useTheme();
  return (
    <div role="radiogroup" aria-label="Tema" className="flex items-center gap-0.5 rounded-lg bg-[var(--surface-hover)] p-0.5">
      {OPCOES_TEMA.map(({ valor, rotulo, icone: Icone }) => (
        <button
          key={valor}
          type="button"
          role="radio"
          aria-checked={tema === valor}
          aria-label={rotulo}
          title={rotulo}
          onClick={() => definir(valor)}
          className={`focus-ring flex size-7 cursor-pointer items-center justify-center rounded-md transition-colors ${
            tema === valor ? "bg-[var(--surface)] text-[var(--fg)] shadow-[0_1px_2px_rgb(0_0_0/0.12)]" : "text-[var(--fg-muted)] hover:text-[var(--fg)]"
          }`}
        >
          <Icone className="size-4" aria-hidden />
        </button>
      ))}
    </div>
  );
}

export function Shell({ children }: { children: ReactNode }) {
  const { sessao, logout, temPapel } = useAuth();
  const { notificar } = useToast();
  const navigate = useNavigate();
  const location = useLocation();
  const [menuAberto, setMenuAberto] = useState(false);
  const [colapsado, setColapsado] = useState(() => localStorage.getItem("crm-sidebar-colapsado") === "1");
  const [busca, setBusca] = useState("");
  // Número de mensagens não lidas do chat no menu e no título da aba (só consulta quem tem o Chat no menu).
  const temChat = !!sessao?.menu.some((m) => m.chave === "chat");
  const chatNaoLidas = useChatNaoLidas(temChat, 30000);
  usePresenca(!!sessao); // "online" no chat: o CRM aberto avisa a cada minuto

  useEffect(() => {
    localStorage.setItem("crm-sidebar-colapsado", colapsado ? "1" : "0");
  }, [colapsado]);

  // Mensagem nova (o total subiu desde a consulta anterior): "plim" e, com a aba em segundo plano, notificação do navegador. A 1ª consulta
  // só registra o ponto de partida (abrir o CRM com mensagens antigas não deve tocar).
  const totalAnterior = useRef<number | null>(null);
  useEffect(() => {
    const antes = totalAnterior.current;
    totalAnterior.current = chatNaoLidas.total;
    if (antes === null || chatNaoLidas.total <= antes) return;
    if (somLigado()) tocarPlim();
    mostrarNotificacao(chatNaoLidas.total, () => navigate("/app/chat"));
  }, [chatNaoLidas.total, navigate]);

  useEffect(() => {
    if (chatNaoLidas.total === 0) return;
    const original = document.title;
    document.title = `(${rotuloNaoLidas(chatNaoLidas.total)}) ${original}`;
    return () => {
      document.title = original;
    };
  }, [chatNaoLidas.total]);

  const emPortal = location.pathname.startsWith("/app/portal");
  // O quadro de leads tem a própria busca (nome, placa, telefone, e-mail): a busca do topo sai dali.
  const noQuadroDeLeads = location.pathname.startsWith("/app/crm/leads/kanban");
  // Financeiro "puro": só gestão da carteira, usuários e avisos — nada de leads, atividades nem avisos de lead novo.
  const soFinanceiro = !!sessao && sessao.papeis.length === 1 && sessao.papeis[0] === "Financeiro";
  const itensNav = emPortal ? PORTAL_NAV : sessao?.menu.map((m) => ({ ...m, icone: iconesPorChave[m.icone] ?? Gauge, divisor: false }));


  // Só o item mais específico fica marcado (ex.: "Quadro de leads" não acende junto com "Leads").
  const rotaAtiva = [...(itensNav ?? [])]
    .filter((i) => !i.rota.startsWith("/tv/") && (location.pathname === i.rota || location.pathname.startsWith(`${i.rota}/`)))
    .sort((x, y) => y.rota.length - x.rota.length)[0]?.rota;

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

      {/* Barra lateral no estilo macOS: painel translúcido colado na borda, itens simples e seleção em tom suave da marca. */}
      <aside
        className={`ui-bar fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r border-[var(--sidebar-border)] bg-[var(--sidebar-bg)] transition-[transform,width] lg:translate-x-0 ${
          menuAberto ? "translate-x-0" : "-translate-x-full"
        } ${colapsado ? "lg:w-16" : "lg:w-56"}`}
      >
        <div className={`flex h-14 shrink-0 items-center justify-between gap-2 px-3.5 ${colapsado ? "lg:flex-col lg:justify-center lg:gap-1 lg:px-2" : ""}`}>
          <Logo subtitulo={emPortal ? "Portal do consultor" : "CRM comercial"} colapsado={colapsado} />
          <IconButton label="Fechar menu" className="lg:hidden" onClick={() => setMenuAberto(false)}>
            <X className="size-4" />
          </IconButton>
          <button
            aria-label={colapsado ? "Expandir menu" : "Recolher menu"}
            title={colapsado ? "Expandir menu" : "Recolher menu"}
            onClick={() => setColapsado((v) => !v)}
            className="focus-ring hidden size-7 shrink-0 cursor-pointer items-center justify-center rounded-md text-[var(--sidebar-fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--sidebar-fg)] lg:flex"
          >
            {colapsado ? <ChevronRight className="size-4" /> : <ChevronLeft className="size-4" />}
          </button>
        </div>

        <nav className="flex-1 space-y-px overflow-y-auto px-2 pb-2 pt-1" aria-label="Navegação principal">
          {itensNav?.map((item) => {
            const linhaBase = `focus-ring group relative flex h-8 items-center gap-2.5 rounded-lg px-2.5 text-[13px] font-medium transition-colors ${colapsado ? "lg:justify-center lg:px-0" : ""}`;
            const inativa = "text-[var(--sidebar-fg)] hover:bg-[var(--surface-hover)]";
            return (
              <div key={item.chave}>
                {item.rota.startsWith("/tv/") ? (
                  <a
                    href={item.rota}
                    target="_blank"
                    rel="noopener"
                    title={colapsado ? `${item.rotulo} (abre em outra aba)` : "Abre em outra aba"}
                    className={`${linhaBase} ${inativa}`}
                  >
                    <item.icone className="size-[17px] shrink-0 text-[var(--sidebar-fg-muted)]" aria-hidden />
                    <span className={colapsado ? "lg:hidden" : undefined}>{item.rotulo}</span>
                  </a>
                ) : (
                  <NavLink
                    to={item.rota}
                    end={item.rota === "/app" || item.rota === "/app/crm" || item.rota === "/app/portal"}
                    onClick={() => setMenuAberto(false)}
                    title={colapsado ? item.rotulo : undefined}
                    className={({ isActive: ativoPelaRota }) => {
                      const isActive = ativoPelaRota && item.rota === rotaAtiva;
                      return `${linhaBase} ${isActive ? "bg-[var(--sidebar-bg-active)] font-semibold text-[var(--sidebar-active-fg)]" : inativa}`;
                    }}
                  >
                    {({ isActive: ativoPelaRota }) => (
                      <>
                        <item.icone
                          className={`size-[17px] shrink-0 ${ativoPelaRota && item.rota === rotaAtiva ? "text-[var(--brand)]" : "text-[var(--sidebar-fg-muted)]"}`}
                          aria-hidden
                        />
                        <span className={`truncate ${colapsado ? "lg:hidden" : ""}`}>{item.rotulo}</span>
                        {item.chave === "chat" && chatNaoLidas.total > 0 && (
                          <span
                            className={`ml-auto rounded-full bg-[var(--danger)] px-1.5 text-[11px] font-semibold text-white ${colapsado ? "lg:absolute lg:right-1 lg:top-0.5 lg:ml-0 lg:px-1 lg:text-[10px]" : ""}`}
                            aria-label={`${chatNaoLidas.total} mensagens não lidas`}
                          >
                            {rotuloNaoLidas(chatNaoLidas.total)}
                          </span>
                        )}
                      </>
                    )}
                  </NavLink>
                )}
                {item.divisor && <div className="my-2 border-t border-[var(--sidebar-border)]" />}
              </div>
            );
          })}
        </nav>

        <div className="border-t border-[var(--sidebar-border)] p-2.5">
          <div className={`flex items-center gap-2.5 ${colapsado ? "lg:flex-col" : ""}`}>
            <Avatar nome={sessao.nomeCompleto} fotoUrl={sessao.fotoUrl} className="size-8 text-xs" />
            <div className={`min-w-0 flex-1 leading-tight ${colapsado ? "lg:hidden" : ""}`}>
              <p className="truncate text-[13px] font-medium text-[var(--sidebar-fg)]">{sessao.nomeCompleto}</p>
              <p className="truncate text-[11px] text-[var(--sidebar-fg-muted)]">{PAPEL_LABEL[sessao.papeis[0]] ?? sessao.papeis[0]}</p>
            </div>
            <button
              onClick={handleLogout}
              aria-label="Sair"
              title="Sair"
              className="focus-ring flex size-7 shrink-0 cursor-pointer items-center justify-center rounded-md text-[var(--sidebar-fg-muted)] hover:bg-[var(--danger-soft)] hover:text-[var(--danger)]"
            >
              <LogOut className="size-4" aria-hidden />
            </button>
          </div>
        </div>
      </aside>

      <div className={`flex min-h-screen min-w-0 flex-1 flex-col transition-[padding] ${colapsado ? "lg:pl-16" : "lg:pl-56"}`}>
        <header className="ui-bar sticky top-0 z-20 flex h-14 items-center justify-between gap-3 border-b border-[var(--sidebar-border)] bg-[var(--header-bg)] px-4 lg:px-8">
          <div className="flex items-center gap-2">
            <IconButton label="Abrir menu" className="lg:hidden" onClick={() => setMenuAberto(true)}>
              <MenuIcon className="size-5" />
            </IconButton>
            {!noQuadroDeLeads && (
            <form onSubmit={handleBuscar} className="relative hidden lg:block">
              <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" />
              <input
                value={busca}
                onChange={(e) => setBusca(e.target.value)}
                placeholder="Buscar clientes..."
                className="focus-ring h-8 w-72 rounded-lg border-0 bg-[var(--surface-hover)] pl-9 pr-3 text-[13px] text-[var(--fg)] placeholder:text-[var(--fg-muted)]"
              />
            </form>
            )}
          </div>

          <div className="flex items-center gap-1.5">
            <NotificacoesAtividades />
            <IconButton label="Mensagens (em breve)" onClick={() => notificar("info", "Chat interno chegando em breve.")}>
              <MessageCircle className="size-4" />
            </IconButton>
            <SeletorDeTema />
            <div className="ml-1.5 hidden items-center gap-2.5 border-l border-[var(--border)] pl-3 sm:flex">
              <Avatar nome={sessao.nomeCompleto} fotoUrl={sessao.fotoUrl} className="size-9 text-sm" />
              <div className="hidden min-w-0 leading-tight xl:block">
                <p className="max-w-40 truncate text-sm font-semibold text-[var(--fg)]">{sessao.nomeCompleto}</p>
                <p className="text-xs text-[var(--fg-muted)]">{PAPEL_LABEL[sessao.papeis[0]] ?? sessao.papeis[0]}</p>
              </div>
            </div>
          </div>
        </header>

        <main className="min-w-0 flex-1 p-4 lg:px-8 lg:py-7">
          {!emPortal && temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial") && <AlertaDistribuicaoLimites />}
          {children}
        </main>
        {!emPortal && !soFinanceiro && <LembretesAdesao />}
        {!emPortal && !soFinanceiro && <NotificacaoNovosLeads usuarioId={sessao.id} />}
        {!soFinanceiro && temPapel("Comercial") && <NotificacaoAvisosPagamento usuarioId={sessao.id} />}
        {!emPortal && !soFinanceiro && <PainelLead />}
        <ConviteNotificacoesPush />
      </div>
    </div>
  );
}
