import { BellRing,
  Ban,
  FileSignature,
  Users,
  Trophy,
  ShoppingBag,
  Eye,
  ArrowDownToLine,
  ArrowUpFromLine,
  Scale,
  Building2,
  LayoutDashboard,
  ReceiptText,
  CalendarCheck,
  CalendarSync,
  Car,
  CircleCheck,
  CircleDollarSign,
  Clock,
  Ellipsis,
  Gem,
  HandCoins,
  Handshake,
  Landmark,
  Megaphone,
  Minus,
  Package,
  Pencil,
  Plus,
  Receipt,
  Search,
  SlidersHorizontal,
  ArrowUpDown,
  X,
  Repeat,
  Satellite,
  Target,
  Trash2,
  TrendingUp,
  Utensils,
  Wallet,
  Wifi,
  type LucideIcon,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, isAbortError } from "../../lib/api";
import { formatarData, formatarMoeda, hojeBrasilia } from "../../lib/format";
import { Avatar, Badge, Button, Card, ConfirmDialog, EmptyState, ErrorState, FieldError, Input, Label, Modal, MoneyInput, Select, Skeleton, Textarea, useToast } from "../../components/ui";
import { DataInput } from "../../components/PeriodoInput";
import { PortalStatCard } from "../../components/portal/PortalStatCard";
import { useAuth } from "../../context/AuthContext";
import { ContratoFinanceiro } from "../../components/portal/ContratoFinanceiro";
import { CobrancasDoConsultor } from "../../components/portal/CobrancasDoConsultor";
import { useSearchParams } from "react-router-dom";
import { abaDoFinanceiro, filtroDeVendas } from "../../lib/atalhosRapidos";
import { ignoraFiltroDeMes, rotuloDaDataDeAdesao, vendasVisiveis } from "../../lib/vendasPendentes";

// ---------- Tipos (espelham PortalFinanceiroController) ----------

type StatusRecebimento = "Pendente" | "Recebido" | "Cancelado";
type StatusPagamento = "APagar" | "Pago" | "Cancelado";

interface Venda {
  id?: string | null;
  /** Venda que veio do quadro de leads (card ganho): cliente, data e adesão são do card e não se editam aqui. */
  oportunidadeId?: string | null;
  doCard?: boolean;
  /** Carimbo de edição devolvido pela API: ao salvar, impede sobrescrever uma alteração mais nova feita em outra tela. */
  versao?: number | null;
  /** Consultor com contrato: o bônus por placa vem da gestão e não se digita. */
  bonusAutomatico?: boolean;
  /** Bônus do contrato: o previsto e o que o consultor informou ter realmente recebido (nulo = ainda não informado). */
  bonusPrevisto?: number | null;
  bonusRecebido?: number | null;
  /** CLT: adesão original do card (o que o consultor recebe é só a porcentagem dela). */
  adesaoOriginal?: number | null;
  /** Nome do consultor dono do lançamento (só nas visões da gestão). */
  consultor?: string | null;
  consultorId?: string | null;
  dataVenda: string;
  cliente: string;
  produto: string;
  valorAdesao: number;
  valorBonus: number;
  premiacoes: number;
  dataPrevistaAdesao: string | null;
  statusAdesao: StatusRecebimento;
  dataPrevistaBonus: string | null;
  statusBonus: StatusRecebimento;
  observacoes: string | null;
}

interface Custo {
  id?: string | null;
  /** Custo que veio do card (rastreador, indicação): só leitura. */
  doCard?: boolean;
  versao?: number | null;
  /** Custo fixo do contrato (coworking, MEI, transporte, notebook): só leitura. */
  fixo?: boolean;
  consultor?: string | null;
  consultorId?: string | null;
  data: string;
  categoria: string;
  descricao: string | null;
  valor: number;
  vencimento: string | null;
  status: StatusPagamento;
  dataPagamento: string | null;
  comprovanteSalvo: boolean;
}

interface Meta {
  mes: number;
  metaAdesao: number;
  metaBonus: number;
  metaRecorrencia: number;
  metaVendas: number;
  observacoes: string | null;
  /** Meta de valor e de vendas definida pela gestão na aba Metas do CRM: só leitura aqui. */
  doCrm?: boolean;
}

interface Config {
  percentualReserva: number;
  caixaInicial: number;
}

/** De quem são os números mostrados: do próprio consultor (editável), de um consultor escolhido ou da equipe toda (só leitura). */
interface VisaoFinanceiro {
  tipo: "propria" | "consultor" | "equipe";
  consultorId?: string | null;
  consultorNome?: string | null;
  somenteLeitura: boolean;
  consultores: number;
}

/** Renda fixa do mês definida pelo contrato: salário (CLT) ou recorrência (PJ). */
interface Renda {
  mes: number;
  tipo: string;
  valor: number;
  consultor?: string | null;
  consultorId?: string | null;
}

interface FinanceiroAno {
  visao?: VisaoFinanceiro | null;
  rendas?: Renda[] | null;
  tipoContrato?: "Pj" | "Clt" | null;
  ano: number;
  config: Config;
  vendas: Venda[];
  custos: Custo[];
  metas: Meta[];
}

const MESES = ["Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"];
const PRODUTOS = ["Adesão", "Recorrência", "Bônus", "Premiações", "Outros"];
const CATEGORIAS_CUSTO = [
  "Rastreador", "Gestão de Tráfego", "Custo de Leads", "Coworking", "Impostos / MEI", "Telefone / Internet",
  "Transporte / Combustível", "Material de Trabalho", "Retiradas", "Alimentação", "Indicação", "Assinaturas", "Outros",
];
const CAT_RETIRADAS = "Retiradas";
const CAT_IMPOSTOS = "Impostos / MEI";

const ROTULO_RECEBIMENTO: Record<StatusRecebimento, string> = { Pendente: "Pendente", Recebido: "Recebido", Cancelado: "Cancelado" };
const ROTULO_PAGAMENTO: Record<StatusPagamento, string> = { APagar: "A pagar", Pago: "Pago", Cancelado: "Cancelado" };

// ---------- Regras (as mesmas da planilha) ----------

type SituacaoRecebimento = "Recebido" | "Pendente" | "Atrasado" | "Cancelado" | "Sem valor";

/** Pendente vira "Atrasado" sozinho quando a data prevista já passou; valor zero não tem situação. */
function situacao(valor: number, status: StatusRecebimento, prevista: string | null, hoje: string): SituacaoRecebimento {
  if (!valor) return "Sem valor";
  if (status === "Recebido") return "Recebido";
  if (status === "Cancelado") return "Cancelado";
  return prevista && hoje > prevista ? "Atrasado" : "Pendente";
}

const mesDe = (data: string) => Number(data.slice(5, 7));

function calcVenda(v: Venda, hoje: string) {
  const sa = situacao(v.valorAdesao, v.statusAdesao, v.dataPrevistaAdesao, hoje);
  const sb = situacao(v.valorBonus, v.statusBonus, v.dataPrevistaBonus, hoje);
  const aberto = (s: SituacaoRecebimento) => s === "Pendente" || s === "Atrasado";
  const recebidoAdesao = sa === "Recebido" ? v.valorAdesao : 0;
  const recebidoBonus = sb === "Recebido" ? v.valorBonus : 0;
  const pendenteAdesao = aberto(sa) ? v.valorAdesao : 0;
  const pendenteBonus = aberto(sb) ? v.valorBonus : 0;
  const recebido = recebidoAdesao + recebidoBonus + v.premiacoes;
  const pendente = pendenteAdesao + pendenteBonus;
  const diasAtraso = Math.max(
    sa === "Atrasado" && v.dataPrevistaAdesao ? diasEntre(v.dataPrevistaAdesao, hoje) : 0,
    sb === "Atrasado" && v.dataPrevistaBonus ? diasEntre(v.dataPrevistaBonus, hoje) : 0
  );
  return { sa, sb, recebido, pendente, diasAtraso, recebidoAdesao, recebidoBonus, pendenteAdesao, pendenteBonus };
}

/**
 * Mês em que o bônus entra no controle: o bônus do contrato é pago 2 meses depois da venda (venda de janeiro = bônus em março), então vale o mês da data
 * prevista de pagamento. Quando esse mês cai em outro ano, o bônus entra na renda daquele ano. Bônus lançado à mão fica no mês da venda.
 */
function mesDoBonus(v: Venda, ano: number): number | null {
  if (v.bonusAutomatico && v.dataPrevistaBonus) return Number(v.dataPrevistaBonus.slice(0, 4)) === ano ? mesDe(v.dataPrevistaBonus) : null;
  return mesDe(v.dataVenda);
}

/** Bônus do contrato cujo mês de pagamento já chegou e o consultor ainda não informou quanto realmente recebeu. */
function bonusAguardandoInforme(v: Venda, hoje: string) {
  return !!v.bonusAutomatico && v.statusBonus !== "Cancelado" && (v.bonusPrevisto ?? 0) > 0 && v.bonusRecebido == null && !!v.dataPrevistaBonus && hoje.slice(0, 7) >= v.dataPrevistaBonus.slice(0, 7);
}

function diasEntre(de: string, ate: string) {
  return Math.round((Date.parse(`${ate}T00:00:00Z`) - Date.parse(`${de}T00:00:00Z`)) / 86_400_000);
}

interface LinhaMes {
  mes: number;
  fixa: number;
  meta: number;
  vendas: number;
  adesao: number;
  bonus: number;
  premiacoes: number;
  receitas: number;
  custos: number;
  retiradas: number;
  lucro: number;
  recebido: number;
  pendente: number;
  imposto: number;
  reserva: number;
  caixa: number;
  deltaMeta: number;
  pctMeta: number;
  margem: number;
}

/**
 * Consolida o ano como o DASHBOARD da planilha. Custos cancelados e valores de vendas canceladas ficam de fora; "Custos totais" não inclui
 * as retiradas (que aparecem à parte). O caixa acumula o que foi de fato recebido menos o que foi de fato pago (custos pagos + retiradas pagas).
 */
function consolidar(d: FinanceiroAno, hoje: string): LinhaMes[] {
  let caixa = d.config.caixaInicial;
  return MESES.map((_, i) => {
    const mes = i + 1;
    const meta = d.metas.find((m) => m.mes === mes);
    const vs = d.vendas.filter((v) => mesDe(v.dataVenda) === mes);
    const vb = d.vendas.filter((v) => mesDoBonus(v, d.ano) === mes); // bônus pelo mês em que é pago
    const cs = d.custos.filter((c) => mesDe(c.data) === mes && c.status !== "Cancelado");
    const adesao = vs.reduce((n, v) => n + (v.statusAdesao === "Cancelado" ? 0 : v.valorAdesao), 0);
    const bonus = vb.reduce((n, v) => n + (v.statusBonus === "Cancelado" ? 0 : v.valorBonus), 0);
    const premiacoes = vs.reduce((n, v) => n + v.premiacoes, 0);
    const fixa = (d.rendas ?? []).filter((r) => r.mes === mes).reduce((n, r) => n + r.valor, 0);
    const receitas = adesao + bonus + premiacoes + fixa;
    const retiradas = cs.filter((c) => c.categoria === CAT_RETIRADAS).reduce((n, c) => n + c.valor, 0);
    const custos = cs.filter((c) => c.categoria !== CAT_RETIRADAS).reduce((n, c) => n + c.valor, 0);
    const imposto = cs.filter((c) => c.categoria === CAT_IMPOSTOS).reduce((n, c) => n + c.valor, 0);
    const pago = cs.filter((c) => c.status === "Pago").reduce((n, c) => n + c.valor, 0);
    const calcAdesao = vs.map((v) => calcVenda(v, hoje));
    const calcBonus = vb.map((v) => calcVenda(v, hoje));
    const recebido = calcAdesao.reduce((n, c) => n + c.recebidoAdesao, 0) + premiacoes + calcBonus.reduce((n, c) => n + c.recebidoBonus, 0) + fixa;
    const pendente = calcAdesao.reduce((n, c) => n + c.pendenteAdesao, 0) + calcBonus.reduce((n, c) => n + c.pendenteBonus, 0);
    const lucro = receitas - custos;
    caixa += recebido - pago;
    const metaTotal = (meta?.metaAdesao ?? 0) + (meta?.metaBonus ?? 0) + (meta?.metaRecorrencia ?? 0);
    return {
      mes,
      fixa,
      meta: metaTotal,
      vendas: vs.filter((v) => v.valorAdesao > 0 || v.valorBonus > 0).length,
      adesao, bonus, premiacoes, receitas, custos, retiradas, lucro, recebido, pendente, imposto,
      reserva: Math.max(0, lucro) * d.config.percentualReserva,
      caixa,
      deltaMeta: adesao + bonus - metaTotal,
      pctMeta: metaTotal > 0 ? (adesao + bonus) / metaTotal : 0,
      margem: receitas > 0 ? lucro / receitas : 0,
    };
  });
}

const soma = (linhas: LinhaMes[], k: keyof LinhaMes) => linhas.reduce((n, l) => n + (l[k] as number), 0);
const pct = (v: number) => `${(v * 100).toLocaleString("pt-BR", { maximumFractionDigits: 1 })}%`;

function BadgeSituacao({ s }: { s: SituacaoRecebimento }) {
  if (s === "Sem valor") return <span className="text-[var(--fg-muted)]">—</span>;
  const variante = { Recebido: "success", Pendente: "warning", Atrasado: "danger", Cancelado: "neutral" } as const;
  return <Badge variant={variante[s]}>{s}</Badge>;
}

// ---------- Barras (abas, resumo e divisão) ----------

const ABAS: { chave: string; rotulo: string; icone: LucideIcon }[] = [
  { chave: "resumo", rotulo: "Resumo", icone: LayoutDashboard },
  { chave: "vendas", rotulo: "Vendas", icone: ReceiptText },
  { chave: "custos", rotulo: "Custos", icone: Wallet },
  { chave: "metas", rotulo: "Metas", icone: Target },
  { chave: "cobrancas", rotulo: "Cobranças", icone: BellRing },
  { chave: "contrato", rotulo: "Contrato", icone: FileSignature },
];

