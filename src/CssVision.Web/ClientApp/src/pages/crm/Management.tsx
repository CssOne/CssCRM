import { usePaginacao } from "../../lib/usePaginacao";
import { HoraInput } from "../../components/PeriodoInput";
import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarDataHora, formatarMoeda, formatarPercentual } from "../../lib/format";
import type { GestaoComercialResumo, RecebeLeadsHistorico, RedistribuicaoHistorico, VendedorResumo } from "../../lib/types";
import { AlertTriangle, ChevronDown, LayoutGrid, List, Timer, UserPlus, Users } from "lucide-react";
import { Avatar, Badge, Card, ConfirmDialog, ErrorState, Select, Skeleton, useToast, Pagination } from "../../components/ui";
import { StatCard } from "../../components/crm/StatCard";
import { NumeroAnimado } from "../../lib/useContagem";
import { OPCOES_O_QUE } from "../../lib/opcoesLead";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import { useEstadoSalvo } from "../../lib/useEstadoSalvo";
import { BarraDeFiltros, CampoDeFiltro } from "../../components/Filtros";

function LimiteInput({
  vendedor,
  tipo,
  onSalvo,
}: {
  vendedor: VendedorResumo;
  tipo: "mensal" | "diario";
  onSalvo: () => void;
}) {
  const { notificar } = useToast();
  const atual = (tipo === "mensal" ? vendedor.limiteMensalLeads : vendedor.limiteDiarioLeads) ?? null;
  const [valor, setValor] = useState(atual?.toString() ?? "");
  const [salvando, setSalvando] = useState(false);

  async function salvar() {
    const limite = valor.trim() === "" ? null : Number(valor);
    if (limite !== null && (Number.isNaN(limite) || limite < 0)) {
      notificar("error", "Limite inválido.");
      setValor(atual?.toString() ?? "");
      return;
    }
    if (limite === atual) return;

    setSalvando(true);
    try {
      await api.put(`/crm/management/vendedores/${vendedor.id}/${tipo === "mensal" ? "limite" : "limite-diario"}`, { limite });
      notificar("success", tipo === "mensal" ? "Limite mensal atualizado." : "Limite diário atualizado.");
      onSalvo();
    } catch {
      notificar("error", "Não foi possível atualizar o limite.");
      setValor(atual?.toString() ?? "");
    } finally {
      setSalvando(false);
    }
  }

  const recebidos = tipo === "mensal" ? vendedor.leadsRecebidosNoMes : vendedor.leadsRecebidosHoje ?? 0;
  const limite = valor.trim() === "" ? null : Number(valor);
  const noLimite = limite !== null && recebidos >= limite;
  return (
    <div className="flex items-center justify-between gap-2 text-xs">
      <span className="text-[var(--fg-muted)]">{tipo === "mensal" ? "Limite mensal" : "Limite diário"}</span>
      <div className="flex items-center gap-1.5">
        <span className={noLimite ? "font-semibold text-[var(--danger)]" : "text-[var(--fg-muted)]"} title={tipo === "mensal" ? "Recebidos no mês" : "Recebidos hoje"}>
          {recebidos} /
        </span>
        <input
          inputMode="numeric"
          className="focus-ring h-7 w-14 rounded-md border border-[var(--border)] bg-[var(--surface)] px-1.5 text-center text-xs text-[var(--fg)] placeholder:text-[var(--fg-muted)] disabled:opacity-50"
          placeholder="∞"
          aria-label={tipo === "mensal" ? "Limite mensal de leads" : "Limite diário de leads"}
          title="Vazio = sem limite"
          value={valor}
          disabled={salvando}
          onChange={(e) => setValor(e.target.value.replace(/[^0-9]/g, ""))}
          onBlur={salvar}
          onKeyDown={(e) => {
            if (e.key === "Enter") (e.target as HTMLInputElement).blur();
          }}
        />
      </div>
    </div>
  );
}

const DIAS_SEMANA = ["D", "S", "T", "Q", "Q", "S", "S"];
const NOMES_DIAS = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];

/** Dias da semana e faixa de horário (Brasília) em que o consultor entra no rodízio de leads. */
function JanelaRecebimento({ vendedor, onSalvo }: { vendedor: VendedorResumo; onSalvo: () => void }) {
  const { notificar } = useToast();
  const inicial = {
    inicio: vendedor.horarioInicioLeads ?? "",
    fim: vendedor.horarioFimLeads ?? "",
    dias: vendedor.diasSemanaLeads ?? [0, 1, 2, 3, 4, 5, 6],
  };
  const [inicio, setInicio] = useState(inicial.inicio);
  const [fim, setFim] = useState(inicial.fim);
  const [dias, setDias] = useState<number[]>(inicial.dias);
  const [salvando, setSalvando] = useState(false);

  const alterado = inicio !== inicial.inicio || fim !== inicial.fim || dias.join() !== [...inicial.dias].sort().join();

  async function salvar() {
    if ((inicio === "") !== (fim === "")) {
      notificar("error", "Informe o horário de início e o de fim, ou deixe os dois vazios.");
      return;
    }
    if (dias.length === 0) {
      notificar("error", "Escolha ao menos um dia da semana.");
      return;
    }
    setSalvando(true);
    try {
      await api.put(`/crm/management/vendedores/${vendedor.id}/janela-recebimento`, {
        horarioInicio: inicio || null,
        horarioFim: fim || null,
        diasSemana: dias,
      });
      notificar("success", "Dias e horário de recebimento atualizados.");
      onSalvo();
    } catch {
      notificar("error", "Não foi possível atualizar o horário.");
    } finally {
      setSalvando(false);
    }
  }

  function alternarDia(d: number) {
    setDias((atual) => (atual.includes(d) ? atual.filter((x) => x !== d) : [...atual, d].sort()));
  }

  return (
    <div className="space-y-1.5 border-t border-[var(--border)] pt-2 text-xs">
      <div className="flex items-center justify-between gap-2">
        <span className="text-[var(--fg-muted)]" title="Horário de Brasília. Vazio = o dia todo">Horário</span>
        <div className="flex items-center gap-1">
          <HoraInput compacto ariaLabel="Início do horário de recebimento de leads" value={inicio} disabled={salvando} onChange={setInicio} />
          <span className="text-[var(--fg-muted)]">às</span>
          <HoraInput compacto ariaLabel="Fim do horário de recebimento de leads" value={fim} disabled={salvando} onChange={setFim} />
        </div>
      </div>
      <div className="flex items-center justify-between gap-2">
        <span className="text-[var(--fg-muted)]">Dias</span>
        <div className="flex gap-0.5" role="group" aria-label="Dias da semana em que recebe leads">
          {DIAS_SEMANA.map((rotulo, d) => {
            const ligado = dias.includes(d);
            return (
              <button
                key={d}
                type="button"
                aria-pressed={ligado}
                title={NOMES_DIAS[d]}
                disabled={salvando}
                onClick={() => alternarDia(d)}
                className={`focus-ring size-6 cursor-pointer rounded-md text-[11px] font-semibold transition-colors disabled:opacity-50 ${
                  ligado ? "bg-[var(--brand)] text-white" : "border border-[var(--border)] text-[var(--fg-muted)]"
                }`}
              >
                {rotulo}
              </button>
            );
          })}
        </div>
      </div>
      {alterado && (
        <div className="flex justify-end gap-2">
          <button type="button" className="focus-ring cursor-pointer text-[var(--fg-muted)] hover:underline" disabled={salvando}
            onClick={() => { setInicio(inicial.inicio); setFim(inicial.fim); setDias(inicial.dias); }}>
            Descartar
          </button>
          <button type="button" className="focus-ring cursor-pointer font-semibold text-[var(--brand)] hover:underline disabled:opacity-50" disabled={salvando} onClick={salvar}>
            Salvar horário
          </button>
        </div>
      )}
    </div>
  );
}

