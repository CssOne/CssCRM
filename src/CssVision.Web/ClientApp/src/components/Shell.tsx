import {
  Briefcase,
  CalendarDays,
  ChartColumn,
  CheckSquare,
  Database,
  ChevronRight,
  PanelLeftClose,
  PanelLeftOpen,
  FileText,
  Gauge,
  IdCard,
  Landmark,
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
  ChevronDown,
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
import { Link, NavLink, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { mostrarNotificacao, somLigado, tocarPlim } from "../lib/avisosDoChat";
import { usePresenca } from "../lib/usePresenca";
import { rotuloNaoLidas, useChatNaoLidas } from "../lib/useChatNaoLidas";
import { useTheme, type Tema } from "../context/ThemeContext";
import { Avatar, IconButton } from "./ui";
import { LembretesAdesao } from "./crm/LembretesAdesao";
import { NotificacaoNovosLeads } from "./crm/NotificacaoNovosLeads";
import { AlertaDistribuicaoLimites } from "./crm/AlertaDistribuicaoLimites";
import { AlertaEmailDoSistema } from "./crm/AlertaEmailDoSistema";
import { NotificacoesAtividades } from "./crm/NotificacoesAtividades";
import { PainelLead } from "./crm/PainelLead";
import { ConviteNotificacoesPush } from "./crm/NotificacoesPush";
import { NotificacaoAvisosPagamento } from "./crm/NotificacaoAvisosPagamento";
import { AlertaInicioCobranca } from "./portal/AlertaInicioCobranca";

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
  landmark: Landmark,
  monitor: Monitor,
  settings: Settings,
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
  { chave: "portal-financeiro", rotulo: "Controle financeiro", icone: Wallet, rota: "/app/portal/controle-financeiro" },
];

/** Seções do menu lateral. Cada item cai na seção da sua chave; o que não estiver aqui vai para "Outros". */
const SECOES: { rotulo: string; chaves: string[] }[] = [
  { rotulo: "Início", chaves: ["portal", "portal-financeiro", "crm-overview", "overview"] },
  { rotulo: "Comercial", chaves: ["crm-leads", "crm-leads-kanban", "crm-pipeline", "crm-agenda", "crm-goals", "crm-relatorio"] },
  { rotulo: "Gestão", chaves: ["crm-management", "crm-users", "setor-financeiro", "tv-comercial"] },
  { rotulo: "Marketing e sistema", chaves: ["marketing", "admin-backups"] },
  { rotulo: "Comunicação", chaves: ["chat", "discord", "suporte"] },
];

/** No Portal do consultor o "Controle financeiro" fica junto dos demais itens do portal (logo abaixo de "Propostas"), não na seção "Início" do CRM. */
function agruparNav<T extends { chave: string }>(itens: T[], emPortal = false): { rotulo: string; itens: T[] }[] {
  const secoes = emPortal ? SECOES.map((sec) => ({ ...sec, chaves: sec.chaves.filter((c) => c !== "portal-financeiro") })) : SECOES;
  const grupos = secoes.map((sec) => ({ rotulo: sec.rotulo, itens: itens.filter((i) => sec.chaves.includes(i.chave)) }));
  const outros = itens.filter((i) => !secoes.some((sec) => sec.chaves.includes(i.chave)));
  if (outros.length) grupos.push({ rotulo: "Outros", itens: outros });
  return grupos.filter((g) => g.itens.length > 0);
}

function Logo({ subtitulo, colapsado = false }: { subtitulo: string; colapsado?: boolean }) {
  return (
    <div className="flex min-w-0 items-center gap-2.5">
      <img src="/logo-css-white.png" alt="CSS Brasil" className="size-8 shrink-0 object-contain" />
      {!colapsado && (
        <div className="logo-texto min-w-0 leading-tight">
          <p className="truncate text-sm font-semibold tracking-tight text-[var(--sidebar-fg)]">CSS Brasil</p>
          <p className="truncate text-[10px] font-medium text-[var(--sidebar-fg-muted)]">{subtitulo}</p>
        </div>
      )}
    </div>
  );
}

const itemMenu = "focus-ring flex items-center gap-2.5 rounded-xl px-2.5 py-2 text-sm font-medium text-[var(--fg)] hover:bg-[var(--surface-hover)]";
const iconeMenu = "flex size-7 items-center justify-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand)]";

/** Chip do usuário no cabeçalho; ao clicar abre um menu com Configurações. */
function MenuDoUsuario({
  nome,
  email,
  fotoUrl,
  papel,
  temSuporte,
  onSair,
}: {
  nome: string;
  email?: string | null;
  fotoUrl?: string | null;
  papel: string;
  temSuporte: boolean;
  onSair: () => void;
}) {
  const [aberto, setAberto] = useState(false);
  const raiz = useRef<HTMLDivElement>(null);
  const local = useLocation();

  useEffect(() => setAberto(false), [local.pathname]);
  useEffect(() => {
    if (!aberto) return;
    const fora = (e: MouseEvent) => {
      if (raiz.current && !raiz.current.contains(e.target as Node)) setAberto(false);
    };
    const esc = (e: KeyboardEvent) => e.key === "Escape" && setAberto(false);
    document.addEventListener("mousedown", fora);
    document.addEventListener("keydown", esc);
    return () => {
      document.removeEventListener("mousedown", fora);
      document.removeEventListener("keydown", esc);
    };
  }, [aberto]);

  return (
    <div ref={raiz} className="relative ml-2">
      <button
        type="button"
        aria-haspopup="menu"
        aria-expanded={aberto}
        onClick={() => setAberto((v) => !v)}
        className="focus-ring flex cursor-pointer items-center gap-2.5 rounded-full border border-[var(--border)] bg-[var(--surface)] py-1 pl-1 pr-3 text-left transition hover:bg-[var(--surface-hover)]"
      >
        <Avatar nome={nome} fotoUrl={fotoUrl} className="size-8 text-xs" />
        <div className="hidden min-w-0 leading-tight md:block">
          <p className="max-w-36 truncate text-[13px] font-semibold text-[var(--fg)]">{nome}</p>
          <p className="text-[11px] text-[var(--fg-muted)]">{papel}</p>
        </div>
        <ChevronDown className={`size-3.5 text-[var(--fg-muted)] transition-transform ${aberto ? "rotate-180" : ""}`} aria-hidden />
      </button>
      {aberto && (
        <div role="menu" className="ui-surface absolute right-0 top-full z-50 mt-2 w-64 rounded-2xl border border-[var(--border)] bg-[var(--surface-solid)] p-1.5 shadow-[var(--shadow-card)]">
          <div className="flex items-center gap-3 rounded-xl bg-[var(--surface-hover)] p-2.5">
            <Avatar nome={nome} fotoUrl={fotoUrl} className="size-10 text-sm" />
            <div className="min-w-0 leading-tight">
              <p className="truncate text-sm font-semibold text-[var(--fg)]">{nome}</p>
              {email && <p className="truncate text-xs text-[var(--fg-muted)]">{email}</p>}
              <p className="mt-0.5 text-[11px] font-medium text-[var(--brand)]">{papel}</p>
            </div>
          </div>
          <div className="mt-1.5 space-y-0.5">
            <Link role="menuitem" to="/app/portal/configuracoes" className={itemMenu}>
              <span className={iconeMenu}>
                <Settings className="size-4" aria-hidden />
              </span>
              Configurações
            </Link>
            {temSuporte && (
              <Link role="menuitem" to="/app/suporte" className={itemMenu}>
                <span className={iconeMenu}>
                  <LifeBuoy className="size-4" aria-hidden />
                </span>
                Ajuda e suporte
              </Link>
            )}
          </div>
          <div className="mx-1 mt-1.5 flex items-center justify-between border-t border-[var(--border)] px-1.5 pb-1 pt-2.5">
            <span className="text-xs font-medium text-[var(--fg-muted)]">Aparência</span>
            <SeletorDeTema />
          </div>
          <div className="mt-1 border-t border-[var(--border)] pt-1.5">
            <button
              type="button"
              role="menuitem"
              onClick={onSair}
              className="focus-ring flex w-full cursor-pointer items-center gap-2.5 rounded-xl px-2.5 py-2 text-sm font-medium text-[var(--danger)] hover:bg-[var(--danger-soft)]"
            >
              <span className="flex size-7 items-center justify-center rounded-lg bg-[var(--danger-soft)]">
                <LogOut className="size-4" aria-hidden />
              </span>
              Sair
            </button>
          </div>
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
    <div role="radiogroup" aria-label="Tema" className="flex items-center gap-0.5 rounded-full bg-[var(--surface-hover)] p-0.5">
      {OPCOES_TEMA.map(({ valor, rotulo, icone: Icone }) => (
        <button
          key={valor}
          type="button"
          role="radio"
          aria-checked={tema === valor}
          aria-label={rotulo}
          title={rotulo}
          onClick={() => definir(valor)}
          className={`focus-ring flex size-7 cursor-pointer items-center justify-center rounded-full transition-colors ${
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
  const navigate = useNavigate();
  const location = useLocation();
  const [menuAberto, setMenuAberto] = useState(false);
  // A barra lateral fica recolhida (cápsula de ícones) e abre sozinha ao passar o mouse; o alfinete ao lado da logo a deixa fixada aberta.
  const [fixada, setFixada] = useState(() => localStorage.getItem("crm-sidebar-fixada") === "1");
  const colapsado = !fixada;
  const [busca, setBusca] = useState("");
  // Número de mensagens não lidas do chat no menu e no título da aba (só consulta quem tem o Chat no menu).
  const temChat = !!sessao?.menu.some((m) => m.chave === "chat");
  const chatNaoLidas = useChatNaoLidas(temChat, 30000);
  usePresenca(!!sessao); // "online" no chat: o CRM aberto avisa a cada minuto

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

  // Configurações (da conta) fica na barra lateral normal do CRM, não na do portal.
  const emPortal = location.pathname.startsWith("/app/portal") && !location.pathname.startsWith("/app/portal/configuracoes");
  // O quadro de leads tem a própria busca (nome, placa, telefone, e-mail): a busca do topo sai dali.
  const noQuadroDeLeads = location.pathname.startsWith("/app/crm/leads/kanban");
  // Financeiro "puro": só gestão da carteira, usuários e avisos — nada de leads, atividades nem avisos de lead novo.
  const soFinanceiro = !!sessao && sessao.papeis.length === 1 && sessao.papeis[0] === "Financeiro";
  const itensNav = emPortal ? PORTAL_NAV : sessao?.menu.map((m) => ({ ...m, icone: iconesPorChave[m.icone] ?? Gauge, divisor: false }));


  // Só o item mais específico fica marcado (ex.: "Quadro de leads" não acende junto com "Leads").
  const emConfiguracoes = location.pathname.startsWith("/app/portal/configuracoes");
  const rotaAtiva = [...(itensNav ?? [])]
    .filter((i) => !emConfiguracoes && !i.rota.startsWith("/tv/") && (location.pathname === i.rota || location.pathname.startsWith(`${i.rota}/`)))
    .sort((x, y) => y.rota.length - x.rota.length)[0]?.rota;

  // Trilha do cabeçalho: seção do menu + página atual (ex.: "Comercial › Quadro de leads").
  const itemAtivo = (itensNav ?? []).find((i) => i.rota === rotaAtiva);
  const trilha = itemAtivo
    ? { secao: emPortal ? "Portal do consultor" : SECOES.find((sec) => sec.chaves.includes(itemAtivo.chave))?.rotulo, titulo: itemAtivo.rotulo }
    : emConfiguracoes
      ? { secao: "Conta", titulo: "Configurações" }
      : null;

  let indiceItem = 0; // ordem do item no menu, para a entrada em sequência
  // Com a barra recolhida, passar o mouse por cima abre ela temporariamente (por cima do conteúdo, sem empurrá-lo).
  useEffect(() => {
    localStorage.setItem("crm-sidebar-fixada", fixada ? "1" : "0");
  }, [fixada]);
  // Tamanho da barra em animação: a cápsula tem a altura do conteúdo (muda ao trocar de menu, ex.: Portal do consultor) e, ao abrir/fechar a barra
  // inteira, largura, altura e posição passam de um valor ao outro com transição — em vez de a barra ser recriada de uma vez.
  const [desktop, setDesktop] = useState(() => window.matchMedia("(min-width: 1024px)").matches);
  useEffect(() => {
    const mq = window.matchMedia("(min-width: 1024px)");
    const mudou = () => setDesktop(mq.matches);
    mq.addEventListener("change", mudou);
    return () => mq.removeEventListener("change", mudou);
  }, []);
  const cabecalhoRef = useRef<HTMLDivElement>(null);
  const navRef = useRef<HTMLElement>(null);
  const conteudoRef = useRef<HTMLDivElement>(null);
  const [alturaCapsula, setAlturaCapsula] = useState<number | null>(null);
  useEffect(() => {
    if (!desktop) {
      setAlturaCapsula(null);
      return;
    }
    if (colapsado === false) return; // barra inteira: altura da janela; a última altura da cápsula fica guardada para a volta animada
    const medir = () => {
      const cab = cabecalhoRef.current;
      const nav = navRef.current;
      const conteudo = conteudoRef.current;
      if (!cab || !nav || !conteudo) return;
      const cs = getComputedStyle(nav);
      const natural = cab.offsetHeight + parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom) + conteudo.offsetHeight + 2;
      setAlturaCapsula(Math.min(Math.ceil(natural), window.innerHeight - 32));
    };
    medir();
    const ro = new ResizeObserver(medir);
    if (conteudoRef.current) ro.observe(conteudoRef.current);
    if (cabecalhoRef.current) ro.observe(cabecalhoRef.current);
    window.addEventListener("resize", medir);
    return () => {
      ro.disconnect();
      window.removeEventListener("resize", medir);
    };
  }, [desktop, colapsado]);
  const recolhida = colapsado; // cápsula só de ícones (não abre ao passar o mouse)
  const capsula = colapsado; // cápsula; fixada = barra oficial
  // Efeito "dock": os ícones crescem perto do mouse e uma etiqueta com o nome desliza ao lado do ícone mais próximo.
  const [dica, setDica] = useState<{ texto: string; x: number; y: number; visivel: boolean } | null>(null);
  const docar = (e: React.MouseEvent<HTMLElement>) => {
    if (!recolhida || window.innerWidth < 1024) return;
    const reduzido = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    let perto: HTMLElement | null = null;
    let menor = Infinity;
    e.currentTarget.querySelectorAll<HTMLElement>("a[data-rotulo]").forEach((el) => {
      const r = el.getBoundingClientRect();
      const d = Math.abs(r.top + r.height / 2 - e.clientY);
      if (d < menor) {
        menor = d;
        perto = el;
      }
      el.style.transition = "transform .45s cubic-bezier(.22,1,.36,1), background-color .2s, color .2s";
      el.style.transform = reduzido ? "" : `scale(${(1 + 0.38 * Math.exp(-(d * d) / 4800)).toFixed(3)})`;
    });
    const alvo = perto as HTMLElement | null;
    if (!alvo || menor > 24) return setDica((d) => (d ? { ...d, visivel: false } : d));
    const r = alvo.getBoundingClientRect();
    const barra = alvo.closest("aside")?.getBoundingClientRect();
    setDica({ texto: alvo.dataset.rotulo ?? "", x: (barra?.right ?? r.right) + 10, y: r.top + r.height / 2, visivel: true });
  };
  const soltarDock = (e: React.MouseEvent<HTMLElement>) => {
    e.currentTarget.querySelectorAll<HTMLElement>("a[data-rotulo]").forEach((el) => {
      el.style.transform = "";
    });
    setDica((d) => (d ? { ...d, visivel: false } : d));
  };

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

      {dica && (
        <span
          aria-hidden
          className="pointer-events-none fixed z-50 hidden -translate-y-1/2 whitespace-nowrap rounded-full bg-white px-3 py-1.5 text-xs font-semibold text-[#04264c] shadow-[0_6px_16px_-6px_rgb(0_0_0/0.5)] lg:block"
          style={{ left: dica.x, top: dica.y, opacity: dica.visivel ? 1 : 0, transition: "top .45s cubic-bezier(.22,1,.36,1), opacity .3s" }}
        >
          {dica.texto}
        </span>
      )}

      {/* Barra lateral nas cores da empresa: azul-marinho cheio, menu em seções e marcador claro no item ativo. */}
      <aside
        data-capsula={capsula ? "true" : "false"}
        style={
          desktop
            ? {
                height: capsula ? (alturaCapsula ?? undefined) : "calc(100vh - 1.5rem)",
                transition:
                  "height .75s cubic-bezier(.22,1,.36,1), width .65s cubic-bezier(.22,1,.36,1), left .65s cubic-bezier(.22,1,.36,1), border-radius .65s cubic-bezier(.22,1,.36,1), box-shadow .65s ease",
              }
            : undefined
        }
        className={`ui-bar barra-anim fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r border-[var(--sidebar-border)] bg-[var(--sidebar-bg)] text-[var(--sidebar-fg)] transition-[transform,width] lg:translate-x-0 ${
          menuAberto ? "translate-x-0" : "-translate-x-full"
        } ${
          capsula
            ? `lg:bottom-auto lg:left-4 lg:top-1/2 lg:h-auto lg:max-h-[calc(100vh-2rem)] lg:w-16 lg:-translate-y-1/2 lg:rounded-[2rem] lg:border lg:border-white/10 lg:shadow-[0_10px_28px_-10px_rgb(4_38_76/0.55)] lg:[background-image:linear-gradient(180deg,rgb(255_255_255/0.09),transparent_38%)]`
            : "lg:bottom-auto lg:left-3 lg:top-1/2 lg:w-60 lg:-translate-y-1/2 lg:rounded-[1.75rem] lg:border lg:border-white/10 lg:shadow-[0_10px_28px_-10px_rgb(4_38_76/0.5)] lg:[background-image:linear-gradient(180deg,rgb(255_255_255/0.07),transparent_32%)]"
        }`}
      >
        <div ref={cabecalhoRef} className={`flex h-16 shrink-0 items-center justify-between gap-2 px-4 ${recolhida ? "lg:h-auto lg:flex-col lg:justify-center lg:gap-2 lg:px-2 lg:pb-1 lg:pt-4" : ""}`}>
          {recolhida ? (
            <>
              {/* Recolhida: a logo vira o botão de abrir a barra inteira quando o mouse passa por cima. */}
              <button
                type="button"
                aria-label="Abrir menu completo"
                title="Abrir menu completo"
                onClick={() => setFixada(true)}
                className="focus-ring group/logo relative hidden size-10 shrink-0 cursor-pointer items-center justify-center rounded-xl transition-colors hover:bg-[var(--sidebar-hover)] lg:flex"
              >
                <img src="/logo-css-white.png" alt="" className="absolute size-8 object-contain transition-all duration-200 group-hover/logo:scale-75 group-hover/logo:opacity-0 group-focus-visible/logo:opacity-0" />
                <PanelLeftOpen className="absolute size-5 scale-75 text-white opacity-0 transition-all duration-200 group-hover/logo:scale-100 group-hover/logo:opacity-100 group-focus-visible/logo:scale-100 group-focus-visible/logo:opacity-100" aria-hidden />
              </button>
              <div className="lg:hidden">
                <Logo subtitulo={emPortal ? "Portal do consultor" : "CRM comercial"} />
              </div>
            </>
          ) : (
            <Logo subtitulo={emPortal ? "Portal do consultor" : "CRM comercial"} />
          )}
          <IconButton label="Fechar menu" className="text-white/70 hover:bg-[var(--sidebar-hover)] hover:text-white lg:hidden" onClick={() => setMenuAberto(false)}>
            <X className="size-4" />
          </IconButton>
          <button
            type="button"
            aria-label="Recolher menu"
            title="Recolher menu"
            onClick={() => setFixada(false)}
            className={`focus-ring hidden size-7 shrink-0 cursor-pointer items-center justify-center rounded-md text-white transition-colors hover:bg-[var(--sidebar-hover)] ${fixada ? "lg:flex" : ""}`}
          >
            <PanelLeftClose className="size-4" />
          </button>
        </div>

        <nav
          ref={navRef}
          className={`sidebar-nav min-h-0 flex-1 overflow-y-auto px-3 pb-3 ${capsula ? "lg:px-1 lg:pt-2" : ""}`}
          aria-label="Navegação principal"
          onMouseMove={docar}
          onMouseLeave={soltarDock}
        >
          <div ref={conteudoRef}>
          {agruparNav((itensNav ?? []).filter((i) => i.chave !== "suporte"), emPortal).map((grupo, gi) => (
            <div key={grupo.rotulo} className={gi === 0 ? "" : capsula ? "mt-4 lg:mt-3" : "mt-4"}>
              {!emPortal && (
                <p className={`mb-1 px-3 text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--sidebar-fg-muted)]/70 ${capsula ? "lg:hidden" : ""}`}>{grupo.rotulo}</p>
              )}
              <div className="space-y-0.5">
                {grupo.itens.map((item) => {
                  const linhaBase = `focus-ring group relative flex h-9 items-center gap-3 rounded-lg px-3 text-[13.5px] font-medium transition-colors ${recolhida ? "lg:mx-auto lg:size-10 lg:justify-center lg:rounded-full lg:px-0" : ""}`;
                  const inativa = "text-[var(--sidebar-fg-muted)] hover:bg-[var(--sidebar-hover)] hover:text-white";
                  return (
                    <div key={item.chave} className="menu-item-anim" style={{ "--i": indiceItem++ } as React.CSSProperties}>
                      {item.rota.startsWith("/tv/") ? (
                        <a
                          href={item.rota}
                          target="_blank"
                          rel="noopener"
                          data-rotulo={`${item.rotulo} (outra aba)`}
                          title={recolhida ? undefined : "Abre em outra aba"}
                          className={`${linhaBase} ${inativa}`}
                        >
                          <item.icone className="size-[18px] shrink-0" aria-hidden />
                          <span className={recolhida ? "lg:hidden" : undefined}>{item.rotulo}</span>
                        </a>
                      ) : (
                        <NavLink
                          to={item.rota}
                          end={item.rota === "/app" || item.rota === "/app/crm" || item.rota === "/app/portal"}
                          onClick={() => setMenuAberto(false)}
                          data-rotulo={item.rotulo}
                          className={({ isActive: ativoPelaRota }) => {
                            const isActive = ativoPelaRota && item.rota === rotaAtiva;
                            return `${linhaBase} ${
                              isActive
                                ? `item-ativo bg-[var(--sidebar-bg-active)] font-semibold text-white before:absolute before:bottom-2 before:left-0 before:top-2 before:w-[3px] before:rounded-full before:bg-[var(--sidebar-accent)] ${
                                    capsula ? "lg:bg-white lg:text-[#04264c] lg:shadow-[0_6px_16px_-6px_rgb(0_0_0/0.5)] lg:before:hidden" : ""
                                  }`
                                : inativa
                            }`;
                          }}
                        >
                          <item.icone className="size-[18px] shrink-0" aria-hidden />
                          <span className={`truncate ${recolhida ? "lg:hidden" : ""}`}>{item.rotulo}</span>
                          {item.chave === "chat" && chatNaoLidas.total > 0 && (
                            <span
                              className={`ml-auto rounded-full bg-[var(--danger)] px-1.5 text-[11px] font-semibold text-white ${recolhida ? "lg:absolute lg:right-1.5 lg:top-0.5 lg:ml-0 lg:px-1 lg:text-[10px]" : ""}`}
                              aria-label={`${chatNaoLidas.total} mensagens não lidas`}
                            >
                              {rotuloNaoLidas(chatNaoLidas.total)}
                            </span>
                          )}
                        </NavLink>
                      )}
                      {item.divisor && <div className={`my-2 border-t border-[var(--sidebar-border)] ${capsula ? "lg:hidden" : ""}`} />}
                    </div>
                  );
                })}
              </div>
            </div>
          ))}
          </div>
        </nav>

      </aside>

      <div className={`flex min-h-screen min-w-0 flex-1 flex-col transition-[padding] duration-500 ease-[cubic-bezier(0.22,1,0.36,1)] [--menu-w:0px] lg:pl-[var(--menu-w)] ${colapsado ? "lg:[--menu-w:6rem]" : "lg:[--menu-w:16.5rem]"}`}>
        {/* Cabeçalho flutuante: cartão arredondado (combina com o menu), trilha da seção, busca, ações e o usuário. */}
        <header className="ui-bar sticky top-3 z-20 mx-4 mt-3 flex h-14 items-center justify-between gap-3 rounded-2xl border border-[var(--border)] bg-[color-mix(in_srgb,var(--surface)_88%,transparent)] px-2.5 shadow-[var(--shadow-card)] lg:mx-8">
          <div className="flex min-w-0 items-center gap-2">
            <IconButton label="Abrir menu" className="lg:hidden" onClick={() => setMenuAberto(true)}>
              <MenuIcon className="size-5" />
            </IconButton>
            {trilha && (
              <div className="hidden min-w-0 items-center gap-1.5 pl-2 text-sm md:flex">
                {trilha.secao && (
                  <>
                    <span className="truncate text-[var(--fg-muted)]">{trilha.secao}</span>
                    <ChevronRight className="size-3.5 shrink-0 text-[var(--fg-muted)]/60" aria-hidden />
                  </>
                )}
                <span className="truncate font-semibold text-[var(--fg)]">{trilha.titulo}</span>
              </div>
            )}
            {!noQuadroDeLeads && (
              <form onSubmit={handleBuscar} className="relative ml-2 hidden lg:block">
                <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" />
                <input
                  value={busca}
                  onChange={(e) => setBusca(e.target.value)}
                  placeholder="Buscar clientes…"
                  aria-label="Buscar clientes"
                  className="focus-ring h-9 w-64 rounded-full border-0 bg-[var(--surface-hover)] pl-10 pr-4 text-[13px] text-[var(--fg)] transition-[width] placeholder:text-[var(--fg-muted)] focus:w-80 xl:w-72 xl:focus:w-96"
                />
              </form>
            )}
          </div>

          <div className="flex shrink-0 items-center gap-1">
            <NotificacoesAtividades />
            {temChat && (
              <div className="relative">
                <IconButton label="Chat" onClick={() => navigate("/app/chat")}>
                  <MessageCircle className="size-4" />
                </IconButton>
                {chatNaoLidas.total > 0 && (
                  <span className="pointer-events-none absolute -right-0.5 -top-0.5 flex min-w-4 items-center justify-center rounded-full bg-[var(--danger)] px-1 text-[10px] font-bold leading-4 text-white">
                    {rotuloNaoLidas(chatNaoLidas.total)}
                  </span>
                )}
              </div>
            )}
            <div className="mx-1.5 hidden h-6 w-px bg-[var(--border)] sm:block" />
            <MenuDoUsuario
              nome={sessao.nomeCompleto}
              email={sessao.email}
              fotoUrl={sessao.fotoUrl}
              papel={PAPEL_LABEL[sessao.papeis[0]] ?? sessao.papeis[0]}
              temSuporte={sessao.menu.some((m) => m.chave === "suporte")}
              onSair={handleLogout}
            />
          </div>
        </header>

        <main className="min-w-0 flex-1 p-4 lg:px-8 lg:py-7">
          {!emPortal && temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial") && <AlertaDistribuicaoLimites />}
          {!emPortal && temPapel("Admin", "GestorMaster") && <AlertaEmailDoSistema />}
          {/* Cada tela entra com um fade e uma leve subida; a chave pelo caminho refaz a animação a cada troca de página. */}
          <div key={location.pathname} className="pagina-anim">
            {children}
          </div>
        </main>
        <footer className="ui-surface mx-4 mb-4 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-2.5 text-xs text-[var(--fg-muted)] lg:mx-8">
          <span>
            © {new Date().getFullYear()} <strong className="font-semibold text-[var(--fg)]">CSS Brasil</strong> · CRM comercial
          </span>
          <span className="flex items-center gap-3">
            {sessao.menu.some((m) => m.chave === "suporte") && (
              <Link to="/app/suporte" className="focus-ring rounded font-medium hover:text-[var(--brand)]">
                Suporte
              </Link>
            )}
            <span className="flex items-center gap-1.5">
              <span className="size-1.5 rounded-full bg-[var(--success)]" aria-hidden /> Sistema online
            </span>
          </span>
        </footer>
        {!emPortal && !soFinanceiro && <LembretesAdesao />}
        {!emPortal && !soFinanceiro && <NotificacaoNovosLeads usuarioId={sessao.id} />}
        {!soFinanceiro && temPapel("Comercial") && <NotificacaoAvisosPagamento usuarioId={sessao.id} />}
        {sessao.papeis.some((p) => p !== "Marketing") && <AlertaInicioCobranca usuarioId={sessao.id} />}
        {!emPortal && !soFinanceiro && <PainelLead />}
        <ConviteNotificacoesPush />
      </div>
    </div>
  );
}