/** Barra de abas em pílula, com ícone e contador. */
function BarraAbas({ ativa, onChange, contagens, ocultas = [] }: { ativa: string; onChange: (chave: string) => void; contagens: Record<string, number | undefined>; ocultas?: string[] }) {
  return (
    <div role="tablist" className="inline-flex max-w-full gap-1 overflow-x-auto rounded-full border border-[var(--border)] bg-[var(--surface)] p-1 shadow-[var(--shadow-card)]">
      {ABAS.filter((a) => !ocultas.includes(a.chave)).map(({ chave, rotulo, icone: Icone }) => {
        const ativo = chave === ativa;
        const n = contagens[chave];
        return (
          <button
            key={chave}
            role="tab"
            aria-selected={ativo}
            onClick={() => onChange(chave)}
            className={`focus-ring inline-flex cursor-pointer items-center gap-2 whitespace-nowrap rounded-full px-4 py-2 text-sm font-semibold transition-all duration-200 ${
              ativo ? "bg-[var(--brand)] text-white shadow-[0_6px_16px_-8px_var(--brand)]" : "text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
            }`}
          >
            <Icone className="size-4" aria-hidden />
            {rotulo}
            {n !== undefined && (
              <span className={`rounded-full px-1.5 text-[11px] font-bold leading-5 [font-variant-numeric:tabular-nums] ${ativo ? "bg-white/25 text-white" : "bg-[var(--surface-hover)] text-[var(--fg-muted)]"}`}>{n}</span>
            )}
          </button>
        );
      })}
    </div>
  );
}

interface Segmento {
  rotulo: string;
  valor: number;
  cor: string;
  icone?: LucideIcon;
}

/** Cartão de resumo: números em destaque e uma barra proporcional dividida em segmentos, com legenda. */
function BarraResumo({ metricas, segmentos, titulo }: { metricas: { rotulo: string; valor: string; cor?: string }[]; segmentos: Segmento[]; titulo: string }) {
  const total = segmentos.reduce((n, x) => n + x.valor, 0);
  return (
    <Card className="p-4">
      <div className="flex flex-wrap gap-x-8 gap-y-3">
        {metricas.map((m) => (
          <div key={m.rotulo}>
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{m.rotulo}</p>
            <p className="text-xl font-bold leading-tight tracking-tight [font-variant-numeric:tabular-nums]" style={{ color: m.cor ?? "var(--fg)" }}>{m.valor}</p>
          </div>
        ))}
      </div>

      <div className="mt-4" role="img" aria-label={titulo}>
        <div className="flex h-3 w-full gap-0.5 overflow-hidden rounded-full bg-[var(--surface-hover)]">
          {total > 0 &&
            segmentos.filter((x) => x.valor > 0).map((x) => (
              <div
                key={x.rotulo}
                title={`${x.rotulo}: ${formatarMoeda(x.valor)} (${pct(x.valor / total)})`}
                className="h-full min-w-1 first:rounded-l-full last:rounded-r-full transition-[flex-grow] duration-500"
                style={{ flexGrow: x.valor, backgroundColor: x.cor }}
              />
            ))}
        </div>
        {total > 0 && (
          <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5">
            {segmentos.filter((x) => x.valor > 0).map((x) => {
              const Icone = x.icone;
              return (
                <li key={x.rotulo} className="flex items-center gap-1.5 text-xs text-[var(--fg-muted)]">
                  <span className="size-2.5 rounded-full" style={{ backgroundColor: x.cor }} aria-hidden />
                  {Icone && <Icone className="size-3.5" aria-hidden />}
                  {x.rotulo} <strong className="text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarMoeda(x.valor)}</strong>
                  <span className="opacity-70">{pct(x.valor / total)}</span>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </Card>
  );
}

// ---------- Página ----------

export function PortalControleFinanceiroPage() {
  const { notificar } = useToast();
  const hoje = hojeBrasilia();
  const [ano, setAno] = useState(Number(hoje.slice(0, 4)));
  const [dados, setDados] = useState<FinanceiroAno | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  // Vindo de um atalho do Portal (?aba=vendas&filtro=pendente): já abre na aba e no atalho de situação pedidos.
  const [paramsDaUrl] = useSearchParams();
  const [aba, setAba] = useState(() => abaDoFinanceiro(paramsDaUrl));
  const [filtroInicialDeVendas] = useState(() => filtroDeVendas(paramsDaUrl));
  // Ao entrar, já mostra o mês atual (quando o ano é o corrente); "Todos os meses" fica a um clique.
  const [mes, setMes] = useState(Number(hoje.slice(5, 7)));
  // Gestão (como no Relatório comercial): escolhe um consultor ou a equipe toda. Vazio = os próprios números.
  const { temPapel, sessao } = useAuth();
  const podeVerEquipe = temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial");
  const [consultor, setConsultor] = useState("");
  const [consultores, setConsultores] = useState<{ id: string; nome: string; ativo: boolean }[]>([]);
  useEffect(() => {
    if (!podeVerEquipe) return;
    api.get<{ id: string; nome: string; ativo: boolean }[]>("/portal/financeiro/consultores").then(setConsultores).catch(() => setConsultores([]));
  }, [podeVerEquipe]);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setErro(null);
      api
        .get<FinanceiroAno>(`/portal/financeiro?ano=${ano}${consultor ? `&consultor=${encodeURIComponent(consultor)}` : ""}`, signal)
        .then(setDados)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o controle financeiro.");
        });
    },
    [ano, consultor]
  );

  useEffect(() => {
    setDados(null);
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  const somenteLeitura = dados?.visao?.somenteLeitura ?? false;
  const abrirConsultor = (id: string) => { setConsultor(id); setAba("resumo"); };
  useEffect(() => { if (consultor === "equipe" && (aba === "contrato" || aba === "cobrancas")) setAba("resumo"); }, [consultor, aba]);
  const linhas = useMemo(() => (dados ? consolidar(dados, hoje) : []), [dados, hoje]);
  const falha = (e: unknown) => notificar("error", e instanceof Error ? e.message : "Não foi possível salvar.");

  const anos = [ano - 1, ano, ano + 1];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold tracking-tight text-[var(--fg)]">Controle financeiro</h1>
          <p className="text-sm text-[var(--fg-muted)]">Suas vendas, recebimentos, custos e metas — só você enxerga estas informações.</p>
        </div>
        <div className="filtros-painel flex items-center gap-2">
        {podeVerEquipe && (
          <div className="w-60">
            <Select aria-label="Consultor" value={consultor} onChange={(e) => setConsultor(e.target.value)}>
              <option value="">Meus números</option>
              <option value="equipe">Toda a equipe</option>
              {consultores.map((c) => (
                <option key={c.id} value={c.id}>{c.nome}</option>
              ))}
            </Select>
          </div>
        )}
        <FiltroMes valor={mes} onChange={setMes} />
        <div className="w-28">
          <Select aria-label="Ano" value={ano} onChange={(e) => { const a = Number(e.target.value); setAno(a); setMes(a === Number(hoje.slice(0, 4)) ? Number(hoje.slice(5, 7)) : 0); }}>
            {anos.map((a) => (
              <option key={a} value={a}>{a}</option>
            ))}
          </Select>
        </div>
        </div>
      </div>

      {dados?.visao?.somenteLeitura && (
        <div className="flex items-center gap-2 rounded-xl border border-[var(--border)] bg-[var(--brand-soft)] px-3 py-2 text-sm text-[var(--fg)]">
          <Eye className="size-4 shrink-0 text-[var(--brand)]" aria-hidden />
          <span>
            Visão da gestão, somente leitura:{" "}
            <strong>{dados.visao.tipo === "equipe" ? `toda a equipe (${dados.visao.consultores} consultores)` : dados.visao.consultorNome}</strong>.
          </span>
          {dados.visao.tipo === "consultor" && (
            <button type="button" onClick={() => setConsultor("equipe")} className="focus-ring ml-auto cursor-pointer rounded-full border border-[var(--border)] px-3 py-1 text-xs font-semibold text-[var(--brand)] hover:bg-[var(--surface-hover)]">
              ← Voltar para a equipe
            </button>
          )}
        </div>
      )}

      <BarraAbas ativa={aba} onChange={setAba} contagens={{ vendas: dados?.vendas.length, custos: dados?.custos.length }} ocultas={dados?.visao?.tipo === "equipe" ? ["contrato", "cobrancas"] : []} />

      {erro ? (
        <ErrorState message={erro} onRetry={() => carregar()} />
      ) : !dados ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-28 rounded-2xl" />
          ))}
        </div>
      ) : aba === "resumo" ? (
        <Resumo dados={dados} linhas={linhas} mes={mes} somenteLeitura={somenteLeitura} equipe={dados.visao?.tipo === "equipe"} hoje={hoje} onAbrirConsultor={abrirConsultor} onSalvarConfig={async (c) => {
          try {
            await api.put(`/portal/financeiro/config?ano=${ano}`, c);
            setDados({ ...dados, config: c });
          } catch (e) { falha(e); }
        }} />
      ) : aba === "vendas" ? (
        <Vendas filtroInicial={filtroInicialDeVendas} dados={dados} mes={mes} somenteLeitura={somenteLeitura} onAbrirConsultor={abrirConsultor} hoje={hoje} ano={ano} recarregar={() => carregar()} onErro={falha} />
      ) : aba === "cobrancas" && dados.visao?.tipo !== "equipe" ? (
        <CobrancasDoConsultor consultorId={dados.visao?.consultorId ?? sessao?.id ?? ""} onMudou={() => carregar()} />
      ) : aba === "contrato" && dados.visao?.tipo !== "equipe" ? (
        <ContratoFinanceiro consultorId={dados.visao?.consultorId ?? sessao?.id ?? ""} ano={ano} onMudou={() => carregar()} />
      ) : aba === "custos" ? (
        <Custos dados={dados} mes={mes} somenteLeitura={somenteLeitura} onAbrirConsultor={abrirConsultor} hoje={hoje} ano={ano} recarregar={() => carregar()} onErro={falha} />
      ) : (
        <Metas dados={dados} linhas={linhas} mes={mes} somenteLeitura={somenteLeitura} ano={ano} recarregar={() => carregar()} onErro={falha} onOk={() => notificar("success", "Metas salvas.")} />
      )}
    </div>
  );
}

// ---------- Resumo ----------

function Resumo({ dados, linhas, mes, somenteLeitura, equipe, hoje, onAbrirConsultor, onSalvarConfig }: { dados: FinanceiroAno; linhas: LinhaMes[]; mes: number; somenteLeitura: boolean; equipe: boolean; hoje: string; onAbrirConsultor: (id: string) => void; onSalvarConfig: (c: Config) => void }) {
  const mesAtual = Number(hojeBrasilia().slice(5, 7));
  // Cartões do período escolhido (um mês ou o ano todo); a tabela abaixo continua mostrando os 12 meses.
  const sel = mes ? [linhas[mes - 1]] : linhas;
  const periodo = mes ? MESES[mes - 1] : `${dados.ano}`;
  const metaTotal = soma(sel, "meta");
  const realMeta = soma(sel, "adesao") + soma(sel, "bonus");
  const receitas = soma(sel, "receitas");
  const custos = soma(sel, "custos") + soma(sel, "retiradas");
  const [reserva, setReserva] = useState(dados.config.percentualReserva * 100);
  const [caixa, setCaixa] = useState<number | null>(dados.config.caixaInicial);

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <PortalStatCard titulo={`Receitas — ${periodo}`} valor={formatarMoeda(receitas)} icone={CircleDollarSign} subtitulo="Adesão + bônus/recorrência + premiações" />
        <PortalStatCard titulo={`Custos — ${periodo}`} valor={formatarMoeda(custos)} icone={Wallet} subtitulo={`${formatarMoeda(soma(sel, "retiradas"))} em retiradas`} />
        <PortalStatCard titulo="Lucro líquido" valor={formatarMoeda(soma(sel, "lucro"))} icone={TrendingUp} subtitulo={`Margem ${pct(receitas > 0 ? soma(sel, "lucro") / receitas : 0)}`} />
        {!equipe && <PortalStatCard titulo="Reserva p/ investir" valor={formatarMoeda(soma(sel, "reserva"))} icone={Gem} subtitulo={`${pct(dados.config.percentualReserva)} do lucro de cada mês`} />}
        <PortalStatCard titulo="Já recebido" valor={formatarMoeda(soma(sel, "recebido"))} icone={CircleDollarSign} />
        <PortalStatCard titulo="Pendente de recebimento" valor={formatarMoeda(soma(sel, "pendente"))} icone={Clock} />
        {dados.tipoContrato && <PortalStatCard titulo={`Renda fixa — ${periodo}`} valor={formatarMoeda(soma(sel, "fixa"))} icone={FileSignature} subtitulo={dados.tipoContrato === "Clt" ? "Salário do contrato CLT" : "Recorrência do contrato PJ"} />}
        <PortalStatCard titulo="Imposto pago" valor={formatarMoeda(soma(sel, "imposto"))} icone={Landmark} />
        <PortalStatCard
          titulo={`Meta — ${periodo}`}
          valor={metaTotal > 0 ? pct(realMeta / metaTotal) : "—"}
          icone={Target}
          progresso={metaTotal > 0 ? (realMeta / metaTotal) * 100 : 0}
          subtitulo={metaTotal > 0 ? `${formatarMoeda(realMeta)} de ${formatarMoeda(metaTotal)}` : "Defina a meta na aba Metas"}
        />
      </div>

      {equipe && <CartoesDosConsultores dados={dados} mes={mes} hoje={hoje} onAbrir={onAbrirConsultor} />}

      {!somenteLeitura && <Card className="filtros-painel p-4">
        <p className="mb-3 text-sm font-semibold text-[var(--fg)]">Configurações de {dados.ano}</p>
        <div className="flex flex-wrap items-end gap-3">
          <div className="w-36">
            <Label htmlFor="cf-reserva">% de reserva</Label>
            <Input id="cf-reserva" type="number" min={0} max={100} step={1} value={reserva} onChange={(e) => setReserva(Number(e.target.value))} />
          </div>
          <div className="w-48">
            <Label htmlFor="cf-caixa">Caixa inicial</Label>
            <MoneyInput id="cf-caixa" value={caixa} onChange={setCaixa} />
          </div>
          <Button size="sm" onClick={() => onSalvarConfig({ percentualReserva: Math.min(100, Math.max(0, reserva)) / 100, caixaInicial: caixa ?? 0 })}>
            Salvar
          </Button>
        </div>
      </Card>}

      <Card className="overflow-x-auto">
        <table className="w-full min-w-[60rem] text-sm [font-variant-numeric:tabular-nums]">
          <thead>
            <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
              {["Mês", "Meta", "Vendas", "Adesão", "Bônus/Recorr.", "Premiações", "Receitas", "Custos", "Retiradas", "Lucro", "Recebido", "Pendente", "% Meta", ...(equipe ? [] : ["Reserva"]), "Caixa"].map((h, i) => (
                <th key={h} className={`px-3 py-2 font-medium ${i ? "text-right" : ""}`}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {linhas.map((l) => (
              <tr key={l.mes} className={`border-b border-[var(--border)] last:border-0 ${l.mes === mesAtual ? "bg-[var(--brand-soft)]" : ""}`}>
                <td className="px-3 py-2 font-medium text-[var(--fg)]">{MESES[l.mes - 1]}</td>
                <td className="px-3 py-2 text-right">{l.meta ? formatarMoeda(l.meta) : "—"}</td>
                <td className="px-3 py-2 text-right">{l.vendas}</td>
                <td className="px-3 py-2 text-right">{formatarMoeda(l.adesao)}</td>
                <td className="px-3 py-2 text-right">{formatarMoeda(l.bonus)}</td>
                <td className="px-3 py-2 text-right">{formatarMoeda(l.premiacoes)}</td>
                <td className="px-3 py-2 text-right font-semibold">{formatarMoeda(l.receitas)}</td>
                <td className="px-3 py-2 text-right">{formatarMoeda(l.custos)}</td>
                <td className="px-3 py-2 text-right">{formatarMoeda(l.retiradas)}</td>
                <td className={`px-3 py-2 text-right font-semibold ${l.lucro < 0 ? "text-[var(--danger)]" : "text-[var(--success)]"}`}>{formatarMoeda(l.lucro)}</td>
                <td className="px-3 py-2 text-right">{formatarMoeda(l.recebido)}</td>
                <td className="px-3 py-2 text-right">{formatarMoeda(l.pendente)}</td>
                <td className="px-3 py-2 text-right">{l.meta ? pct(l.pctMeta) : "—"}</td>
                {!equipe && <td className="px-3 py-2 text-right">{formatarMoeda(l.reserva)}</td>}
                <td className="px-3 py-2 text-right">{formatarMoeda(l.caixa)}</td>
              </tr>
            ))}
            <tr className="border-t-2 border-[var(--border)] font-semibold text-[var(--fg)]">
              <td className="px-3 py-2">Total</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "meta"))}</td>
              <td className="px-3 py-2 text-right">{soma(linhas, "vendas")}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "adesao"))}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "bonus"))}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "premiacoes"))}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(receitas)}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "custos"))}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "retiradas"))}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "lucro"))}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "recebido"))}</td>
              <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "pendente"))}</td>
              <td className="px-3 py-2 text-right">{pct(soma(linhas, "meta") > 0 ? (soma(linhas, "adesao") + soma(linhas, "bonus")) / soma(linhas, "meta") : 0)}</td>
              {!equipe && <td className="px-3 py-2 text-right">{formatarMoeda(soma(linhas, "reserva"))}</td>}
              <td className="px-3 py-2 text-right">{formatarMoeda(linhas[11].caixa)}</td>
            </tr>
          </tbody>
        </table>
      </Card>
      <p className="text-xs text-[var(--fg-muted)]">
        Receitas e custos consideram o mês da data da venda/do custo; valores cancelados ficam de fora. O caixa soma o que você já recebeu e desconta o que já pagou (incluindo retiradas).
      </p>
    </div>
  );
}