/** Tipos de lead ("O que?") que o consultor recebe — o mesmo campo de Usuários > Editar. Nenhum marcado = qualquer tipo. */
function TiposLeadSelect({ vendedor, onSalvo }: { vendedor: VendedorResumo; onSalvo: () => void }) {
  const { notificar } = useToast();
  const [tipos, setTipos] = useState<string[]>(vendedor.recebeSomenteOQue ?? []);
  const [salvando, setSalvando] = useState(false);

  async function alternar(tipo: string) {
    const novos = tipos.includes(tipo) ? tipos.filter((t) => t !== tipo) : [...tipos, tipo];
    const anteriores = tipos;
    setTipos(novos);
    setSalvando(true);
    try {
      await api.put(`/crm/management/vendedores/${vendedor.id}/tipos-lead`, { oQue: novos });
      notificar("success", novos.length === 0 ? `${vendedor.nome} recebe qualquer tipo de lead.` : `${vendedor.nome} recebe só: ${novos.join(", ")}.`);
      onSalvo();
    } catch {
      setTipos(anteriores);
      notificar("error", "Não foi possível atualizar os tipos de lead.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <div className="space-y-1 border-t border-[var(--border)] pt-2 text-xs">
      <span className="text-[var(--fg-muted)]" title="Nenhum marcado = recebe qualquer tipo de lead">Recebe somente leads de</span>
      <div className="flex flex-wrap gap-1" role="group" aria-label="Tipos de lead que o consultor recebe">
        {OPCOES_O_QUE.map((tipo) => {
          const ligado = tipos.includes(tipo);
          return (
            <button
              key={tipo}
              type="button"
              aria-pressed={ligado}
              disabled={salvando}
              onClick={() => alternar(tipo)}
              className={`focus-ring cursor-pointer rounded-full px-2 py-0.5 text-[11px] font-semibold transition-colors disabled:opacity-50 ${
                ligado ? "bg-[var(--brand)] text-white" : "border border-[var(--border)] text-[var(--fg-muted)]"
              }`}
            >
              {tipo}
            </button>
          );
        })}
      </div>
      {tipos.length === 0 && <p className="text-[var(--fg-muted)]">Todos os tipos</p>}
    </div>
  );
}

const ROTULO_EVENTO_RECEBE_LEADS: Record<string, string> = {
  desativou: "Desativou o recebimento de leads",
  ativou: "Voltou a receber leads",
  conta_inativada: "Conta inativada (fora da distribuição)",
  conta_reativada: "Conta reativada",
};

function RecebeLeadsSwitch({ vendedor, onSalvo }: { vendedor: VendedorResumo; onSalvo: () => void }) {
  const { notificar } = useToast();
  const [ligado, setLigado] = useState(vendedor.recebeLeads !== false);
  const [salvando, setSalvando] = useState(false);

  // Desativar pede confirmação (afeta a distribuição de leads daquele consultor); voltar a receber não.
  const [confirmando, setConfirmando] = useState(false);

  function alternar() {
    if (ligado) setConfirmando(true);
    else void salvar(true);
  }

  async function salvar(novo: boolean) {
    setLigado(novo);
    setSalvando(true);
    try {
      await api.put(`/crm/management/vendedores/${vendedor.id}/recebe-leads`, { recebeLeads: novo });
      notificar("success", novo ? `${vendedor.nome} voltou a receber leads.` : `${vendedor.nome} parou de receber leads.`);
      onSalvo();
    } catch {
      setLigado(!novo);
      notificar("error", "Não foi possível alterar.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <>
    <button
      type="button"
      role="switch"
      aria-checked={ligado}
      disabled={salvando}
      onClick={alternar}
      title={ligado ? "Recebendo leads da distribuição automática — clique para pausar" : "Fora da distribuição de leads — clique para voltar a receber"}
      className="focus-ring mt-2 flex cursor-pointer items-center gap-2 text-xs font-medium disabled:opacity-60"
    >
      <span
        className={`relative inline-flex h-4 w-7 shrink-0 items-center rounded-full transition-colors ${
          ligado ? "bg-[var(--success)]" : "bg-[var(--border)]"
        }`}
      >
        <span className={`inline-block size-3 rounded-full bg-white shadow transition-transform ${ligado ? "translate-x-3.5" : "translate-x-0.5"}`} />
      </span>
      <span className={ligado ? "text-[var(--success)]" : "text-[var(--fg-muted)]"}>{ligado ? "Recebe leads" : "Não recebe leads"}</span>
    </button>

      <ConfirmDialog
        open={confirmando}
        title="Parar de receber leads?"
        danger
        confirmLabel="Sim, desativar"
        loading={salvando}
        message={
          <>
            Você quer mesmo desativar o recebimento de leads de <strong>{vendedor.nome}</strong>? Ele(a) deixa de receber novos leads da distribuição
            automática até alguém ativar de novo. Os leads que já estão com {vendedor.nome} continuam com ele(a). Fica registrado que foi você quem desativou.
          </>
        }
        onConfirm={() => { setConfirmando(false); void salvar(false); }}
        onCancel={() => setConfirmando(false)}
      />
    </>
  );
}

/** 0,4 h → "24 min"; 5,5 h → "5h 30min"; 50 h → "2,1 dias". */
function formatarDuracao(horas: number) {
  if (horas < 1) return `${Math.max(1, Math.round(horas * 60))} min`;
  if (horas < 24) {
    const h = Math.floor(horas);
    const m = Math.round((horas - h) * 60);
    return m > 0 ? `${h}h ${m}min` : `${h}h`;
  }
  return `${(horas / 24).toLocaleString("pt-BR", { maximumFractionDigits: 1 })} dias`;
}

/** Dia da semana (0 = domingo) hoje em Brasília. */
function diaDaSemanaBrasilia(): number {
  const nome = new Intl.DateTimeFormat("en-US", { weekday: "short", timeZone: "America/Sao_Paulo" }).format(new Date());
  return ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"].indexOf(nome);
}

/** Quantos leads de tráfego pago cada consultor marcado para receber já pegou hoje e quantos ainda faltam (até o limite diário). */
type SituacaoDoDia = "todos" | "faltam" | "atingiram" | "semlead" | "fora";

/**
 * Leads de hoje por vendedor: quanto cada consultor que recebe leads já pegou hoje, quanto falta para o limite diário e quem está parado, com barra de
 * progresso e uma situação por consultor. Dá para filtrar por situação e ordenar. Conta os leads de tráfego pago atribuídos hoje.
 */
function LeadsDoDiaCard({ vendedores, filtrado = false }: { vendedores: VendedorResumo[]; filtrado?: boolean }) {
  const hoje = diaDaSemanaBrasilia();
  const [situacao, setSituacao] = useState<SituacaoDoDia>("todos");
  const [ordem, setOrdem] = useState<"menos" | "mais" | "nome">("menos");

  const todas = vendedores
    .filter((v) => v.ativo !== false && v.recebeLeads)
    .map((v) => {
      const pegou = v.leadsRecebidosHoje ?? 0;
      const limite = v.limiteDiarioLeads ?? null;
      const foraDoDia = !!v.diasSemanaLeads && v.diasSemanaLeads.length > 0 && !v.diasSemanaLeads.includes(hoje);
      const faltam = limite === null ? null : Math.max(limite - pegou, 0);
      const estado: "fora" | "atingiu" | "semlead" | "andamento" | "semlimite" = foraDoDia ? "fora" : limite !== null && faltam === 0 ? "atingiu" : pegou === 0 ? "semlead" : limite === null ? "semlimite" : "andamento";
      return { v, pegou, limite, faltam, foraDoDia, estado };
    });

  const contagens = {
    todos: todas.length,
    faltam: todas.filter((l) => !l.foraDoDia && l.faltam !== null && l.faltam > 0).length,
    atingiram: todas.filter((l) => l.estado === "atingiu").length,
    semlead: todas.filter((l) => !l.foraDoDia && l.pegou === 0).length,
    fora: todas.filter((l) => l.foraDoDia).length,
  };
  const linhas = todas
    .filter((l) => (situacao === "todos" ? true : situacao === "faltam" ? !l.foraDoDia && l.faltam !== null && l.faltam > 0 : situacao === "atingiram" ? l.estado === "atingiu" : situacao === "semlead" ? !l.foraDoDia && l.pegou === 0 : l.foraDoDia))
    .sort((a, b) =>
      ordem === "nome"
        ? a.v.nome.localeCompare(b.v.nome, "pt-BR")
        : ordem === "mais"
          ? b.pegou - a.pegou || a.v.nome.localeCompare(b.v.nome, "pt-BR")
          : a.foraDoDia === b.foraDoDia ? a.pegou - b.pegou || a.v.nome.localeCompare(b.v.nome, "pt-BR") : a.foraDoDia ? 1 : -1
    );

  const totalPegou = todas.reduce((n, l) => n + l.pegou, 0);
  const comLimite = todas.filter((l) => !l.foraDoDia && l.limite !== null);
  const limiteTotal = comLimite.reduce((n, l) => n + (l.limite ?? 0), 0);
  const pegouComLimite = comLimite.reduce((n, l) => n + Math.min(l.pegou, l.limite ?? 0), 0);
  const totalFaltam = comLimite.reduce((n, l) => n + (l.faltam ?? 0), 0);
  const semLimite = todas.filter((l) => l.limite === null && !l.foraDoDia).length;
  const pagina = usePaginacao(linhas, 16);

  const estiloDoEstado = {
    fora: { rotulo: "Não recebe hoje", cor: "var(--fg-muted)" },
    atingiu: { rotulo: "Limite atingido", cor: "var(--success)" },
    semlead: { rotulo: "Sem lead hoje", cor: "var(--warning)" },
    semlimite: { rotulo: "Sem limite diário", cor: "var(--brand)" },
    andamento: { rotulo: "", cor: "var(--brand)" },
  } as const;

  const abas: [SituacaoDoDia, string][] = [["todos", "Todos"], ["faltam", "Faltam"], ["atingiram", "Atingiram o limite"], ["semlead", "Sem lead hoje"], ["fora", "Fora hoje"]];

  return (
    <Card className="h-full p-4">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-[15px] font-semibold tracking-tight text-[var(--fg)]">Leads de hoje por vendedor</h2>
          <p className="text-xs text-[var(--fg-muted)]">Só quem está marcado para receber leads. Conta os leads de tráfego pago atribuídos hoje. Segue os filtros acima.</p>
        </div>
        <div className="flex flex-wrap gap-4 text-center">
          <div>
            <p className="text-xl font-semibold text-[var(--brand)]"><NumeroAnimado valor={String(totalPegou)} /></p>
            <p className="text-[11px] text-[var(--fg-muted)]">pegos hoje</p>
          </div>
          <div title="Soma do que falta até o limite diário de quem tem limite">
            <p className="text-xl font-semibold text-[var(--fg)]"><NumeroAnimado valor={String(totalFaltam)} /></p>
            <p className="text-[11px] text-[var(--fg-muted)]">faltam (com limite)</p>
          </div>
          <div title="Ainda não receberam nenhum lead hoje">
            <p className="text-xl font-semibold text-[var(--fg)]"><NumeroAnimado valor={String(contagens.semlead)} /></p>
            <p className="text-[11px] text-[var(--fg-muted)]">sem lead hoje</p>
          </div>
          {semLimite > 0 && (
            <div title="Sem limite diário: recebem enquanto houver lead, na ordem do rodízio">
              <p className="text-xl font-semibold text-[var(--fg)]"><NumeroAnimado valor={String(semLimite)} /></p>
              <p className="text-[11px] text-[var(--fg-muted)]">sem limite diário</p>
            </div>
          )}
        </div>
      </div>

      {limiteTotal > 0 && (
        <div className="mb-3" title="Do total dos limites diários de quem tem limite, quanto já foi distribuído hoje">
          <div className="mb-1 flex items-center justify-between text-[11px] text-[var(--fg-muted)]">
            <span>Distribuição do dia (quem tem limite)</span>
            <span className="font-semibold text-[var(--fg)] [font-variant-numeric:tabular-nums]">{pegouComLimite} de {limiteTotal} · {Math.round((pegouComLimite / limiteTotal) * 100)}%</span>
          </div>
          <div className="h-2 overflow-hidden rounded-full bg-[var(--surface-hover)]">
            <div className="barra-cresce h-full rounded-full bg-gradient-to-r from-[var(--brand)] to-[color-mix(in_srgb,var(--brand)_55%,#A6C3E0)]" style={{ width: `${Math.min(100, (pegouComLimite / limiteTotal) * 100)}%` }} />
          </div>
        </div>
      )}

      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div role="tablist" data-sem-animacao aria-label="Situação no dia" className="flex flex-wrap gap-1">
          {abas.map(([chave, rotulo]) => (
            <button
              key={chave}
              type="button"
              role="tab"
              aria-selected={situacao === chave}
              onClick={() => setSituacao(chave)}
              className={`focus-ring flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium transition-colors ${
                situacao === chave ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-hover)] text-[var(--fg-muted)] hover:text-[var(--fg)]"
              }`}
            >
              {rotulo}
              <span className={`rounded-full px-1.5 text-[10px] ${situacao === chave ? "bg-white/25" : "bg-[var(--surface)]"}`}>{contagens[chave]}</span>
            </button>
          ))}
        </div>
        <div className="w-44">
          <Select aria-label="Ordenar" value={ordem} onChange={(e) => setOrdem(e.target.value as typeof ordem)}>
            <option value="menos">Menos leads primeiro</option>
            <option value="mais">Mais leads primeiro</option>
            <option value="nome">Nome (A–Z)</option>
          </Select>
        </div>
      </div>

      {todas.length === 0 ? (
        <p className="rounded-xl bg-[var(--surface-hover)] px-4 py-6 text-center text-sm text-[var(--fg-muted)]">{filtrado ? "Ninguém que está marcado para receber leads combina com os filtros." : "Ninguém está marcado para receber leads."}</p>
      ) : linhas.length === 0 ? (
        <p className="rounded-xl bg-[var(--surface-hover)] px-4 py-6 text-center text-sm text-[var(--fg-muted)]">Ninguém nesta situação agora.</p>
      ) : (
        <>
          {/* A chave muda só com o filtro/ordem (o bloco reaparece); a troca de página é animada pela própria paginação — sem animar duas vezes. */}
          <div key={`${situacao}-${ordem}`} className="entra grid gap-1.5 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4">
            {pagina.itensDaPagina.map(({ v, pegou, limite, faltam, estado }, i) => {
              const est = estiloDoEstado[estado];
              const texto = estado === "andamento" ? `faltam ${faltam}` : est.rotulo;
              return (
                <div
                  key={v.id}
                  style={{ "--i": i, borderLeftColor: est.cor } as React.CSSProperties}
                  title={`${v.nome} · ${v.regionalNome ?? "Sem regional"}`}
                  className={`cartao-sobe rounded-lg border border-l-[3px] border-[var(--border)] bg-[var(--surface)] px-2.5 py-1.5 ${estado === "fora" ? "opacity-60" : ""}`}
                >
                  <div className="flex items-center gap-2">
                    <Avatar nome={v.nome} usuarioId={v.id} className="size-6 text-[9px]" />
                    <p className="min-w-0 flex-1 truncate text-xs font-semibold text-[var(--fg)]">{v.nome}</p>
                    <span className="shrink-0 text-right [font-variant-numeric:tabular-nums]">
                      <strong className="text-sm leading-none text-[var(--fg)]">{pegou}</strong>
                      {limite !== null && <span className="text-[11px] text-[var(--fg-muted)]"> / {limite}</span>}
                    </span>
                  </div>
                  <div className="mt-1 flex items-center gap-2">
                    {limite !== null && estado !== "fora" ? (
                      <div className="h-1 flex-1 overflow-hidden rounded-full bg-[var(--surface-hover)]">
                        <div className="barra-cresce h-full rounded-full" style={{ "--i": i, width: `${Math.min(100, limite === 0 ? 0 : (pegou / limite) * 100)}%`, backgroundColor: est.cor } as React.CSSProperties} />
                      </div>
                    ) : null}
                    <p className="shrink-0 text-[10px] font-medium leading-tight" style={{ color: est.cor }}>{texto}</p>
                  </div>
                </div>
              );
            })}
          </div>
          <Pagination pagina={pagina.pagina} totalPaginas={pagina.totalPaginas} onChange={pagina.setPagina} />
        </>
      )}
    </Card>
  );
}

type OrdemCarteira = "nome" | "mes" | "anterior" | "oportunidades" | "hoje";

/** Resumo, em uma linha, de como o consultor recebe leads: tipos, horário e dias. */
function resumoDaDistribuicao(v: VendedorResumo) {
  const tipos = v.recebeSomenteOQue?.length ? v.recebeSomenteOQue.join(", ") : "todos os tipos";
  const horario = v.horarioInicioLeads && v.horarioFimLeads ? `${v.horarioInicioLeads}–${v.horarioFimLeads}` : "o dia todo";
  const dias = !v.diasSemanaLeads || v.diasSemanaLeads.length === 0 || v.diasSemanaLeads.length === 7 ? "todos os dias" : v.diasSemanaLeads.map((d) => NOMES_DIAS[d].slice(0, 3)).join(", ");
  return { tipos, horario, dias };
}

function CartaoDoVendedor({ v, onSalvo }: { v: VendedorResumo; onSalvo: () => void }) {
  return (
          <div className="space-y-3.5 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 text-sm">
            <div className="flex items-center gap-3">
              <Avatar nome={v.nome} usuarioId={v.id} className="size-9 text-xs" />
              <div className="min-w-0 flex-1">
                <p className="flex items-center gap-2 font-semibold text-[var(--fg)]" title={v.nome}>
                  <span className="truncate">{v.nome}</span>
                  {v.ativo === false && <Badge variant="danger">Inativo</Badge>}
                </p>
                <p className="truncate text-xs text-[var(--fg-muted)]">{v.regionalNome ?? "Sem regional"}</p>
              </div>
            </div>
            <dl
              className="grid grid-cols-3 gap-2 rounded-xl bg-[var(--surface-hover)] p-2.5 text-center [font-variant-numeric:tabular-nums]"
              title="Leads de tráfego pago (Notion e sistema novo) que chegaram para o vendedor"
            >
              <div>
                <dd className="text-base font-semibold text-[var(--brand)]">{(v.leadsTrafegoNoMes ?? 0).toLocaleString("pt-BR")}</dd>
                <dt className="text-[10px] text-[var(--fg-muted)]">leads no mês</dt>
              </div>
              <div>
                <dd className="text-base font-semibold text-[var(--fg)]">{(v.leadsTrafegoMesAnterior ?? 0).toLocaleString("pt-BR")}</dd>
                <dt className="text-[10px] text-[var(--fg-muted)]">mês anterior</dt>
              </div>
              <div>
                <dd className="text-base font-semibold text-[var(--fg)]">{v.oportunidadesAbertas}</dd>
                <dt className="text-[10px] text-[var(--fg-muted)]">oportunidades</dt>
              </div>
            </dl>
            <div>
              <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Limites de leads</p>
              <div className="space-y-1.5">
                <LimiteInput tipo="mensal" vendedor={v} onSalvo={() => onSalvo()} />
                <LimiteInput tipo="diario" vendedor={v} onSalvo={() => onSalvo()} />
              </div>
            </div>
            <div className="space-y-2.5 border-t border-[var(--border)] pt-3">
              <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Distribuição</p>
              <TiposLeadSelect key={`${v.id}-${v.recebeSomenteOQue?.join("|")}`} vendedor={v} onSalvo={() => onSalvo()} />
              <JanelaRecebimento key={`${v.id}-${v.horarioInicioLeads}-${v.horarioFimLeads}-${v.diasSemanaLeads?.join("")}`} vendedor={v} onSalvo={() => onSalvo()} />
              <RecebeLeadsSwitch vendedor={v} onSalvo={() => onSalvo()} />
            </div>
          </div>
  );
}

/**
 * Carteira por vendedor: em lista (padrão, compacta, com o "recebe leads" na própria linha e a configuração completa ao abrir a linha) ou em cartões.
 * A ordem e o modo ficam guardados no navegador.
 */
function CarteiraDosVendedores({ vendedores, onSalvo }: { vendedores: VendedorResumo[]; onSalvo: () => void }) {
  const [modo, setModo] = useEstadoSalvo<"lista" | "cartoes">("gestao:carteira-modo", "lista");
  const [ordem, setOrdem] = useEstadoSalvo<OrdemCarteira>("gestao:carteira-ordem", "nome");
  const [abertos, setAbertos] = useState<Set<string>>(() => new Set());
  // Quem já foi aberto alguma vez mantém o conteúdo montado: assim a linha também anima ao fechar (a altura recolhe em vez de sumir de uma vez).
  const [jaAbertos, setJaAbertos] = useState<Set<string>>(() => new Set());

  const ordenados = useMemo(() => {
    const lista = [...vendedores];
    const valor = (v: VendedorResumo) => {
      switch (ordem) {
        case "mes": return v.leadsTrafegoNoMes ?? 0;
        case "anterior": return v.leadsTrafegoMesAnterior ?? 0;
        case "oportunidades": return v.oportunidadesAbertas;
        case "hoje": return v.leadsRecebidosHoje ?? 0;
        default: return 0;
      }
    };
    return ordem === "nome" ? lista.sort((a, b) => a.nome.localeCompare(b.nome, "pt-BR")) : lista.sort((a, b) => valor(b) - valor(a) || a.nome.localeCompare(b.nome, "pt-BR"));
  }, [vendedores, ordem]);
  const pagina = usePaginacao(ordenados, modo === "lista" ? 20 : 9);

  const alternarAberto = (id: string) =>
    setAbertos((atual) => {
      const novo = new Set(atual);
      if (novo.has(id)) novo.delete(id);
      else novo.add(id);
      return novo;
    });
  const alternar = (id: string) => {
    if (jaAbertos.has(id)) return alternarAberto(id);
    // Primeira abertura: monta o conteúdo já recolhido e só no quadro seguinte abre, para a transição de altura rodar.
    setJaAbertos((atual) => new Set(atual).add(id));
    requestAnimationFrame(() => requestAnimationFrame(() => alternarAberto(id)));
  };

  return (
    <Card className="p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-[15px] font-semibold tracking-tight text-[var(--fg)]">Carteira por vendedor</h2>
          <p className="text-xs text-[var(--fg-muted)]">
            {ordenados.length} consultor(es){modo === "lista" ? " · clique na linha para configurar limites, tipos de lead e horário" : ""}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="w-44">
            <Select aria-label="Ordenar a carteira" value={ordem} onChange={(e) => setOrdem(e.target.value as OrdemCarteira)}>
              <option value="nome">Ordem: nome (A–Z)</option>
              <option value="mes">Ordem: leads no mês</option>
              <option value="anterior">Ordem: mês anterior</option>
              <option value="oportunidades">Ordem: oportunidades</option>
              <option value="hoje">Ordem: leads hoje</option>
            </Select>
          </div>
          <div role="radiogroup" aria-label="Forma de exibição" className="flex items-center gap-0.5 rounded-full bg-[var(--surface-hover)] p-0.5">
            {([["lista", "Lista", List], ["cartoes", "Cartões", LayoutGrid]] as const).map(([valor, rotulo, Icone]) => (
              <button
                key={valor}
                type="button"
                role="radio"
                aria-checked={modo === valor}
                onClick={() => setModo(valor)}
                className={`focus-ring flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium transition-colors ${
                  modo === valor ? "bg-[var(--surface)] text-[var(--fg)] shadow-[0_1px_2px_rgb(0_0_0/0.12)]" : "text-[var(--fg-muted)] hover:text-[var(--fg)]"
                }`}
              >
                <Icone className="size-3.5" aria-hidden /> {rotulo}
              </button>
            ))}
          </div>
        </div>
      </div>

      {ordenados.length === 0 ? (
        <p className="rounded-xl bg-[var(--surface-hover)] px-4 py-6 text-center text-sm text-[var(--fg-muted)]">Nenhum consultor combina com os filtros.</p>
      ) : modo === "cartoes" ? (
        <div key={`cartoes-${ordem}`} className="entra grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {pagina.itensDaPagina.map((v) => (
            <CartaoDoVendedor key={v.id} v={v} onSalvo={onSalvo} />
          ))}
        </div>
      ) : (
        <div key={`lista-${ordem}`} className="entra overflow-x-auto">
          <table className="w-full min-w-[60rem] text-sm [font-variant-numeric:tabular-nums]">
            <thead>
              <tr className="text-left">
                {["Consultor", "Leads no mês", "Mês anterior", "Oportunidades", "Hoje", "Limite mensal", "Distribuição", "Recebe leads", ""].map((h, i) => (
                  <th key={`${h}${i}`} className={`border-b border-[var(--border)] px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)] ${i >= 1 && i <= 5 ? "text-right" : ""}`}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {pagina.itensDaPagina.map((v) => {
                const aberto = abertos.has(v.id);
                const d = resumoDaDistribuicao(v);
                return (
                  <Fragment key={v.id}>
                    <tr
                      onClick={() => alternar(v.id)}
                      aria-expanded={aberto}
                      className={`group cursor-pointer border-b border-[var(--border)] transition-colors duration-300 hover:bg-[var(--brand-soft)] active:bg-[var(--brand-soft)] ${aberto ? "bg-[var(--brand-soft)]" : ""} ${v.ativo === false ? "opacity-70" : ""}`}
                    >
                      <td className={`px-3 py-2.5 transition-[box-shadow,padding] duration-300 ${aberto ? "pl-4 shadow-[inset_3px_0_0_var(--brand)]" : "shadow-[inset_0_0_0_var(--brand)] group-hover:pl-4"}`}>
                        <span className="flex items-center gap-2.5">
                          <Avatar nome={v.nome} usuarioId={v.id} className="size-8 text-xs" />
                          <span className="min-w-0">
                            <span className="flex items-center gap-2 font-semibold text-[var(--fg)]">
                              <span className="truncate" title={v.nome}>{v.nome}</span>
                              {v.ativo === false && <Badge variant="danger">Inativo</Badge>}
                            </span>
                            <span className="block truncate text-xs text-[var(--fg-muted)]">{v.regionalNome ?? "Sem regional"}</span>
                          </span>
                        </span>
                      </td>
                      <td className="px-3 py-2.5 text-right font-semibold text-[var(--brand)]">{(v.leadsTrafegoNoMes ?? 0).toLocaleString("pt-BR")}</td>
                      <td className="px-3 py-2.5 text-right text-[var(--fg)]">{(v.leadsTrafegoMesAnterior ?? 0).toLocaleString("pt-BR")}</td>
                      <td className="px-3 py-2.5 text-right text-[var(--fg)]">{v.oportunidadesAbertas}</td>
                      <td className="px-3 py-2.5 text-right text-[var(--fg)]">
                        {v.leadsRecebidosHoje ?? 0}
                        <span className="text-[var(--fg-muted)]">{v.limiteDiarioLeads != null ? ` / ${v.limiteDiarioLeads}` : ""}</span>
                      </td>
                      <td className="px-3 py-2.5 text-right text-[var(--fg)]">
                        {v.leadsRecebidosNoMes.toLocaleString("pt-BR")}
                        <span className="text-[var(--fg-muted)]">{v.limiteMensalLeads != null ? ` / ${v.limiteMensalLeads}` : ""}</span>
                      </td>
                      <td className="max-w-[14rem] px-3 py-2.5 text-xs text-[var(--fg-muted)]">
                        <span className="block truncate" title={d.tipos}>{d.tipos}</span>
                        <span className="block truncate">{d.horario} · {d.dias}</span>
                      </td>
                      <td className="px-3 py-2.5" onClick={(e) => e.stopPropagation()}>
                        <RecebeLeadsSwitch vendedor={v} onSalvo={onSalvo} />
                      </td>
                      <td className="px-3 py-2.5 text-right">
                        <ChevronDown className={`inline size-4 transition-all duration-300 ease-[cubic-bezier(0.34,1.56,0.64,1)] ${aberto ? "rotate-180 text-[var(--brand)]" : "text-[var(--fg-muted)]"}`} aria-hidden />
                      </td>
                    </tr>
                    {jaAbertos.has(v.id) && (
                      <tr className={`bg-[var(--surface-hover)] ${aberto ? "border-b border-[var(--border)]" : ""}`} aria-hidden={!aberto}>
                        <td colSpan={9} className="p-0">
                          {/* grid-template-rows 0fr → 1fr: a altura do conteúdo cresce e recolhe suavemente, sem saber o tamanho dele. */}
                          <div className={`grid transition-[grid-template-rows,opacity] duration-500 ease-[cubic-bezier(0.22,1,0.36,1)] ${aberto ? "grid-rows-[1fr] opacity-100" : "grid-rows-[0fr] opacity-0"}`}>
                            <div className="min-h-0 overflow-hidden">
                              <div className="grid gap-4 px-4 py-3 md:grid-cols-3">
                            <div>
                              <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Limites de leads</p>
                              <div className="space-y-1.5">
                                <LimiteInput tipo="mensal" vendedor={v} onSalvo={onSalvo} />
                                <LimiteInput tipo="diario" vendedor={v} onSalvo={onSalvo} />
                              </div>
                            </div>
                            <div>
                              <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Tipos de lead</p>
                              <TiposLeadSelect key={`${v.id}-${v.recebeSomenteOQue?.join("|")}`} vendedor={v} onSalvo={onSalvo} />
                            </div>
                            <div>
                              <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">Horário e dias</p>
                              <JanelaRecebimento key={`${v.id}-${v.horarioInicioLeads}-${v.horarioFimLeads}-${v.diasSemanaLeads?.join("")}`} vendedor={v} onSalvo={onSalvo} />
                            </div>
                              </div>
                            </div>
                          </div>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      <Pagination pagina={pagina.pagina} totalPaginas={pagina.totalPaginas} onChange={pagina.setPagina} />
    </Card>
  );
}

export function ManagementPage() {
  const [resumo, setResumo] = useState<GestaoComercialResumo | null>(null);
  const [vendedores, setVendedores] = useState<VendedorResumo[] | null>(null);
  const [historico, setHistorico] = useState<RedistribuicaoHistorico[] | null>(null);
  const [historicoRecebeLeads, setHistoricoRecebeLeads] = useState<RecebeLeadsHistorico[] | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const carregar = useCallback((signal?: AbortSignal) => {
    setCarregando(true);
    setErro(null);
    Promise.all([
      api.get<GestaoComercialResumo>("/crm/management/summary", signal),
      api.get<VendedorResumo[]>("/crm/management/vendedores", signal),
      api.get<RedistribuicaoHistorico[]>("/crm/management/redistribuicoes", signal),
      // Histórico de quem desativou o recebimento de leads: se falhar, o resto da tela continua funcionando.
      api.get<RecebeLeadsHistorico[]>("/crm/management/recebe-leads-historico", signal).catch((e) => { if (isAbortError(e)) throw e; return [] as RecebeLeadsHistorico[]; }),
    ])
      .then(([r, v, h, rl]) => {
        setResumo(r);
        setVendedores(v);
        setHistorico(h);
        setHistoricoRecebeLeads(rl);
      })
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar a gestão comercial."); })
      .finally(() => { if (!signal?.aborted) setCarregando(false); });
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  // Tempo real: ranking, carteira e métricas se atualizam sozinhos (vendas, leads) e quando o mês vira.
  useAtualizarAoVivo(() => setRecarregar((n) => n + 1));

  // Filtros da Gestão comercial: nome, regional, situação da conta e "recebe leads". A busca por nome vale também para o ranking e
  // para o tempo até o 1º contato; regional/situação/recebe leads filtram a carteira.
  // Os filtros ficam guardados no navegador (por usuário) e só saem em "Limpar filtros".
  const [buscaNome, setBuscaNome] = useEstadoSalvo("gestao:busca-nome", "");
  const [filtroRegional, setFiltroRegional] = useEstadoSalvo("gestao:regional", "");
  const [filtroSituacao, setFiltroSituacao] = useEstadoSalvo<"" | "ativos" | "inativos">("gestao:situacao", "");
  const [filtroRecebe, setFiltroRecebe] = useEstadoSalvo<"" | "sim" | "nao">("gestao:recebe", "");
  const termo = buscaNome.trim().toLowerCase();
  const casaNome = (nome: string) => !termo || nome.toLowerCase().includes(termo);
  const regionaisDisponiveis = useMemo(
    () => [...new Set((vendedores ?? []).map((v) => v.regionalNome).filter((r): r is string => !!r))].sort(),
    [vendedores]
  );
  const vendedoresFiltrados = useMemo(
    () =>
      (vendedores ?? []).filter(
        (v) =>
          casaNome(v.nome) &&
          (!filtroRegional || v.regionalNome === filtroRegional) &&
          (filtroSituacao === "" || (filtroSituacao === "ativos") === (v.ativo !== false)) &&
          (filtroRecebe === "" || (filtroRecebe === "sim") === !!v.recebeLeads)
      ),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [vendedores, termo, filtroRegional, filtroSituacao, filtroRecebe]
  );
  const rankingFiltrado = useMemo(
    () => (resumo?.ranking ?? []).filter((r) => casaNome(r.vendedorNome)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [resumo, termo]
  );
  const contatoFiltrado = useMemo(
    () => (resumo?.primeiroContatoPorVendedor ?? []).filter((r) => casaNome(r.vendedorNome)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [resumo, termo]
  );
  const temFiltro = !!(termo || filtroRegional || filtroSituacao || filtroRecebe);

  // Paginação das listas (hooks antes dos retornos de carregando/erro).
  const paginaContato = usePaginacao(contatoFiltrado, 8);
  const paginaRanking = usePaginacao(rankingFiltrado, 10);
  const paginaParadas = usePaginacao(resumo?.oportunidadesSemMovimentacao, 8);
  const paginaHistorico = usePaginacao(historico, 10);
  const paginaRecebeLeads = usePaginacao(historicoRecebeLeads, 10);

  if (carregando && !resumo) {
    return (
      <div className="space-y-4">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-40" />
        ))}
      </div>
    );
  }

  if (!resumo || !vendedores || !historico) {
    return <ErrorState message={erro ?? "Não foi possível carregar."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Gestão comercial</h1>
        <p className="text-sm text-[var(--fg-muted)]">
          Distribua leads na página de Leads (seleção em lote) e acompanhe aqui o desempenho da equipe.
        </p>
      </div>

      <BarraDeFiltros
        busca={buscaNome}
        onBusca={setBuscaNome}
        placeholder="Buscar consultor pelo nome"
        chips={[
          ...(filtroRegional ? [{ rotulo: "Regional", valor: filtroRegional, limpar: () => setFiltroRegional("") }] : []),
          ...(filtroSituacao ? [{ rotulo: "Situação", valor: filtroSituacao === "ativos" ? "Ativos" : "Inativos", limpar: () => setFiltroSituacao("") }] : []),
          ...(filtroRecebe ? [{ rotulo: "Recebe leads", valor: filtroRecebe === "sim" ? "Recebe" : "Não recebe", limpar: () => setFiltroRecebe("") }] : []),
        ]}
        onLimparTudo={() => { setBuscaNome(""); setFiltroRegional(""); setFiltroSituacao(""); setFiltroRecebe(""); }}
      >
        <CampoDeFiltro rotulo="Regional">
          <Select value={filtroRegional} onChange={(e) => setFiltroRegional(e.target.value)}>
            <option value="">Todas</option>
            {regionaisDisponiveis.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </Select>
        </CampoDeFiltro>
        <CampoDeFiltro rotulo="Situação">
          <Select value={filtroSituacao} onChange={(e) => setFiltroSituacao(e.target.value as typeof filtroSituacao)}>
            <option value="">Todos</option>
            <option value="ativos">Ativos</option>
            <option value="inativos">Inativos</option>
          </Select>
        </CampoDeFiltro>
        <CampoDeFiltro rotulo="Recebe leads">
          <Select value={filtroRecebe} onChange={(e) => setFiltroRecebe(e.target.value as typeof filtroRecebe)}>
            <option value="">Todos</option>
            <option value="sim">Recebe</option>
            <option value="nao">Não recebe</option>
          </Select>
        </CampoDeFiltro>
      </BarraDeFiltros>

      <div className="entra grid grid-cols-2 gap-3 lg:grid-cols-4" style={{ "--i": 1 } as React.CSSProperties}>
        <StatCard titulo="Recebendo leads" valor={String(vendedoresFiltrados.filter((v) => v.ativo !== false && v.recebeLeads).length)} icone={Users} tom="brand" subtitulo={`de ${vendedoresFiltrados.filter((v) => v.ativo !== false).length} consultores ativos`} />
        <StatCard titulo="Leads no mês (tráfego)" valor={vendedoresFiltrados.reduce((n, v) => n + (v.leadsTrafegoNoMes ?? 0), 0).toLocaleString("pt-BR")} icone={UserPlus} tom="success" subtitulo="atribuídos à equipe" />
        <StatCard titulo="1º contato (24 h)" valor={(resumo.leadsComPrimeiroContato ?? 0) > 0 ? formatarDuracao(resumo.tempoMedioPrimeiroContatoHoras) : "—"} icone={Timer} tom="warning" subtitulo="média nas últimas 24 horas" />
        <StatCard titulo="Oportunidades paradas" valor={String(resumo.oportunidadesSemMovimentacao.length)} icone={AlertTriangle} tom={resumo.oportunidadesSemMovimentacao.length > 0 ? "danger" : "neutral"} subtitulo="sem movimentação" />
      </div>

      {/* "Leads de hoje" e "Tempo até o 1º contato" lado a lado; a carteira ocupa a largura toda (lista ou cartões). */}
      <div className="grid gap-4 lg:grid-cols-3">
        <div className="entra lg:col-span-2" style={{ "--i": 2 } as React.CSSProperties}>
          <LeadsDoDiaCard vendedores={vendedoresFiltrados} filtrado={temFiltro} />
        </div>
        <Card className="entra !rounded-2xl p-4" style={{ "--i": 3 } as React.CSSProperties}>
          <div className="mb-3 flex items-center justify-between gap-2">
            <div className="flex items-center gap-2.5">
              <span className="flex size-8 items-center justify-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand)]">
                <Timer className="size-4" aria-hidden />
              </span>
              <h2 className="text-[15px] font-bold leading-tight tracking-tight text-[var(--fg)]">Tempo até o 1º contato</h2>
            </div>
            <span className="shrink-0 rounded-full bg-[var(--surface-hover)] px-2.5 py-1 text-[11px] font-semibold text-[var(--fg-muted)]">Últimas 24 h</span>
          </div>
          {(resumo.leadsComPrimeiroContato ?? 0) === 0 ? (
            <p className="rounded-xl bg-[var(--surface-hover)] px-3 py-4 text-sm text-[var(--fg-muted)]">
              Nenhum lead do CRM que chegou nas últimas 24 horas teve contato ainda. O contato conta quando o consultor move o lead de etapa
              no quadro ou conclui uma atividade.
            </p>
          ) : (
            <>
              {(() => {
                const h = resumo.tempoMedioPrimeiroContatoHoras;
                const nivel = h <= 1 ? { texto: "Rápido", cor: "var(--success)" } : h <= 4 ? { texto: "Atenção", cor: "var(--warning)" } : { texto: "Lento", cor: "var(--danger)" };
                return (
                  <div className="relative overflow-hidden rounded-2xl p-4" style={{ backgroundColor: `color-mix(in srgb, ${nivel.cor} 9%, var(--surface-hover))` }}>
                    <span aria-hidden className="pointer-events-none absolute -right-8 -top-8 size-28 rounded-full opacity-70 blur-2xl" style={{ backgroundColor: `color-mix(in srgb, ${nivel.cor} 26%, transparent)` }} />
                    <div className="relative flex items-end justify-between gap-3">
                      <p className="text-4xl font-bold leading-none tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarDuracao(h)}</p>
                      <span className="rounded-full px-2.5 py-1 text-[11px] font-bold" style={{ backgroundColor: `color-mix(in srgb, ${nivel.cor} 16%, transparent)`, color: nivel.cor }}>
                        {nivel.texto}
                      </span>
                    </div>
                    <p className="relative mt-2 text-xs text-[var(--fg-muted)]">
                      média de {resumo.leadsComPrimeiroContato} lead(s) das últimas 24 horas, da chegada até o consultor mover de etapa ou concluir uma atividade
                    </p>
                  </div>
                );
              })()}
              {contatoFiltrado.length > 0 && (
                <ul className="mt-3 space-y-2">
                  {(() => {
                    const maior = Math.max(...contatoFiltrado.map((x) => x.horas), 0.01);
                    return paginaContato.itensDaPagina.map((v) => (
                      <li key={v.vendedorId} className="text-xs">
                        <div className="flex items-center justify-between gap-2">
                          <span className="flex min-w-0 items-center gap-2">
                            <Avatar nome={v.vendedorNome} usuarioId={v.vendedorId} className="size-5 text-[9px]" />
                            <span className="truncate font-medium text-[var(--fg)]" title={v.vendedorNome}>
                              {v.vendedorNome}
                            </span>
                          </span>
                          <span className="shrink-0 text-[var(--fg-muted)]">
                            <strong className="text-[var(--fg)]">{formatarDuracao(v.horas)}</strong> · {v.leads} lead(s)
                          </span>
                        </div>
                        <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-[var(--surface-hover)]">
                          <div className="h-full rounded-full bg-gradient-to-r from-[var(--brand)] to-[color-mix(in_srgb,var(--brand)_50%,#A6C3E0)]" style={{ width: `${Math.max(6, (v.horas / maior) * 100)}%` }} />
                        </div>
                      </li>
                    ));
                  })()}
                </ul>
              )}
              <Pagination pagina={paginaContato.pagina} totalPaginas={paginaContato.totalPaginas} onChange={paginaContato.setPagina} />
            </>
          )}
        </Card>
      </div>

      <div className="entra" style={{ "--i": 4 } as React.CSSProperties}>
        <CarteiraDosVendedores vendedores={vendedoresFiltrados} onSalvo={() => setRecarregar((n) => n + 1)} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="overflow-x-auto p-4">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Ranking comercial</h2>
          {rankingFiltrado.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Sem vendas no período.</p>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                  <th className="pb-2 font-medium">#</th>
                  <th className="pb-2 font-medium">Vendedor</th>
                  <th className="pb-2 font-medium">Ganhas</th>
                  <th className="pb-2 font-medium" title="Soma do pagamento de adesão das vendas do período">Valor (adesão)</th>
                  <th className="pb-2 font-medium">Conversão</th>
                </tr>
              </thead>
              <tbody>
                {paginaRanking.itensDaPagina.map((r) => (
                  <tr key={r.vendedorId} className="border-b border-[var(--border)] last:border-0">
                    <td className="py-2"><span className={`flex size-6 items-center justify-center rounded-full text-xs font-semibold ${r.posicao <= 3 ? "bg-[var(--brand)] text-[var(--brand-fg)]" : "bg-[var(--surface-hover)] text-[var(--fg-muted)]"}`}>{r.posicao}</span></td>
                    <td className="py-2 text-[var(--fg)]">{r.vendedorNome}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{r.vendasGanhas}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{formatarMoeda(r.valorAdesao ?? 0)}</td>
                    <td className="py-2 text-[var(--fg-muted)]">{formatarPercentual(r.taxaConversao)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <Pagination pagina={paginaRanking.pagina} totalPaginas={paginaRanking.totalPaginas} onChange={paginaRanking.setPagina} />
        </Card>

        <Card className="p-4">
          <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Motivos de perda</h2>
          {resumo.motivosPerda.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Sem perdas registradas no período.</p>
          ) : (
            <ul className="space-y-2">
              {resumo.motivosPerda.map((m) => (
                <li key={m.motivo} className="flex items-center justify-between text-sm">
                  <span className="text-[var(--fg)]">{m.motivo}</span>
                  <span className="text-[var(--fg-muted)]">
                    {m.quantidade}x · {formatarMoeda(m.valorPerdido)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <Card className="p-4">
        <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Oportunidades sem movimentação</h2>
        {resumo.oportunidadesSemMovimentacao.length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhuma oportunidade estagnada. 🎉</p>
        ) : (
          <ul className="space-y-2">
            {paginaParadas.itensDaPagina.map((o) => (
              <li key={o.opportunityId} className="flex items-center justify-between rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-sm">
                <div>
                  <p className="font-medium text-[var(--fg)]">{o.leadNome}</p>
                  <p className="text-xs text-[var(--fg-muted)]">
                    {o.etapaNome} · {o.responsavelNome}
                  </p>
                </div>
                <Badge variant="warning">{o.diasSemMovimentacao} dia(s) parada</Badge>
              </li>
            ))}
          </ul>
        )}
        <Pagination pagina={paginaParadas.pagina} totalPaginas={paginaParadas.totalPaginas} onChange={paginaParadas.setPagina} />
      </Card>

      <Card className="overflow-x-auto p-4">
        <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Histórico de redistribuições</h2>
        {historico.length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhuma redistribuição registrada.</p>
        ) : (
          <table className="w-full min-w-[560px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 font-medium">Lead</th>
                <th className="pb-2 font-medium">De</th>
                <th className="pb-2 font-medium">Para</th>
                <th className="pb-2 font-medium">Por</th>
                <th className="pb-2 font-medium">Quando</th>
              </tr>
            </thead>
            <tbody>
              {paginaHistorico.itensDaPagina.map((h, i) => (
                <tr key={i} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2">
                    <Link to={`/app/crm/leads/${h.leadId}`} className="text-[var(--fg)] hover:text-[var(--brand)]">
                      {h.leadNome}
                    </Link>
                  </td>
                  <td className="py-2 text-[var(--fg-muted)]">{h.responsavelAnteriorNome ?? "—"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{h.responsavelNovoNome}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{h.alteradoPorNome ?? "—"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarDataHora(h.alteradoEm)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <Pagination pagina={paginaHistorico.pagina} totalPaginas={paginaHistorico.totalPaginas} onChange={paginaHistorico.setPagina} />
      </Card>

      <Card className="overflow-x-auto p-4">
        <h2 className="text-sm font-semibold text-[var(--fg)]">Histórico de recebimento de leads</h2>
        <p className="mb-3 text-xs text-[var(--fg-muted)]">
          Quem desativou (ou reativou) cada consultor da distribuição de leads. Inclui as contas inativadas e reativadas que o sistema já registrava; as trocas do
          interruptor "Recebe leads" passam a ser registradas a partir de agora.
        </p>
        {(historicoRecebeLeads ?? []).length === 0 ? (
          <p className="text-sm text-[var(--fg-muted)]">Nenhuma alteração registrada.</p>
        ) : (
          <table className="w-full min-w-[560px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 font-medium">Consultor</th>
                <th className="pb-2 font-medium">O que aconteceu</th>
                <th className="pb-2 font-medium">Quem fez</th>
                <th className="pb-2 font-medium">Quando</th>
              </tr>
            </thead>
            <tbody>
              {paginaRecebeLeads.itensDaPagina.map((h, i) => (
                <tr key={`${h.consultorId}-${h.ocorridoEm}-${i}`} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2 text-[var(--fg)]">{h.consultorNome}</td>
                  <td className="py-2">
                    <Badge variant={h.evento === "desativou" || h.evento === "conta_inativada" ? "warning" : "success"}>
                      {ROTULO_EVENTO_RECEBE_LEADS[h.evento] ?? h.evento}
                    </Badge>
                  </td>
                  <td className="py-2 text-[var(--fg-muted)]">{h.alteradoPorNome ?? "—"}</td>
                  <td className="py-2 text-[var(--fg-muted)]">{formatarDataHora(h.ocorridoEm)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <Pagination pagina={paginaRecebeLeads.pagina} totalPaginas={paginaRecebeLeads.totalPaginas} onChange={paginaRecebeLeads.setPagina} />
      </Card>
    </div>
  );
}