// ---------- Filtro de mês ----------

function FiltroMes({ valor, onChange }: { valor: number; onChange: (m: number) => void }) {
  return (
    <div className="w-36">
      <Select aria-label="Mês" value={valor} onChange={(e) => onChange(Number(e.target.value))}>
        <option value={0}>Todos os meses</option>
        {MESES.map((m, i) => (
          <option key={m} value={i + 1}>{m}</option>
        ))}
      </Select>
    </div>
  );
}

// ---------- Equipe: custos separados por consultor ----------

/** Consultores que aparecem nos lançamentos (id e nome), em ordem alfabética — alimenta o filtro por consultor. */
function opcoesDeConsultor(itens: { consultorId?: string | null; consultor?: string | null }[]): [string, string][] {
  const mapa = new Map<string, string>();
  for (const i of itens) if (i.consultorId && i.consultor) mapa.set(i.consultorId, i.consultor);
  return [...mapa.entries()].sort((a, b) => a[1].localeCompare(b[1], "pt-BR"));
}

function BotaoConsultor({ nome, onClick }: { nome: string; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} title="Abrir o controle financeiro completo deste consultor" className="focus-ring cursor-pointer rounded text-left font-medium text-[var(--brand)] underline-offset-2 hover:underline">
      {nome}
    </button>
  );
}

/**
 * Na visão da equipe, os custos separados por consultor: quanto cada um gastou, quanto ganhou e o lucro no período escolhido.
 * Clicar em um consultor abre o relatório completo dele (custos e ganhos, todas as abas).
 */
interface ResumoConsultor {
  id: string;
  nome: string;
  custos: number;
  pagos: number;
  aPagar: number;
  ganho: number;
  recebido: number;
  pendente: number;
  vendas: number;
}

/** Números de cada consultor no período (um mês ou o ano): custos, ganho, recebido, pendente e nº de vendas. Base da lista de custos e dos cartões do Resumo. */
function resumirPorConsultor(dados: FinanceiroAno, mes: number, hoje: string): ResumoConsultor[] {
  const por = new Map<string, ResumoConsultor>();
  const item = (id: string, nome: string) => {
    let l = por.get(id);
    if (!l) por.set(id, (l = { id, nome, custos: 0, pagos: 0, aPagar: 0, ganho: 0, recebido: 0, pendente: 0, vendas: 0 }));
    return l;
  };
  for (const c of dados.custos) {
    if (!c.consultorId || c.status === "Cancelado" || (mes && mesDe(c.data) !== mes)) continue;
    const l = item(c.consultorId, c.consultor ?? "—");
    l.custos += c.valor;
    if (c.status === "Pago") l.pagos += c.valor; else l.aPagar += c.valor;
  }
  for (const v of dados.vendas) {
    if (!v.consultorId || (mes && mesDe(v.dataVenda) !== mes)) continue;
    const l = item(v.consultorId, v.consultor ?? "—");
    const calc = calcVenda(v, hoje);
    l.ganho += (v.statusAdesao === "Cancelado" ? 0 : v.valorAdesao) + (v.statusBonus === "Cancelado" ? 0 : v.valorBonus) + v.premiacoes;
    l.recebido += calc.recebido;
    l.pendente += calc.pendente;
    if (v.valorAdesao > 0 || v.valorBonus > 0) l.vendas += 1;
  }
  for (const r of dados.rendas ?? []) {
    if (!r.consultorId || (mes && r.mes !== mes)) continue;
    const l = item(r.consultorId, r.consultor ?? "—");
    l.ganho += r.valor;
    l.recebido += r.valor;
  }
  return [...por.values()];
}

/**
 * Na visão da equipe, os custos separados por consultor: quanto cada um gastou, quanto ganhou e o lucro no período escolhido.
 * Clicar em um consultor abre o relatório completo dele (custos e ganhos, todas as abas).
 */
function CustosPorConsultor({ dados, mes, hoje, onAbrir }: { dados: FinanceiroAno; mes: number; hoje: string; onAbrir: (id: string) => void }) {
  const linhas = useMemo(
    () => resumirPorConsultor(dados, mes, hoje).sort((a, b) => b.custos - a.custos || a.nome.localeCompare(b.nome, "pt-BR")),
    [dados, mes, hoje]
  );

  const total = (k: "custos" | "ganho" | "recebido") => linhas.reduce((n, l) => n + l[k], 0);
  const maior = Math.max(1, ...linhas.map((l) => l.custos));

  if (linhas.length === 0) return null;
  return (
    <Card className="overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--border)] px-4 py-3">
        <p className="flex items-center gap-2 text-base font-semibold text-[var(--fg)]"><Users className="size-5 text-[var(--brand)]" aria-hidden /> Custos por consultor</p>
        <p className="text-xs text-[var(--fg-muted)]">Clique em um consultor para ver o relatório completo dele.</p>
      </div>
      <div className="max-h-[26rem] overflow-auto">
        <table className="w-full min-w-[40rem] text-sm [font-variant-numeric:tabular-nums]">
          <thead>
            <tr className="text-left">
              {["Consultor", "Custos", "A pagar", "Ganho", "Já recebido", "Lucro"].map((h, i) => (
                <th key={h} className={`sticky top-0 z-10 whitespace-nowrap border-b border-[var(--border)] bg-[var(--surface-solid)] px-4 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)] ${i ? "text-right" : ""}`}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {linhas.map((l) => {
              const lucro = l.ganho - l.custos;
              return (
                <tr key={l.id} onClick={() => onAbrir(l.id)} className="cursor-pointer border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--brand-soft)]">
                  <td className="min-w-52 px-4 py-2.5">
                    <button type="button" onClick={(e) => { e.stopPropagation(); onAbrir(l.id); }} className="focus-ring cursor-pointer rounded text-left font-semibold text-[var(--fg)] hover:text-[var(--brand)]">{l.nome}</button>
                    <div className="mt-1.5 h-1.5 w-full max-w-56 overflow-hidden rounded-full bg-[var(--surface-hover)]" aria-hidden>
                      <div className="h-full rounded-full bg-[var(--danger)]" style={{ width: `${(l.custos / maior) * 100}%` }} />
                    </div>
                  </td>
                  <td className="px-4 py-2.5 text-right font-medium">{formatarMoeda(l.custos)}</td>
                  <td className="px-4 py-2.5 text-right text-[var(--warning)]">{l.aPagar ? formatarMoeda(l.aPagar) : "—"}</td>
                  <td className="px-4 py-2.5 text-right">{formatarMoeda(l.ganho)}</td>
                  <td className="px-4 py-2.5 text-right text-[var(--success)]">{formatarMoeda(l.recebido)}</td>
                  <td className={`px-4 py-2.5 text-right font-semibold ${lucro < 0 ? "text-[var(--danger)]" : "text-[var(--success)]"}`}>{formatarMoeda(lucro)}</td>
                </tr>
              );
            })}
          </tbody>
          <tfoot>
            <tr className="font-semibold text-[var(--fg)]">
              <td className="px-4 py-2.5">Total ({linhas.length} consultores)</td>
              <td className="px-4 py-2.5 text-right">{formatarMoeda(total("custos"))}</td>
              <td className="px-4 py-2.5 text-right">{formatarMoeda(linhas.reduce((n, l) => n + l.aPagar, 0))}</td>
              <td className="px-4 py-2.5 text-right">{formatarMoeda(total("ganho"))}</td>
              <td className="px-4 py-2.5 text-right">{formatarMoeda(total("recebido"))}</td>
              <td className="px-4 py-2.5 text-right">{formatarMoeda(total("ganho") - total("custos"))}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </Card>
  );
}

/**
 * Cartões dos consultores no Resumo da equipe: quem mais ganhou, quem mais vendeu, quem mais gastou e um cartão por consultor
 * (ganho, custos, lucro, vendas e quanto do ganho já foi recebido). Clicar em um cartão abre o relatório completo dele.
 */
function CartoesDosConsultores({ dados, mes, hoje, onAbrir }: { dados: FinanceiroAno; mes: number; hoje: string; onAbrir: (id: string) => void }) {
  const [todos, setTodos] = useState(false);
  const periodo = mes ? `${MESES[mes - 1]}/${dados.ano}` : `${dados.ano}`;
  const consultores = useMemo(
    () => resumirPorConsultor(dados, mes, hoje).filter((c) => c.ganho > 0 || c.custos > 0).sort((a, b) => b.ganho - a.ganho || a.nome.localeCompare(b.nome, "pt-BR")),
    [dados, mes, hoje]
  );
  if (consultores.length === 0) return null;

  const melhorGanho = consultores[0];
  const maisVendas = [...consultores].sort((a, b) => b.vendas - a.vendas)[0];
  const maiorCusto = [...consultores].sort((a, b) => b.custos - a.custos)[0];
  const exibidos = todos ? consultores : consultores.slice(0, 8);

  const Destaque = ({ titulo, nome, valor, icone: Icone }: { titulo: string; nome: string; valor: string; icone: LucideIcon }) => (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface-hover)] px-3 py-2.5">
      <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand)]"><Icone className="size-4" aria-hidden /></span>
      <div className="min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{titulo}</p>
        <p className="truncate text-sm font-semibold text-[var(--fg)]" title={nome}>{nome}</p>
        <p className="text-xs text-[var(--fg-muted)] [font-variant-numeric:tabular-nums]">{valor}</p>
      </div>
    </div>
  );

  return (
    <section className="space-y-3" aria-label="Consultores">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h2 className="flex items-center gap-2 text-base font-semibold text-[var(--fg)]"><Users className="size-5 text-[var(--brand)]" aria-hidden /> Consultores — {periodo}</h2>
          <p className="text-xs text-[var(--fg-muted)]">{consultores.length} consultor(es) com movimento no período. Clique em um cartão para ver o relatório completo.</p>
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <Destaque titulo="Maior ganho" nome={melhorGanho.nome} valor={formatarMoeda(melhorGanho.ganho)} icone={Trophy} />
        <Destaque titulo="Mais vendas" nome={maisVendas.nome} valor={`${maisVendas.vendas} venda(s)`} icone={ShoppingBag} />
        <Destaque titulo="Maior custo" nome={maiorCusto.nome} valor={formatarMoeda(maiorCusto.custos)} icone={Wallet} />
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {exibidos.map((c, i) => {
          const lucro = c.ganho - c.custos;
          const pctRecebido = c.ganho > 0 ? Math.min(100, (c.recebido / c.ganho) * 100) : 0;
          return (
            <button
              key={c.id}
              type="button"
              onClick={() => onAbrir(c.id)}
              title="Abrir o controle financeiro completo deste consultor"
              className="ui-surface focus-ring group cursor-pointer rounded-[var(--radius-card)] border border-[var(--border)] bg-[var(--surface)] p-4 text-left shadow-[var(--shadow-card)] transition-[transform,box-shadow,border-color] duration-200 hover:-translate-y-0.5 hover:border-[var(--brand)]"
            >
              <div className="flex items-center gap-3">
                <Avatar nome={c.nome} usuarioId={c.id} className="size-10 text-sm" />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-semibold text-[var(--fg)] group-hover:text-[var(--brand)]" title={c.nome}>{c.nome}</p>
                  <p className="text-xs text-[var(--fg-muted)]">{c.vendas} venda(s)</p>
                </div>
                {i < 3 && !todos && <span className="rounded-full bg-[var(--brand-soft)] px-2 py-0.5 text-[11px] font-bold text-[var(--brand)]">#{i + 1}</span>}
              </div>

              <p className="mt-3 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Ganho</p>
              <p className="text-xl font-bold leading-tight tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarMoeda(c.ganho)}</p>

              <div className="mt-2 h-1.5 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]" role="img" aria-label={`${Math.round(pctRecebido)}% do ganho já recebido`}>
                <div className="h-full rounded-full bg-[var(--success)]" style={{ width: `${pctRecebido}%` }} />
              </div>
              <p className="mt-1 text-[11px] text-[var(--fg-muted)]">{Math.round(pctRecebido)}% recebido{c.pendente > 0 ? ` · ${formatarMoeda(c.pendente)} pendente` : ""}</p>

              <div className="mt-3 grid grid-cols-2 gap-2 border-t border-[var(--border)] pt-3 text-xs [font-variant-numeric:tabular-nums]">
                <div>
                  <p className="text-[var(--fg-muted)]">Custos</p>
                  <p className="font-semibold text-[var(--fg)]">{formatarMoeda(c.custos)}</p>
                </div>
                <div>
                  <p className="text-[var(--fg-muted)]">Lucro</p>
                  <p className={`font-semibold ${lucro < 0 ? "text-[var(--danger)]" : "text-[var(--success)]"}`}>{formatarMoeda(lucro)}</p>
                </div>
              </div>
            </button>
          );
        })}
      </div>

      {consultores.length > 8 && (
        <div className="flex justify-center">
          <Button size="sm" variant="secondary" onClick={() => setTodos((t) => !t)}>
            {todos ? "Mostrar só os 8 primeiros" : `Mostrar todos (${consultores.length})`}
          </Button>
        </div>
      )}
    </section>
  );
}

// ---------- Balanço do mês ----------

/** Entradas x saídas do período (um mês ou o ano): o que já entrou/saiu e o que ainda vai entrar/sair, mais o saldo realizado e o projetado. */
function BalancoMes({ dados, mes, hoje }: { dados: FinanceiroAno; mes: number; hoje: string }) {
  const vendas = dados.vendas.filter((v) => !mes || mesDe(v.dataVenda) === mes).map((v) => calcVenda(v, hoje));
  const custos = dados.custos.filter((c) => c.status !== "Cancelado" && (!mes || mesDe(c.data) === mes));

  const recebido = vendas.reduce((n, x) => n + x.recebido, 0);
  const aReceber = vendas.reduce((n, x) => n + x.pendente, 0);
  const pago = custos.filter((c) => c.status === "Pago").reduce((n, c) => n + c.valor, 0);
  const aPagar = custos.filter((c) => c.status === "APagar").reduce((n, c) => n + c.valor, 0);
  const atrasado = custos.filter((c) => c.status === "APagar" && !!c.vencimento && hoje > c.vencimento).reduce((n, c) => n + c.valor, 0);

  const entradas = recebido + aReceber;
  const saidas = pago + aPagar;
  const saldoReal = recebido - pago;
  const saldoProj = entradas - saidas;
  const maior = Math.max(entradas, saidas, 1);
  const cobertura = saidas > 0 ? entradas / saidas : null;
  const periodo = mes ? `${MESES[mes - 1]}/${dados.ano}` : `${dados.ano}`;

  const Barra = ({ titulo, icone: Icone, partes, total }: { titulo: string; icone: LucideIcon; partes: { rotulo: string; valor: number; cor: string }[]; total: number }) => (
    <div>
      <div className="mb-1.5 flex items-center justify-between gap-2 text-sm">
        <span className="flex items-center gap-2 font-semibold text-[var(--fg)]"><Icone className="size-4 text-[var(--fg-muted)]" aria-hidden /> {titulo}</span>
        <strong className="text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarMoeda(total)}</strong>
      </div>
      <div className="h-3.5 overflow-hidden rounded-full bg-[var(--surface-hover)]">
        <div className="flex h-full gap-0.5" style={{ width: `${(total / maior) * 100}%` }}>
          {partes.filter((x) => x.valor > 0).map((x) => (
            <div key={x.rotulo} title={`${x.rotulo}: ${formatarMoeda(x.valor)}`} className="h-full min-w-1 first:rounded-l-full last:rounded-r-full transition-[flex-grow] duration-500" style={{ flexGrow: x.valor, backgroundColor: x.cor }} />
          ))}
        </div>
      </div>
      <div className="mt-1.5 flex flex-wrap gap-x-4 gap-y-1 text-xs text-[var(--fg-muted)] [font-variant-numeric:tabular-nums]">
        {partes.map((x) => (
          <span key={x.rotulo} className="flex items-center gap-1.5"><span className="size-2 rounded-full" style={{ backgroundColor: x.cor }} aria-hidden />{x.rotulo} <strong className="text-[var(--fg)]">{formatarMoeda(x.valor)}</strong></span>
        ))}
      </div>
    </div>
  );

  const Saldo = ({ rotulo, valor, dica }: { rotulo: string; valor: number; dica: string }) => (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-hover)] px-4 py-3">
      <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{rotulo}</p>
      <p className="text-xl font-bold leading-tight tracking-tight [font-variant-numeric:tabular-nums]" style={{ color: valor < 0 ? "var(--danger)" : "var(--success)" }}>{formatarMoeda(valor)}</p>
      <p className="mt-0.5 text-[11px] text-[var(--fg-muted)]">{dica}</p>
    </div>
  );

  return (
    <Card className="p-5">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
        <p className="flex items-center gap-2 text-base font-semibold text-[var(--fg)]"><Scale className="size-5 text-[var(--brand)]" aria-hidden /> Balanço de {periodo}</p>
        {atrasado > 0 && <Badge variant="danger">{formatarMoeda(atrasado)} de custos em atraso</Badge>}
      </div>

      <div className="grid gap-6 lg:grid-cols-[1fr_auto] lg:items-start">
        <div className="space-y-5">
          <Barra titulo="Entradas" icone={ArrowDownToLine} total={entradas} partes={[{ rotulo: "Já recebido", valor: recebido, cor: "var(--success)" }, { rotulo: "A receber", valor: aReceber, cor: "color-mix(in srgb, var(--success) 40%, transparent)" }]} />
          <Barra titulo="Saídas" icone={ArrowUpFromLine} total={saidas} partes={[{ rotulo: "Já pago", valor: pago, cor: "var(--danger)" }, { rotulo: "A pagar", valor: aPagar, cor: "color-mix(in srgb, var(--danger) 40%, transparent)" }]} />
        </div>
        <div className="grid gap-3 sm:grid-cols-3 lg:w-[34rem]">
          <Saldo rotulo="Saldo realizado" valor={saldoReal} dica="Recebido − pago até agora" />
          <Saldo rotulo="Saldo projetado" valor={saldoProj} dica="Se tudo for recebido e pago" />
          <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-hover)] px-4 py-3">
            <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Cobertura</p>
            <p className="text-xl font-bold leading-tight tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{cobertura === null ? "—" : `${Math.round(cobertura * 100)}%`}</p>
            <p className="mt-0.5 text-[11px] text-[var(--fg-muted)]">{cobertura === null ? "Sem custos no período" : cobertura >= 1 ? "Entradas cobrem os custos" : "Entradas não cobrem os custos"}</p>
          </div>
        </div>
      </div>
    </Card>
  );
}

// ---------- Filtros ----------

const semAcento = (t: string) => t.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
const contem = (texto: string | null | undefined, termo: string) => !termo || semAcento(texto ?? "").includes(semAcento(termo.trim()));

/** Mostra o texto com o trecho buscado em destaque (sem diferenciar acento nem maiúscula). */
function Destaque({ texto, termo }: { texto: string; termo: string }) {
  const t = semAcento(termo.trim());
  if (!t) return <>{texto}</>;
  const chars = [...texto];
  const mapa: number[] = [];
  let norm = "";
  chars.forEach((ch, i) => {
    const n = semAcento(ch);
    for (let k = 0; k < n.length; k++) mapa.push(i);
    norm += n;
  });
  const achou = norm.indexOf(t);
  if (achou < 0) return <>{texto}</>;
  const ini = mapa[achou];
  const fim = mapa[achou + t.length - 1] + 1;
  return (
    <>
      {chars.slice(0, ini).join("")}
      <mark className="rounded bg-[color-mix(in_srgb,var(--warning)_35%,transparent)] px-0.5 text-inherit">{chars.slice(ini, fim).join("")}</mark>
      {chars.slice(fim).join("")}
    </>
  );
}

interface ChipFiltro {
  valor: string;
  rotulo: string;
  contagem: number;
  cor?: string;
}

interface TagFiltro {
  rotulo: string;
  onRemover: () => void;
}

/**
 * Barra de filtros: busca, ordenação, atalhos de situação com contagem (um toque), filtros avançados que abrem sob demanda
 * e as etiquetas dos filtros ligados — cada uma se remove sozinha.
 */
function BarraFiltros({
  busca, onBusca, placeholder, chips, chip, onChip, ordem, onOrdem, opcoesOrdem, tags, onLimpar, avancadosAtivos, principais, children,
}: {
  busca: string; onBusca: (t: string) => void; placeholder: string;
  chips: ChipFiltro[]; chip: string; onChip: (v: string) => void;
  ordem: string; onOrdem: (v: string) => void; opcoesOrdem: [string, string][];
  tags: TagFiltro[]; onLimpar: () => void; avancadosAtivos: number; principais?: React.ReactNode; children: React.ReactNode;
}) {
  const [aberto, setAberto] = useState(avancadosAtivos > 0);
  const ligados = tags.length + (chip ? 1 : 0);
  const campo = useRef<HTMLInputElement>(null);

  // Atalho: "/" leva direto ao campo de busca (como em tantos sistemas), a menos que já se esteja digitando em outro campo.
  useEffect(() => {
    const aoTeclar = (e: KeyboardEvent) => {
      const alvo = e.target as HTMLElement | null;
      if (e.key !== "/" || e.ctrlKey || e.metaKey || e.altKey) return;
      if (alvo && (alvo.tagName === "INPUT" || alvo.tagName === "TEXTAREA" || alvo.tagName === "SELECT" || alvo.isContentEditable)) return;
      e.preventDefault();
      campo.current?.focus();
    };
    document.addEventListener("keydown", aoTeclar);
    return () => document.removeEventListener("keydown", aoTeclar);
  }, []);

  return (
    <Card className="filtros-painel space-y-3 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <div className="filtro-campo relative min-w-52 flex-1 basis-64" data-ativo={!!busca}>
          <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
          <Input
            ref={campo}
            type="search"
            aria-label={placeholder}
            placeholder={placeholder}
            autoComplete="off"
            className="!pl-10 !pr-12 [&::-webkit-search-cancel-button]:hidden"
            value={busca}
            onChange={(e) => onBusca(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Escape") { if (busca) onBusca(""); else e.currentTarget.blur(); }
            }}
          />
          {busca ? (
            <button type="button" aria-label="Limpar busca" onClick={() => { onBusca(""); campo.current?.focus(); }} className="focus-ring absolute right-2.5 top-1/2 flex size-6 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full bg-[var(--surface-hover)] text-[var(--fg-muted)] hover:bg-[var(--brand)] hover:text-white">
              <X className="size-3.5" aria-hidden />
            </button>
          ) : (
            <kbd aria-hidden className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 rounded-md border border-[var(--border)] px-1.5 text-[11px] font-medium leading-5 text-[var(--fg-muted)]">/</kbd>
          )}
        </div>
        {principais}
        <div className="flex w-56 items-center gap-1.5">
          <ArrowUpDown className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />
          <div className="min-w-0 flex-1">
            <Select aria-label="Ordenar por" value={ordem} onChange={(e) => onOrdem(e.target.value)}>
              {opcoesOrdem.map(([v, r]) => (
                <option key={v} value={v}>{r}</option>
              ))}
            </Select>
          </div>
        </div>
        <Button size="sm" variant="secondary" aria-expanded={aberto} onClick={() => setAberto((a) => !a)}>
          <SlidersHorizontal className="size-4" aria-hidden /> Filtros
          {avancadosAtivos > 0 && <span className="rounded-full bg-[var(--brand)] px-1.5 text-[11px] font-bold leading-5 text-white">{avancadosAtivos}</span>}
        </Button>
      </div>

      <div className="flex flex-wrap gap-1.5" role="radiogroup" aria-label="Situação">
        {chips.map((c) => {
          const ativo = chip === c.valor;
          const cor = c.cor ?? "var(--brand)";
          return (
            <button
              key={c.valor || "todos"}
              type="button"
              role="radio"
              aria-checked={ativo}
              onClick={() => onChip(c.valor)}
              style={{ "--cor": cor } as React.CSSProperties}
              className={`focus-ring inline-flex cursor-pointer items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-semibold transition-colors ${
                ativo ? "border-[var(--cor)] bg-[color-mix(in_srgb,var(--cor)_14%,transparent)] text-[var(--cor)]" : "border-[var(--border)] text-[var(--fg-muted)] hover:text-[var(--fg)]"
              }`}
            >
              {c.valor && <span className="size-2 rounded-full" style={{ backgroundColor: cor }} aria-hidden />}
              {c.rotulo}
              <span className={`rounded-full px-1.5 text-[11px] leading-5 [font-variant-numeric:tabular-nums] ${ativo ? "bg-[var(--cor)] text-white" : "bg-[var(--surface-hover)]"}`}>{c.contagem}</span>
            </button>
          );
        })}
      </div>

      {aberto && <div className="grid gap-2 border-t border-[var(--border)] pt-3 sm:grid-cols-2 lg:grid-cols-4">{children}</div>}

      {ligados > 0 && (
        <div className="flex flex-wrap items-center gap-1.5 border-t border-[var(--border)] pt-3">
          <span className="text-xs text-[var(--fg-muted)]">Filtrando por:</span>
          {(chip ? [{ rotulo: chips.find((c) => c.valor === chip)?.rotulo ?? chip, onRemover: () => onChip("") }, ...tags] : tags).map((t) => (
            <span key={t.rotulo} className="inline-flex items-center gap-1 rounded-full bg-[var(--brand-soft)] py-0.5 pl-2.5 pr-1 text-xs font-medium text-[var(--brand)]">
              {t.rotulo}
              <button type="button" aria-label={`Remover filtro ${t.rotulo}`} onClick={t.onRemover} className="focus-ring flex size-5 cursor-pointer items-center justify-center rounded-full hover:bg-[var(--brand)] hover:text-white">
                <X className="size-3" aria-hidden />
              </button>
            </span>
          ))}
          <button type="button" onClick={onLimpar} className="focus-ring ml-1 cursor-pointer rounded text-xs font-semibold text-[var(--fg-muted)] underline-offset-2 hover:text-[var(--fg)] hover:underline">
            Limpar tudo
          </button>
        </div>
      )}
    </Card>
  );
}

function FiltroSelect({ rotulo, valor, onChange, opcoes }: { rotulo: string; valor: string; onChange: (v: string) => void; opcoes: [string, string][] }) {
  return (
    <div className="filtro-campo" data-ativo={!!valor}>
      <Select aria-label={rotulo} value={valor} onChange={(e) => onChange(e.target.value)}>
        <option value="">{rotulo}: todos</option>
        {opcoes.map(([v, r]) => (
          <option key={v} value={v}>{r}</option>
        ))}
      </Select>
    </div>
  );
}

// ---------- Vendas ----------

function Vendas({ filtroInicial = "", dados, mes, somenteLeitura, onAbrirConsultor, hoje, ano, recarregar, onErro }: { filtroInicial?: string; dados: FinanceiroAno; mes: number; somenteLeitura: boolean; onAbrirConsultor: (id: string) => void; hoje: string; ano: number; recarregar: () => void; onErro: (e: unknown) => void }) {
  const mostrarConsultor = dados.visao?.tipo !== undefined && dados.visao.tipo !== "propria";
  const opcoesConsultor = opcoesDeConsultor(dados.vendas);
  const [edicao, setEdicao] = useState<Venda | null>(null);
  const [excluir, setExcluir] = useState<Venda | null>(null);
  const [salvando, setSalvando] = useState(false);

  const [busca, setBusca] = useState("");
  const [chip, setChip] = useState(filtroInicial);
  const [fProduto, setFProduto] = useState("");
  const [fConsultor, setFConsultor] = useState("");
  const [ordem, setOrdem] = useState("recentes");
  const limpar = () => { setBusca(""); setChip(""); setFProduto(""); setFConsultor(""); };

  const situacaoDe = (v: Venda) => calcVenda(v, hoje);
  const confere = (v: Venda, f: string) => {
    if (!f) return true;
    const c = situacaoDe(v);
    return f === "pendente" ? c.pendente > 0 : f === "atrasada" ? c.diasAtraso > 0 : c.pendente === 0;
  };
  // "Com pendência" e "Em atraso" valem para o ano todo (uma venda fechada em outro mês, com a adesão agendada para depois, continua pendente); os demais atalhos seguem o mês.
  const visiveis = vendasVisiveis(dados.vendas, {
    mes,
    filtro: chip,
    noMes: (v) => mesDe(v.dataVenda) === mes,
    passaOutros: (v) => (contem(v.cliente, busca) || contem(v.observacoes, busca) || contem(v.consultor, busca)) && (!fProduto || v.produto === fProduto) && (!fConsultor || v.consultorId === fConsultor),
    confere,
  });
  const chips: ChipFiltro[] = [
    { valor: "", rotulo: "Todas", contagem: visiveis.contagem("") },
    { valor: "pendente", rotulo: "Com pendência", contagem: visiveis.contagem("pendente"), cor: "var(--warning)" },
    { valor: "atrasada", rotulo: "Em atraso", contagem: visiveis.contagem("atrasada"), cor: "var(--danger)" },
    { valor: "recebida", rotulo: "Tudo recebido", contagem: visiveis.contagem("recebida"), cor: "var(--success)" },
  ];
  const ordenacoes: Record<string, (a: Venda, b: Venda) => number> = {
    recentes: (a, b) => b.dataVenda.localeCompare(a.dataVenda),
    antigas: (a, b) => a.dataVenda.localeCompare(b.dataVenda),
    adesao: (a, b) => b.valorAdesao - a.valorAdesao,
    pendente: (a, b) => situacaoDe(b).pendente - situacaoDe(a).pendente,
    cliente: (a, b) => a.cliente.localeCompare(b.cliente, "pt-BR"),
  };
  const lista = [...visiveis.lista].sort(ordenacoes[ordem]);
  const tags: TagFiltro[] = [
    ...(busca.trim() ? [{ rotulo: `“${busca.trim()}”`, onRemover: () => setBusca("") }] : []),
    ...(fProduto ? [{ rotulo: fProduto, onRemover: () => setFProduto("") }] : []),
    ...(fConsultor ? [{ rotulo: opcoesConsultor.find(([id]) => id === fConsultor)?.[1] ?? "Consultor", onRemover: () => setFConsultor("") }] : []),
  ];
  const ativos = tags.length + (chip ? 1 : 0);
  const calc = lista.map((v) => ({ v, c: calcVenda(v, hoje) }));
  const totalRecebido = calc.reduce((n, x) => n + x.c.recebido, 0);
  const totalPendente = calc.reduce((n, x) => n + x.c.pendente, 0);

  async function salvar(v: Venda) {
    if (!v.cliente.trim()) return onErro(new Error("Informe o cliente."));
    if (!v.dataVenda.startsWith(String(ano))) return onErro(new Error(`A data da venda deve estar em ${ano}.`));
    setSalvando(true);
    try {
      if (v.id) await api.put(`/portal/financeiro/vendas/${v.id}`, v);
      else await api.post("/portal/financeiro/vendas", v);
      setEdicao(null);
      recarregar();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  async function alternar(v: Venda, campo: "statusAdesao" | "statusBonus") {
    try {
      const novo = { ...v, [campo]: v[campo] === "Recebido" ? "Pendente" : "Recebido" };
      // Venda do card ainda sem complemento salvo: o primeiro clique cria o complemento.
      if (v.id) await api.put(`/portal/financeiro/vendas/${v.id}`, novo);
      else await api.post("/portal/financeiro/vendas", novo);
      recarregar();
    } catch (e) { onErro(e); }
  }

  async function remover() {
    if (!excluir?.id) return;
    try { await api.del(`/portal/financeiro/vendas/${excluir.id}`); setExcluir(null); recarregar(); } catch (e) { onErro(e); }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-[var(--fg-muted)]">
          {lista.length} venda(s) {ignoraFiltroDeMes(chip) ? "do ano" : "no período"}
          {visiveis.fezCortePorMes && " — inclui vendas fechadas em outros meses que ainda estão pendentes"}
        </p>
      </div>

      <BarraFiltros
        busca={busca} onBusca={setBusca} placeholder="Buscar cliente ou observação…"
        chips={chips} chip={chip} onChip={setChip}
        ordem={ordem} onOrdem={setOrdem}
        opcoesOrdem={[["recentes", "Mais recentes"], ["antigas", "Mais antigas"], ["adesao", "Maior adesão"], ["pendente", "Maior valor pendente"], ["cliente", "Cliente (A–Z)"]]}
        tags={tags} onLimpar={limpar} avancadosAtivos={fProduto ? 1 : 0}
        principais={dados.visao?.tipo === "equipe" ? <div className="filtro-campo w-56" data-ativo={!!fConsultor}><FiltroSelect rotulo="Consultor" valor={fConsultor} onChange={setFConsultor} opcoes={opcoesConsultor} /></div> : undefined}
      >
        <FiltroSelect rotulo="Produto" valor={fProduto} onChange={setFProduto} opcoes={PRODUTOS.map((x) => [x, x])} />
      </BarraFiltros>

      {lista.length > 0 && (
        <BarraResumo
          titulo="Recebido e pendente no período"
          metricas={[
            { rotulo: "Recebido", valor: formatarMoeda(totalRecebido), cor: "var(--success)" },
            { rotulo: "Pendente", valor: formatarMoeda(totalPendente), cor: "var(--warning)" },
          ]}
          segmentos={[
            { rotulo: "Recebido", valor: totalRecebido, cor: "var(--success)" },
            { rotulo: "Pendente", valor: totalPendente, cor: "var(--warning)" },
          ]}
        />
      )}

      {lista.length === 0 ? (
        ativos > 0 ? (
          <EmptyState title="Nenhuma venda com esses filtros" description="Mude ou limpe os filtros para ver as outras vendas." action={<Button size="sm" variant="secondary" onClick={limpar}>Limpar filtros</Button>} />
        ) : (
          <EmptyState title="Nenhuma venda lançada" description="As vendas vêm do quadro de leads: quando você concluir uma venda por lá, ela aparece aqui para você complementar bônus e recebimentos." />
        )
      ) : (
        <Card className="max-h-[34rem] overflow-auto">
          <table className="w-full min-w-[56rem] text-sm [font-variant-numeric:tabular-nums]">
            <thead>
              <tr className="text-left">
                {["Data", "Cliente", ...(mostrarConsultor ? ["Consultor"] : []), "Produto", "Adesão", "Status adesão", "Bônus/Recorr.", "Status bônus", "Premiações", "Recebido", "Pendente", "Atraso", ""].map((h) => (
                  <th key={h} className="sticky top-0 z-10 whitespace-nowrap border-b border-[var(--border)] bg-[var(--surface-solid)] px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {calc.map(({ v, c }) => (
                <tr key={v.id ?? v.oportunidadeId} className="border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--brand-soft)]">
                  <td className="whitespace-nowrap px-3 py-2">{formatarData(v.dataVenda)}</td>
                  <td className="max-w-48 truncate px-3 py-2 font-medium text-[var(--fg)]" title={v.cliente}>
                    <Destaque texto={v.cliente} termo={busca} />
                    {v.doCard && <span className="ml-2 rounded-full bg-[var(--brand-soft)] px-1.5 py-0.5 align-middle text-[10px] font-semibold text-[var(--brand)]" title="Vem do quadro de leads">card</span>}
                  </td>
                  {mostrarConsultor && <td className="whitespace-nowrap px-3 py-2">{v.consultorId && dados.visao?.tipo === "equipe" ? <BotaoConsultor nome={v.consultor ?? "—"} onClick={() => onAbrirConsultor(v.consultorId!)} /> : (v.consultor ?? "—")}</td>}
                  <td className="px-3 py-2">{v.produto}</td>
                  <td className="px-3 py-2">
                    {formatarMoeda(v.valorAdesao)}
                    {v.adesaoOriginal != null && v.adesaoOriginal > 0 && (
                      <span className="block text-[11px] text-[var(--fg-muted)]" title="Sua comissão (CLT): a porcentagem do contrato sobre a adesão da venda, calculada sozinha">
                        {Math.round((v.valorAdesao / v.adesaoOriginal) * 1000) / 10}% de {formatarMoeda(v.adesaoOriginal)}
                      </span>
                    )}
                  </td>
                  <td className="px-3 py-2">
                    <button type="button" className="cursor-pointer disabled:cursor-default" title={somenteLeitura ? undefined : "Clique para marcar como recebido / pendente"} disabled={somenteLeitura} onClick={() => alternar(v, "statusAdesao")}>
                      <BadgeSituacao s={c.sa} />
                    </button>
                    {(() => {
                      const data = rotuloDaDataDeAdesao(c.sa, v.dataPrevistaAdesao);
                      return data && <span className={`block whitespace-nowrap text-[11px] ${data.atrasada ? "font-medium text-[var(--danger)]" : "text-[var(--fg-muted)]"}`}>{data.texto}</span>;
                    })()}
                  </td>
                  <td className="px-3 py-2">
                    {formatarMoeda(v.valorBonus)}
                    {v.bonusAutomatico && v.statusBonus !== "Cancelado" && v.bonusRecebido == null && (v.bonusPrevisto ?? 0) > 0 && <span className="block text-[11px] text-[var(--fg-muted)]">previsão</span>}
                    {v.bonusAutomatico && v.bonusRecebido != null && v.bonusRecebido !== v.bonusPrevisto && <span className="block text-[11px] text-[var(--fg-muted)]">previsto {formatarMoeda(v.bonusPrevisto ?? 0)}</span>}
                  </td>
                  <td className="px-3 py-2">
                    {v.bonusAutomatico && v.statusBonus !== "Cancelado" && v.bonusRecebido == null ? (
                      bonusAguardandoInforme(v, hoje) ? (
                        <button type="button" disabled={somenteLeitura} onClick={() => setEdicao(v)} className="cursor-pointer disabled:cursor-default" title="O mês do pagamento chegou: informe quanto você realmente recebeu">
                          <Badge variant="warning">Informar recebido</Badge>
                        </button>
                      ) : (v.bonusPrevisto ?? 0) > 0 ? (
                        <Badge variant="neutral">Previsto{v.dataPrevistaBonus ? ` · ${MESES[mesDe(v.dataPrevistaBonus) - 1]}/${v.dataPrevistaBonus.slice(2, 4)}` : ""}</Badge>
                      ) : <span className="text-[var(--fg-muted)]">—</span>
                    ) : (
                      <button type="button" className="cursor-pointer disabled:cursor-default" title={somenteLeitura || v.bonusAutomatico ? undefined : "Clique para marcar como recebido / pendente"} disabled={somenteLeitura || v.bonusAutomatico} onClick={() => alternar(v, "statusBonus")}>
                        <BadgeSituacao s={c.sb} />
                      </button>
                    )}
                  </td>
                  <td className="px-3 py-2">{formatarMoeda(v.premiacoes)}</td>
                  <td className="px-3 py-2 text-[var(--success)]">{formatarMoeda(c.recebido)}</td>
                  <td className="px-3 py-2">{c.pendente ? formatarMoeda(c.pendente) : "—"}</td>
                  <td className="px-3 py-2">{c.diasAtraso ? <span className="text-[var(--danger)]">{c.diasAtraso} d</span> : "—"}</td>
                  <td className="whitespace-nowrap px-3 py-2 text-right">
                    {!somenteLeitura && <button type="button" aria-label="Editar" className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--brand)]" onClick={() => setEdicao(v)}><Pencil className="size-4" /></button>}
                    {!somenteLeitura && !v.doCard && <button type="button" aria-label="Excluir" className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--danger)]" onClick={() => setExcluir(v)}><Trash2 className="size-4" /></button>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}

      {edicao && <VendaModal inicial={edicao} salvando={salvando} onSalvar={salvar} onFechar={() => setEdicao(null)} />}
      <ConfirmDialog open={!!excluir} danger title="Excluir venda" confirmLabel="Excluir" message={`Excluir a venda de "${excluir?.cliente}"? Isso não pode ser desfeito.`} onConfirm={remover} onCancel={() => setExcluir(null)} />
    </div>
  );
}

function VendaModal({ inicial, salvando, onSalvar, onFechar }: { inicial: Venda; salvando: boolean; onSalvar: (v: Venda) => void; onFechar: () => void }) {
  const [v, setV] = useState(inicial);
  const hojeForm = hojeBrasilia();
  const set = <K extends keyof Venda>(k: K, valor: Venda[K]) => setV((a) => ({ ...a, [k]: valor }));
  const statusOpcoes = (["Pendente", "Recebido", "Cancelado"] as const).map((s) => <option key={s} value={s}>{ROTULO_RECEBIMENTO[s]}</option>);

  return (
    <Modal
      open
      onClose={onFechar}
      title="Complementar venda"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onFechar}>Cancelar</Button>
          <Button loading={salvando} onClick={() => onSalvar(v)}>Salvar</Button>
        </>
      }
    >
      {v.doCard && <p className="mb-3 rounded-lg bg-[var(--brand-soft)] px-3 py-2 text-xs text-[var(--fg)]">Esta venda veio do quadro de leads: cliente, data e adesão se atualizam por lá. Aqui você complementa bônus, premiações e recebimentos.</p>}
      <div className="filtros-painel grid gap-3 sm:grid-cols-2">
        <div><Label htmlFor="v-data" required>Data da venda</Label><DataInput id="v-data" disabled={v.doCard} value={v.dataVenda} onChange={(d) => set("dataVenda", d)} /></div>
        <div><Label htmlFor="v-prod">Produto / serviço</Label>
          <Select id="v-prod" disabled={v.doCard} value={v.produto} onChange={(e) => set("produto", e.target.value)}>{PRODUTOS.map((p) => <option key={p} value={p}>{p}</option>)}</Select>
        </div>
        <div className="sm:col-span-2"><Label htmlFor="v-cli" required>Cliente</Label><Input id="v-cli" maxLength={200} disabled={v.doCard} value={v.cliente} onChange={(e) => set("cliente", e.target.value)} /></div>
        <div><Label htmlFor="v-ad">Valor da adesão</Label><MoneyInput id="v-ad" disabled={v.doCard} value={v.valorAdesao} onChange={(n) => set("valorAdesao", n ?? 0)} /></div>
        <div>
          <Label htmlFor="v-bo">{v.bonusAutomatico ? "Bônus previsto" : "Bônus / recorrência"}</Label>
          <MoneyInput id="v-bo" disabled={v.bonusAutomatico} value={v.bonusAutomatico ? v.bonusPrevisto ?? 0 : v.valorBonus} onChange={(n) => set("valorBonus", n ?? 0)} />
          {v.bonusAutomatico && <p className="mt-1 text-xs text-[var(--fg-muted)]">Previsão pelo contrato (aba Contrato). O bônus é sobre o 1º boleto pago do associado e costuma ser pago 2 meses depois da venda.</p>}
        </div>
        <div><Label htmlFor="v-pa">Previsão de recebimento da adesão</Label><DataInput id="v-pa" value={v.dataPrevistaAdesao ?? ""} onChange={(d) => set("dataPrevistaAdesao", d || null)} /></div>
        <div><Label htmlFor="v-sa">Status da adesão</Label><Select id="v-sa" value={v.statusAdesao} onChange={(e) => set("statusAdesao", e.target.value as StatusRecebimento)}>{statusOpcoes}</Select></div>
        <div><Label htmlFor="v-pb">Previsão de recebimento do bônus</Label><DataInput id="v-pb" value={v.dataPrevistaBonus ?? ""} onChange={(d) => set("dataPrevistaBonus", d || null)} /></div>
        {v.bonusAutomatico ? (
          <div className={bonusAguardandoInforme(v, hojeForm) ? "rounded-xl border border-[var(--warning)] bg-[var(--warning-soft,transparent)] p-2" : ""}>
            <Label htmlFor="v-br">Bônus realmente recebido</Label>
            <MoneyInput id="v-br" value={v.bonusRecebido ?? null} onChange={(n) => set("bonusRecebido", n)} />
            <p className="mt-1 text-xs text-[var(--fg-muted)]">
              {bonusAguardandoInforme(v, hojeForm)
                ? "O mês do pagamento chegou: informe quanto você recebeu. Se o associado não pagou o 1º boleto, informe 0."
                : "Preencha quando o mês do pagamento chegar. Deixe vazio enquanto não receber."}
            </p>
          </div>
        ) : (
          <div><Label htmlFor="v-sb">Status do bônus</Label><Select id="v-sb" value={v.statusBonus} onChange={(e) => set("statusBonus", e.target.value as StatusRecebimento)}>{statusOpcoes}</Select></div>
        )}
        <div><Label htmlFor="v-pr">Premiações</Label><MoneyInput id="v-pr" value={v.premiacoes} onChange={(n) => set("premiacoes", n ?? 0)} /></div>
        <div className="sm:col-span-2"><Label htmlFor="v-obs">Observações</Label><Textarea id="v-obs" rows={2} maxLength={1000} value={v.observacoes ?? ""} onChange={(e) => set("observacoes", e.target.value || null)} /></div>
      </div>
    </Modal>
  );
}

// ---------- Custos ----------

function custoVazio(hoje: string): Custo {
  return { data: hoje, categoria: CATEGORIAS_CUSTO[0], descricao: null, valor: 0, vencimento: null, status: "APagar", dataPagamento: null, comprovanteSalvo: false };
}

function Custos({ dados, mes, somenteLeitura, onAbrirConsultor, hoje, ano, recarregar, onErro }: { dados: FinanceiroAno; mes: number; somenteLeitura: boolean; onAbrirConsultor: (id: string) => void; hoje: string; ano: number; recarregar: () => void; onErro: (e: unknown) => void }) {
  const mostrarConsultor = dados.visao?.tipo !== undefined && dados.visao.tipo !== "propria";
  const opcoesConsultor = opcoesDeConsultor([...dados.custos, ...dados.vendas]);
  const [edicao, setEdicao] = useState<Custo | null>(null);
  const [excluir, setExcluir] = useState<Custo | null>(null);
  const [salvando, setSalvando] = useState(false);

  const [busca, setBusca] = useState("");
  const [chip, setChip] = useState("");
  const [fCategoria, setFCategoria] = useState("");
  const [fConsultor, setFConsultor] = useState("");
  const [fOrigem, setFOrigem] = useState("");
  const [fComprovante, setFComprovante] = useState("");
  const [ordem, setOrdem] = useState("recentes");
  const limpar = () => { setBusca(""); setChip(""); setFCategoria(""); setFOrigem(""); setFComprovante(""); setFConsultor(""); };

  const atrasado = (c: Custo) => c.status === "APagar" && !!c.vencimento && hoje > c.vencimento;
  // Tudo menos o atalho de situação: serve de base para as contagens dos atalhos.
  const base = dados.custos.filter((c) =>
    (!mes || mesDe(c.data) === mes) &&
    (contem(c.descricao, busca) || contem(c.categoria, busca) || contem(c.consultor, busca)) &&
    (!fCategoria || c.categoria === fCategoria) &&
    (!fConsultor || c.consultorId === fConsultor) &&
    (fOrigem !== "card" || !!c.doCard) && (fOrigem !== "meu" || !c.doCard) &&
    (fComprovante !== "sem" || (c.status === "Pago" && !c.comprovanteSalvo)));
  const confere = (c: Custo, f: string) => (!f ? true : f === "Atrasado" ? atrasado(c) : f === "APagar" ? c.status === "APagar" && !atrasado(c) : c.status === f);
  const chips: ChipFiltro[] = [
    { valor: "", rotulo: "Todos", contagem: base.length },
    { valor: "APagar", rotulo: "A pagar", contagem: base.filter((c) => confere(c, "APagar")).length, cor: "var(--warning)" },
    { valor: "Atrasado", rotulo: "Atrasados", contagem: base.filter((c) => confere(c, "Atrasado")).length, cor: "var(--danger)" },
    { valor: "Pago", rotulo: "Pagos", contagem: base.filter((c) => confere(c, "Pago")).length, cor: "var(--success)" },
    { valor: "Cancelado", rotulo: "Cancelados", contagem: base.filter((c) => confere(c, "Cancelado")).length, cor: "var(--fg-muted)" },
  ];
  const ordenacoes: Record<string, (a: Custo, b: Custo) => number> = {
    recentes: (a, b) => b.data.localeCompare(a.data),
    antigas: (a, b) => a.data.localeCompare(b.data),
    maior: (a, b) => b.valor - a.valor,
    menor: (a, b) => a.valor - b.valor,
    vencimento: (a, b) => (a.vencimento ?? "9999").localeCompare(b.vencimento ?? "9999"),
    categoria: (a, b) => a.categoria.localeCompare(b.categoria, "pt-BR"),
  };
  const lista = base.filter((c) => confere(c, chip)).sort(ordenacoes[ordem]);
  const tags: TagFiltro[] = [
    ...(busca.trim() ? [{ rotulo: `“${busca.trim()}”`, onRemover: () => setBusca("") }] : []),
    ...(fConsultor ? [{ rotulo: opcoesConsultor.find(([id]) => id === fConsultor)?.[1] ?? "Consultor", onRemover: () => setFConsultor("") }] : []),
    ...(fCategoria ? [{ rotulo: fCategoria, onRemover: () => setFCategoria("") }] : []),
    ...(fOrigem ? [{ rotulo: fOrigem === "card" ? "Do quadro de leads" : "Lançados por mim", onRemover: () => setFOrigem("") }] : []),
    ...(fComprovante ? [{ rotulo: "Pagos sem comprovante", onRemover: () => setFComprovante("") }] : []),
  ];
  const ativos = tags.length + (chip ? 1 : 0);
  const validos = lista.filter((c) => c.status !== "Cancelado");
  const total = validos.reduce((n, c) => n + c.valor, 0);
  const aPagar = validos.filter((c) => c.status === "APagar").reduce((n, c) => n + c.valor, 0);

  const porCategoria = CATEGORIAS_CUSTO.map((cat) => ({ cat, valor: validos.filter((c) => c.categoria === cat).reduce((n, c) => n + c.valor, 0) })).filter((x) => x.valor > 0);

  async function salvar(c: Custo, repetir: number) {
    if (!c.valor) return onErro(new Error("Informe o valor."));
    if (!c.data.startsWith(String(ano))) return onErro(new Error(`A data do custo deve estar em ${ano}.`));
    setSalvando(true);
    try {
      if (c.id) await api.put(`/portal/financeiro/custos/${c.id}`, c);
      else {
        // Custo recorrente: um lançamento por mês, parando em dezembro. Só o primeiro herda "pago"; os próximos nascem "a pagar".
        const meses = Math.min(repetir, 12 - Number(c.data.slice(5, 7)) + 1);
        for (let i = 0; i < meses; i++) {
          await api.post("/portal/financeiro/custos", {
            ...c,
            data: somarMeses(c.data, i),
            vencimento: c.vencimento ? somarMeses(c.vencimento, i) : null,
            ...(i > 0 ? { status: "APagar", dataPagamento: null, comprovanteSalvo: false } : {}),
          });
        }
        if (meses < repetir) onErro(new Error(`Lançados ${meses} meses: o controle vai até dezembro de ${ano}.`));
      }
      setEdicao(null);
      recarregar();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  async function alternar(c: Custo) {
    const pago = c.status === "Pago";
    try {
      await api.put(`/portal/financeiro/custos/${c.id}`, { ...c, status: pago ? "APagar" : "Pago", dataPagamento: pago ? null : hoje });
      recarregar();
    } catch (e) { onErro(e); }
  }

  async function remover() {
    if (!excluir?.id) return;
    try { await api.del(`/portal/financeiro/custos/${excluir.id}`); setExcluir(null); recarregar(); } catch (e) { onErro(e); }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-[var(--fg-muted)]">{lista.length} custo(s) no período</p>
        {!somenteLeitura && (
          <Button size="sm" onClick={() => setEdicao(custoVazio(hoje.startsWith(String(ano)) ? hoje : `${ano}-01-01`))}>
            <Plus className="size-4" aria-hidden /> Novo custo
          </Button>
        )}
      </div>

      {dados.visao?.tipo === "equipe" && <CustosPorConsultor dados={dados} mes={mes} hoje={hoje} onAbrir={onAbrirConsultor} />}

      <BalancoMes dados={dados} mes={mes} hoje={hoje} />

      <BarraFiltros
        busca={busca} onBusca={setBusca} placeholder="Buscar por descrição ou categoria…"
        chips={chips} chip={chip} onChip={setChip}
        ordem={ordem} onOrdem={setOrdem}
        opcoesOrdem={[["recentes", "Mais recentes"], ["antigas", "Mais antigas"], ["maior", "Maior valor"], ["menor", "Menor valor"], ["vencimento", "Vencimento mais próximo"], ["categoria", "Categoria (A–Z)"]]}
        tags={tags} onLimpar={limpar} avancadosAtivos={[fCategoria, fOrigem, fComprovante].filter(Boolean).length}
        principais={dados.visao?.tipo === "equipe" ? <div className="filtro-campo w-56" data-ativo={!!fConsultor}><FiltroSelect rotulo="Consultor" valor={fConsultor} onChange={setFConsultor} opcoes={opcoesConsultor} /></div> : undefined}
      >
        <FiltroSelect rotulo="Categoria" valor={fCategoria} onChange={setFCategoria} opcoes={CATEGORIAS_CUSTO.map((x) => [x, x])} />
        <FiltroSelect rotulo="Origem" valor={fOrigem} onChange={setFOrigem} opcoes={[["card", "Do quadro de leads"], ["meu", "Lançados por mim"]]} />
        <FiltroSelect rotulo="Comprovante" valor={fComprovante} onChange={setFComprovante} opcoes={[["sem", "Pagos sem comprovante"]]} />
      </BarraFiltros>

      {porCategoria.length > 0 && (
        <BarraResumo
          titulo="Custos por categoria"
          metricas={[
            { rotulo: "Total", valor: formatarMoeda(total) },
            { rotulo: "Já pago", valor: formatarMoeda(total - aPagar), cor: "var(--success)" },
            { rotulo: "A pagar", valor: formatarMoeda(aPagar), cor: "var(--warning)" },
          ]}
          segmentos={porCategoria.map((x) => ({ rotulo: x.cat, valor: x.valor, cor: corCategoria(x.cat), icone: ICONE_CATEGORIA[x.cat] }))}
        />
      )}

      {lista.length === 0 ? (
        ativos > 0 ? (
          <EmptyState title="Nenhum custo com esses filtros" description="Mude ou limpe os filtros para ver os outros custos." action={<Button size="sm" variant="secondary" onClick={limpar}>Limpar filtros</Button>} />
        ) : (
          <EmptyState title="Nenhum custo lançado" description="Registre rastreador, leads, impostos e retiradas para saber o seu lucro real." />
        )
      ) : (
        <Card className="max-h-[34rem] overflow-auto">
          <table className="w-full min-w-[48rem] text-sm [font-variant-numeric:tabular-nums]">
            <thead>
              <tr className="text-left">
                {["Data", ...(mostrarConsultor ? ["Consultor"] : []), "Categoria", "Descrição", "Valor", "Vencimento", "Status", "Pago em", "Comprovante", ""].map((h) => (
                  <th key={h} className="sticky top-0 z-10 whitespace-nowrap border-b border-[var(--border)] bg-[var(--surface-solid)] px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {lista.map((c) => {
                const atrasado = c.status === "APagar" && !!c.vencimento && hoje > c.vencimento;
                return (
                  <tr key={c.id ?? `${c.categoria}-${c.data}-${c.descricao}-${c.valor}`} className="border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--brand-soft)]">
                    <td className="whitespace-nowrap px-3 py-2">{formatarData(c.data)}</td>
                    {mostrarConsultor && <td className="whitespace-nowrap px-3 py-2">{c.consultorId && dados.visao?.tipo === "equipe" ? <BotaoConsultor nome={c.consultor ?? "—"} onClick={() => onAbrirConsultor(c.consultorId!)} /> : (c.consultor ?? "—")}</td>}
                    <td className="px-3 py-2 font-medium text-[var(--fg)]"><span className="inline-flex items-center gap-2">{(() => { const I = ICONE_CATEGORIA[c.categoria] ?? Ellipsis; const cor = corCategoria(c.categoria); return <span className="flex size-6 shrink-0 items-center justify-center rounded-md" style={{ backgroundColor: `color-mix(in srgb, ${cor} 20%, transparent)`, color: cor }}><I className="size-3.5" aria-hidden /></span>; })()}<Destaque texto={c.categoria} termo={busca} /></span></td>
                    <td className="max-w-56 truncate px-3 py-2" title={c.descricao ?? ""}>{c.descricao ? <Destaque texto={c.descricao} termo={busca} /> : "—"}</td>
                    <td className="px-3 py-2">{formatarMoeda(c.valor)}</td>
                    <td className="whitespace-nowrap px-3 py-2">{c.vencimento ? formatarData(c.vencimento) : "—"}</td>
                    <td className="px-3 py-2">
                      <button type="button" className="cursor-pointer" title={c.doCard ? "Custo lançado no card" : "Clique para marcar como pago / a pagar"} disabled={c.doCard || c.fixo || somenteLeitura} onClick={() => alternar(c)}>
                        <Badge variant={c.status === "Pago" ? "success" : c.status === "Cancelado" ? "neutral" : atrasado ? "danger" : "warning"}>
                          {atrasado ? "Atrasado" : ROTULO_PAGAMENTO[c.status]}
                        </Badge>
                      </button>
                    </td>
                    <td className="whitespace-nowrap px-3 py-2">{c.dataPagamento ? formatarData(c.dataPagamento) : "—"}</td>
                    <td className="px-3 py-2">{c.comprovanteSalvo ? "Sim" : "Não"}</td>
                    <td className="whitespace-nowrap px-3 py-2 text-right">
                      {somenteLeitura ? null : c.doCard || c.fixo ? (
                        <span className="rounded-full bg-[var(--brand-soft)] px-1.5 py-0.5 text-[10px] font-semibold text-[var(--brand)]" title={c.fixo ? "Custo fixo do contrato" : "Vem do quadro de leads"}>{c.fixo ? "fixo" : "card"}</span>
                      ) : (
                        <>
                          <button type="button" aria-label="Editar" className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--brand)]" onClick={() => setEdicao(c)}><Pencil className="size-4" /></button>
                          <button type="button" aria-label="Excluir" className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--danger)]" onClick={() => setExcluir(c)}><Trash2 className="size-4" /></button>
                        </>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </Card>
      )}

      {edicao && <CustoModal inicial={edicao} salvando={salvando} onSalvar={salvar} onFechar={() => setEdicao(null)} />}
      <ConfirmDialog open={!!excluir} danger title="Excluir custo" confirmLabel="Excluir" message={`Excluir o custo "${excluir?.categoria}" de ${formatarMoeda(excluir?.valor)}?`} onConfirm={remover} onCancel={() => setExcluir(null)} />
    </div>
  );
}

/** Soma meses a uma data "yyyy-MM-dd", sem estourar o fim do mês (31/01 + 1 mês = 28/02). */
function somarMeses(data: string, n: number) {
  const [a, m, d] = data.split("-").map(Number);
  const alvo = new Date(Date.UTC(a, m - 1 + n, 1));
  const ultimo = new Date(Date.UTC(alvo.getUTCFullYear(), alvo.getUTCMonth() + 1, 0)).getUTCDate();
  return `${alvo.getUTCFullYear()}-${String(alvo.getUTCMonth() + 1).padStart(2, "0")}-${String(Math.min(d, ultimo)).padStart(2, "0")}`;
}

/** Cada tipo de custo tem a própria cor, a mesma na barra do resumo, na tabela e na lista do formulário. */
const COR_CATEGORIA: Record<string, string> = {
  "Rastreador": "#0ea5e9",
  "Gestão de Tráfego": "#f97316",
  "Custo de Leads": "#8b5cf6",
  "Coworking": "#14b8a6",
  "Impostos / MEI": "#ef4444",
  "Telefone / Internet": "#3b82f6",
  "Transporte / Combustível": "#f59e0b",
  "Material de Trabalho": "#6366f1",
  "Retiradas": "#ec4899",
  "Alimentação": "#84cc16",
  "Indicação": "#d946ef",
  "Assinaturas": "#64748b",
  "Outros": "#94a3b8",
};
const corCategoria = (cat: string) => COR_CATEGORIA[cat] ?? "#94a3b8";

const ICONE_CATEGORIA: Record<string, LucideIcon> = {
  "Rastreador": Satellite,
  "Gestão de Tráfego": Megaphone,
  "Custo de Leads": Target,
  "Coworking": Building2,
  "Impostos / MEI": Landmark,
  "Telefone / Internet": Wifi,
  "Transporte / Combustível": Car,
  "Material de Trabalho": Package,
  "Retiradas": HandCoins,
  "Alimentação": Utensils,
  "Indicação": Handshake,
  "Assinaturas": Repeat,
  "Outros": Ellipsis,
};

function somarDias(data: string, n: number) {
  const d = new Date(`${data}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

const fimDoMes = (data: string) => {
  const [a, m] = data.split("-").map(Number);
  return new Date(Date.UTC(a, m, 0)).toISOString().slice(0, 10);
};

/** Cartão de opção selecionável (situação, recorrência). */
function OpcaoCartao({ ativo, icone: Icone, titulo, detalhe, cor = "var(--brand)", onClick }: { ativo: boolean; icone: LucideIcon; titulo: string; detalhe?: string; cor?: string; onClick: () => void }) {
  return (
    <button
      type="button"
      aria-pressed={ativo}
      onClick={onClick}
      style={{ "--cor": cor } as React.CSSProperties}
      className={`focus-ring flex flex-1 cursor-pointer items-center gap-2.5 rounded-xl border px-3 py-2.5 text-left transition-all ${
        ativo
          ? "border-[var(--cor)] bg-[color-mix(in_srgb,var(--cor)_12%,transparent)] shadow-[0_0_0_1px_var(--cor)]"
          : "border-[var(--border)] hover:border-[color-mix(in_srgb,var(--cor)_45%,var(--border))]"
      }`}
    >
      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg" style={{ backgroundColor: `color-mix(in srgb, ${cor} 16%, transparent)`, color: cor }}>
        <Icone className="size-4" aria-hidden />
      </span>
      <span className="min-w-0">
        <span className="block text-sm font-semibold text-[var(--fg)]">{titulo}</span>
        {detalhe && <span className="block truncate text-[11px] text-[var(--fg-muted)]">{detalhe}</span>}
      </span>
    </button>
  );
}

function SecaoTitulo({ children }: { children: React.ReactNode }) {
  return <p className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{children}</p>;
}

function CustoModal({ inicial, salvando, onSalvar, onFechar }: { inicial: Custo; salvando: boolean; onSalvar: (c: Custo, repetir: number) => void; onFechar: () => void }) {
  const [c, setC] = useState(inicial);
  const [mensal, setMensal] = useState(false);
  const [repetir, setRepetir] = useState(3);
  const [tentou, setTentou] = useState(false);
  const novo = !c.id;
  const pago = c.status === "Pago";
  const hoje = hojeBrasilia();
  const set = <K extends keyof Custo>(k: K, valor: Custo[K]) => setC((a) => ({ ...a, [k]: valor }));

  // Marcar como pago já preenche a data do pagamento com hoje; voltar para "a pagar" limpa data e comprovante.
  function mudarStatus(status: StatusPagamento) {
    setC((a) => ({
      ...a,
      status,
      dataPagamento: status === "Pago" ? a.dataPagamento ?? hoje : null,
      comprovanteSalvo: status === "Pago" ? a.comprovanteSalvo : false,
    }));
  }

  const mesesDisponiveis = c.data ? 12 - Number(c.data.slice(5, 7)) + 1 : 1;
  const parcelas = novo && mensal ? Math.min(repetir, mesesDisponiveis) : 1;
  const nomeMes = (i: number) => MESES[(Number(c.data.slice(5, 7)) - 1 + i) % 12];

  function enviar() {
    setTentou(true);
    if (!c.valor || !c.data) return;
    onSalvar(c, parcelas);
  }

  const atalhosVencimento = [
    { rotulo: "Hoje", data: hoje },
    { rotulo: "Em 7 dias", data: somarDias(hoje, 7) },
    { rotulo: "Em 30 dias", data: somarDias(hoje, 30) },
    { rotulo: "Fim do mês", data: fimDoMes(c.data || hoje) },
  ];

  return (
    <Modal
      open
      onClose={onFechar}
      size="xl"
      title={novo ? "Novo custo" : "Editar custo"}
      footer={
        <div className="flex w-full flex-wrap items-center justify-between gap-3">
          <p className="text-sm text-[var(--fg-muted)]">
            {parcelas > 1 ? (
              <>
                <strong className="text-[var(--fg)]">{parcelas} lançamentos</strong> de {formatarMoeda(c.valor)} · total <strong className="text-[var(--fg)]">{formatarMoeda(c.valor * parcelas)}</strong>
              </>
            ) : (
              <>
                Total <strong className="text-[var(--fg)]">{formatarMoeda(c.valor)}</strong>
              </>
            )}
          </p>
          <div className="flex gap-2">
            <Button variant="secondary" onClick={onFechar}>Cancelar</Button>
            <Button loading={salvando} onClick={enviar}>{parcelas > 1 ? `Lançar ${parcelas} meses` : novo ? "Lançar custo" : "Salvar"}</Button>
          </div>
        </div>
      }
    >
      <form className="filtros-painel grid gap-6 md:grid-cols-[15rem_1fr]" onSubmit={(e) => { e.preventDefault(); enviar(); }}>
        {/* Categoria: lista vertical com ícone */}
        <div>
          <SecaoTitulo>Tipo de custo</SecaoTitulo>
          <div className="flex flex-col gap-1" role="radiogroup" aria-label="Tipo de custo">
            {CATEGORIAS_CUSTO.map((cat) => {
              const Icone = ICONE_CATEGORIA[cat] ?? Ellipsis;
              const ativo = c.categoria === cat;
              const cor = corCategoria(cat);
              return (
                <button
                  key={cat}
                  type="button"
                  role="radio"
                  aria-checked={ativo}
                  onClick={() => set("categoria", cat)}
                  style={ativo ? { backgroundColor: `color-mix(in srgb, ${cor} 14%, transparent)`, boxShadow: `inset 0 0 0 1px ${cor}` } : undefined}
                  className={`focus-ring flex cursor-pointer items-center gap-3 rounded-xl px-2.5 py-2 text-left text-sm transition-colors ${
                    ativo ? "font-semibold text-[var(--fg)]" : "text-[var(--fg)] hover:bg-[var(--surface-hover)]"
                  }`}
                >
                  <span
                    className="flex size-8 shrink-0 items-center justify-center rounded-lg"
                    style={ativo ? { backgroundColor: cor, color: "#fff" } : { backgroundColor: `color-mix(in srgb, ${cor} 18%, transparent)`, color: cor }}
                  >
                    <Icone className="size-4" aria-hidden />
                  </span>
                  <span className="truncate">{cat}</span>
                </button>
              );
            })}
          </div>
        </div>

        <div className="min-w-0 space-y-5">
          {/* Valor em destaque */}
          <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-hover)] p-4">
            <Label htmlFor="c-val" required>Valor do custo</Label>
            <MoneyInput id="c-val" autoFocus className="!h-14 !text-2xl !font-semibold" value={c.valor} onChange={(n) => set("valor", n ?? 0)} aria-invalid={tentou && !c.valor} />
            <FieldError>{tentou && !c.valor ? "Informe o valor do custo." : undefined}</FieldError>
            <div className="mt-3 grid gap-3 sm:grid-cols-2">
              <div>
                <Label htmlFor="c-data" required>Data do custo</Label>
                <DataInput id="c-data" value={c.data} onChange={(d) => set("data", d)} />
                <FieldError>{tentou && !c.data ? "Informe a data." : undefined}</FieldError>
              </div>
              <div>
                <Label htmlFor="c-desc">Descrição</Label>
                <Input id="c-desc" maxLength={300} placeholder="Ex.: fatura de outubro" value={c.descricao ?? ""} onChange={(e) => set("descricao", e.target.value || null)} />
              </div>
            </div>
          </div>

          {/* Situação */}
          <div>
            <SecaoTitulo>Situação do pagamento</SecaoTitulo>
            <div className="flex flex-wrap gap-2">
              <OpcaoCartao ativo={c.status === "APagar"} icone={Clock} titulo="A pagar" detalhe="Ainda vai pagar" cor="var(--warning)" onClick={() => mudarStatus("APagar")} />
              <OpcaoCartao ativo={pago} icone={CircleCheck} titulo="Pago" detalhe="Já saiu do caixa" cor="var(--success)" onClick={() => mudarStatus("Pago")} />
              {!novo && <OpcaoCartao ativo={c.status === "Cancelado"} icone={Ban} titulo="Cancelado" detalhe="Não conta" cor="var(--fg-muted)" onClick={() => mudarStatus("Cancelado")} />}
            </div>

            {c.status === "APagar" && (
              <div className="mt-3 space-y-2">
                <Label htmlFor="c-venc">Vencimento</Label>
                <DataInput id="c-venc" className="sm:max-w-56" value={c.vencimento ?? ""} onChange={(d) => set("vencimento", d || null)} />
                <div className="flex flex-wrap gap-1.5">
                  {atalhosVencimento.map((a) => (
                    <button
                      key={a.rotulo}
                      type="button"
                      onClick={() => set("vencimento", a.data)}
                      className={`focus-ring cursor-pointer rounded-full border px-2.5 py-1 text-xs font-medium transition-colors ${
                        c.vencimento === a.data ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]" : "border-[var(--border)] text-[var(--fg-muted)] hover:text-[var(--fg)]"
                      }`}
                    >
                      {a.rotulo}
                    </button>
                  ))}
                </div>
              </div>
            )}

            {pago && (
              <div className="mt-3 grid items-end gap-3 sm:grid-cols-2">
                <div>
                  <Label htmlFor="c-pg">Pago em</Label>
                  <DataInput id="c-pg" value={c.dataPagamento ?? ""} onChange={(d) => set("dataPagamento", d || null)} />
                </div>
                <button
                  type="button"
                  role="switch"
                  aria-checked={c.comprovanteSalvo}
                  onClick={() => set("comprovanteSalvo", !c.comprovanteSalvo)}
                  className="focus-ring flex h-10 cursor-pointer items-center justify-between gap-3 rounded-xl border border-[var(--border)] px-3 text-sm text-[var(--fg)]"
                >
                  <span className="flex items-center gap-2"><Receipt className="size-4 text-[var(--fg-muted)]" aria-hidden /> Comprovante salvo</span>
                  <span className={`relative h-5 w-9 rounded-full transition-colors ${c.comprovanteSalvo ? "bg-[var(--success)]" : "bg-[var(--border)]"}`}>
                    <span className={`absolute top-0.5 size-4 rounded-full bg-white shadow transition-all ${c.comprovanteSalvo ? "left-[1.125rem]" : "left-0.5"}`} />
                  </span>
                </button>
              </div>
            )}
          </div>

          {/* Recorrência */}
          {novo && (
            <div>
              <SecaoTitulo>Frequência</SecaoTitulo>
              <div className="flex flex-wrap gap-2">
                <OpcaoCartao ativo={!mensal} icone={CalendarCheck} titulo="Só uma vez" detalhe="Lançamento único" onClick={() => setMensal(false)} />
                <OpcaoCartao ativo={mensal} icone={CalendarSync} titulo="Todo mês" detalhe="Custo fixo recorrente" onClick={() => setMensal(true)} />
              </div>

              {mensal && (
                <div className="mt-3 rounded-2xl border border-[var(--border)] p-3">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-sm font-medium text-[var(--fg)]">Por quantos meses?</p>
                      <p className="text-xs text-[var(--fg-muted)]">Os próximos meses entram como “a pagar”, até dezembro.</p>
                    </div>
                    <div className="flex items-center gap-1 rounded-full border border-[var(--border)] p-1">
                      <button type="button" aria-label="Menos um mês" disabled={repetir <= 2} onClick={() => setRepetir((r) => Math.max(2, r - 1))} className="focus-ring flex size-8 cursor-pointer items-center justify-center rounded-full text-[var(--fg)] hover:bg-[var(--surface-hover)] disabled:cursor-not-allowed disabled:opacity-40">
                        <Minus className="size-4" aria-hidden />
                      </button>
                      <span className="w-8 text-center text-sm font-semibold [font-variant-numeric:tabular-nums]">{parcelas}</span>
                      <button type="button" aria-label="Mais um mês" disabled={repetir >= mesesDisponiveis} onClick={() => setRepetir((r) => Math.min(mesesDisponiveis, r + 1))} className="focus-ring flex size-8 cursor-pointer items-center justify-center rounded-full text-[var(--fg)] hover:bg-[var(--surface-hover)] disabled:cursor-not-allowed disabled:opacity-40">
                        <Plus className="size-4" aria-hidden />
                      </button>
                    </div>
                  </div>
                  <div className="mt-3 flex flex-wrap gap-1.5">
                    {Array.from({ length: parcelas }).map((_, i) => (
                      <span key={i} className="rounded-full bg-[var(--brand-soft)] px-2.5 py-1 text-[11px] font-medium text-[var(--brand)]">
                        {nomeMes(i)} · {formatarMoeda(c.valor)}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
        <button type="submit" className="hidden" aria-hidden tabIndex={-1} />
      </form>
    </Modal>
  );
}

// ---------- Metas ----------

function Metas({ dados, linhas, mes, somenteLeitura, ano, recarregar, onErro, onOk }: { dados: FinanceiroAno; linhas: LinhaMes[]; mes: number; somenteLeitura: boolean; ano: number; recarregar: () => void; onErro: (e: unknown) => void; onOk: () => void }) {
  const base = useMemo(
    () => MESES.map((_, i): Meta => dados.metas.find((m) => m.mes === i + 1) ?? { mes: i + 1, metaAdesao: 0, metaBonus: 0, metaRecorrencia: 0, metaVendas: 0, observacoes: null }),
    [dados.metas]
  );
  const [metas, setMetas] = useState(base);
  const [salvando, setSalvando] = useState(false);
  useEffect(() => setMetas(base), [base]);

  const alterados = metas.filter((m, i) => JSON.stringify(m) !== JSON.stringify(base[i]));
  const set = (mes: number, patch: Partial<Meta>) => setMetas((a) => a.map((m) => (m.mes === mes ? { ...m, ...patch } : m)));

  async function salvar() {
    setSalvando(true);
    try {
      for (const m of alterados) await api.put(`/portal/financeiro/metas/${ano}`, m);
      onOk();
      recarregar();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  // Mês escolhido no topo: a meta é mensal e do próprio consultor, então mostra só aquele mês, com o andamento.
  if (mes) {
    const m = metas[mes - 1];
    const l = linhas[mes - 1];
    const total = m.metaAdesao + m.metaBonus + m.metaRecorrencia;
    const real = l.adesao + l.bonus;
    const barra = (feito: number, meta: number) => (meta > 0 ? Math.min(100, (feito / meta) * 100) : 0);
    const alterado = JSON.stringify(m) !== JSON.stringify(base[mes - 1]);

    const Linha = ({ titulo, feito, meta, moeda = true }: { titulo: string; feito: number; meta: number; moeda?: boolean }) => (
      <div>
        <div className="flex items-baseline justify-between gap-2 text-sm">
          <span className="font-medium text-[var(--fg)]">{titulo}</span>
          <span className="text-[var(--fg-muted)] [font-variant-numeric:tabular-nums]">
            <strong className="text-[var(--fg)]">{moeda ? formatarMoeda(feito) : feito}</strong> de {meta > 0 ? (moeda ? formatarMoeda(meta) : meta) : "—"}
          </span>
        </div>
        <div className="mt-1.5 h-2.5 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
          <div className="h-full rounded-full bg-[var(--brand)] transition-[width] duration-500" style={{ width: `${barra(feito, meta)}%` }} />
        </div>
      </div>
    );

    return (
      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="space-y-4 p-5">
          <div className="flex items-center justify-between gap-3">
            <div>
              <p className="text-base font-semibold text-[var(--fg)]">Meta de {MESES[mes - 1]}/{ano}</p>
              <p className="text-xs text-[var(--fg-muted)]">Mensal e só sua.</p>
            </div>
            {!somenteLeitura && <Button size="sm" loading={salvando} disabled={!alterado} onClick={salvar}>Salvar meta</Button>}
          </div>

          <div>
            <Label htmlFor="m-ad">Meta de valor (adesão)</Label>
            <MoneyInput id="m-ad" disabled={m.doCrm || somenteLeitura} value={m.metaAdesao} onChange={(n) => set(mes, { metaAdesao: n ?? 0 })} />
          </div>
          <div>
            <Label htmlFor="m-vd">Meta de vendas</Label>
            <Input id="m-vd" type="number" min={0} disabled={m.doCrm || somenteLeitura} value={m.metaVendas} onChange={(e) => set(mes, { metaVendas: Math.max(0, Number(e.target.value) || 0) })} />
            {m.doCrm && <p className="mt-1 text-xs text-[var(--fg-muted)]">Definidas pela gestão na aba Metas do CRM.</p>}
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <div><Label htmlFor="m-bo">Meta de bônus</Label><MoneyInput id="m-bo" disabled={somenteLeitura} value={m.metaBonus} onChange={(n) => set(mes, { metaBonus: n ?? 0 })} /></div>
            <div><Label htmlFor="m-rc">Meta de recorrência</Label><MoneyInput id="m-rc" disabled={somenteLeitura} value={m.metaRecorrencia} onChange={(n) => set(mes, { metaRecorrencia: n ?? 0 })} /></div>
          </div>
          <div>
            <Label htmlFor="m-ob">Observações / estratégia</Label>
            <Textarea id="m-ob" rows={3} disabled={somenteLeitura} maxLength={1000} value={m.observacoes ?? ""} onChange={(e) => set(mes, { observacoes: e.target.value || null })} />
          </div>
        </Card>

        <Card className="space-y-5 p-5">
          <div>
            <p className="text-base font-semibold text-[var(--fg)]">Andamento do mês</p>
            <p className="text-xs text-[var(--fg-muted)]">O resultado vem das suas vendas do quadro de leads.</p>
          </div>
          <Linha titulo="Valor (adesão + bônus)" feito={real} meta={total} />
          <Linha titulo="Número de vendas" feito={l.vendas} meta={m.metaVendas} moeda={false} />
          <div className="grid grid-cols-3 gap-3 border-t border-[var(--border)] pt-4 text-center [font-variant-numeric:tabular-nums]">
            <div><p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Meta total</p><p className="text-base font-bold text-[var(--fg)]">{formatarMoeda(total)}</p></div>
            <div><p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Atingido</p><p className="text-base font-bold text-[var(--brand)]">{total ? pct(l.pctMeta) : "—"}</p></div>
            <div><p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Falta</p><p className="text-base font-bold" style={{ color: total && l.deltaMeta < 0 ? "var(--danger)" : "var(--success)" }}>{total ? formatarMoeda(Math.max(0, -l.deltaMeta)) : "—"}</p></div>
          </div>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-[var(--fg-muted)]">A meta de valor (adesão) e a de vendas vêm da aba Metas do CRM, definidas pela gestão. Você preenche a meta de bônus/recorrência; o resultado real vem das suas vendas.</p>
        {!somenteLeitura && <Button size="sm" loading={salvando} disabled={alterados.length === 0} onClick={salvar}>Salvar metas</Button>}
      </div>
      <Card className="overflow-x-auto">
        <table className="w-full min-w-[64rem] text-sm [font-variant-numeric:tabular-nums]">
          <thead>
            <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
              {["Mês", "Meta adesão", "Meta bônus", "Meta recorrência", "Meta nº vendas", "Meta total", "Real (adesão+bônus)", "Delta", "% meta", "Vendas feitas", "Observações"].map((h) => (
                <th key={h} className="px-3 py-2 font-medium">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {metas.map((m) => {
              const l = linhas[m.mes - 1];
              const total = m.metaAdesao + m.metaBonus + m.metaRecorrencia;
              return (
                <tr key={m.mes} className="border-b border-[var(--border)] last:border-0">
                  <td className="px-3 py-2 font-medium text-[var(--fg)]">{MESES[m.mes - 1]}</td>
                  <td className="px-2 py-1.5"><MoneyInput aria-label={`Meta adesão ${MESES[m.mes - 1]}`} disabled={m.doCrm || somenteLeitura} title={m.doCrm ? "Definida na aba Metas do CRM" : undefined} className="w-32" value={m.metaAdesao} onChange={(n) => set(m.mes, { metaAdesao: n ?? 0 })} /></td>
                  <td className="px-2 py-1.5"><MoneyInput aria-label={`Meta bônus ${MESES[m.mes - 1]}`} disabled={somenteLeitura} className="w-32" value={m.metaBonus} onChange={(n) => set(m.mes, { metaBonus: n ?? 0 })} /></td>
                  <td className="px-2 py-1.5"><MoneyInput aria-label={`Meta recorrência ${MESES[m.mes - 1]}`} disabled={somenteLeitura} className="w-32" value={m.metaRecorrencia} onChange={(n) => set(m.mes, { metaRecorrencia: n ?? 0 })} /></td>
                  <td className="px-2 py-1.5"><Input aria-label={`Meta de vendas ${MESES[m.mes - 1]}`} type="number" min={0} disabled={m.doCrm || somenteLeitura} title={m.doCrm ? "Definida na aba Metas do CRM" : undefined} className="w-20" value={m.metaVendas} onChange={(e) => set(m.mes, { metaVendas: Math.max(0, Number(e.target.value) || 0) })} /></td>
                  <td className="px-3 py-2">{formatarMoeda(total)}</td>
                  <td className="px-3 py-2">{formatarMoeda(l.adesao + l.bonus)}</td>
                  <td className={`px-3 py-2 ${total && l.deltaMeta < 0 ? "text-[var(--danger)]" : total ? "text-[var(--success)]" : ""}`}>{total ? formatarMoeda(l.deltaMeta) : "—"}</td>
                  <td className="px-3 py-2">{total ? pct(l.pctMeta) : "—"}</td>
                  <td className="px-3 py-2">{l.vendas}{m.metaVendas ? ` / ${m.metaVendas}` : ""}</td>
                  <td className="px-2 py-1.5"><Input aria-label={`Observações ${MESES[m.mes - 1]}`} disabled={somenteLeitura} className="w-48" maxLength={1000} value={m.observacoes ?? ""} onChange={(e) => set(m.mes, { observacoes: e.target.value || null })} /></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </Card>
    </div>
  );
}
