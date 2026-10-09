import { usePaginacao } from "../../lib/usePaginacao";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarDataHora, formatarMoeda, formatarPercentual } from "../../lib/format";
import type { GestaoComercialResumo, RedistribuicaoHistorico, VendedorResumo } from "../../lib/types";
import { Badge, Card, ErrorState, Input, Select, Skeleton, useToast, Pagination } from "../../components/ui";
import { OPCOES_O_QUE } from "../../lib/opcoesLead";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";

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

  const campoHora =
    "focus-ring h-7 w-[4.5rem] rounded-md border border-[var(--border)] bg-[var(--surface)] px-1 text-center text-xs text-[var(--fg)] disabled:opacity-50";
  return (
    <div className="space-y-1.5 border-t border-[var(--border)] pt-2 text-xs">
      <div className="flex items-center justify-between gap-2">
        <span className="text-[var(--fg-muted)]" title="Horário de Brasília. Vazio = o dia todo">Horário</span>
        <div className="flex items-center gap-1">
          <input type="time" className={campoHora} aria-label="Início do horário de recebimento de leads" value={inicio} disabled={salvando} onChange={(e) => setInicio(e.target.value)} />
          <span className="text-[var(--fg-muted)]">às</span>
          <input type="time" className={campoHora} aria-label="Fim do horário de recebimento de leads" value={fim} disabled={salvando} onChange={(e) => setFim(e.target.value)} />
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

function RecebeLeadsSwitch({ vendedor, onSalvo }: { vendedor: VendedorResumo; onSalvo: () => void }) {
  const { notificar } = useToast();
  const [ligado, setLigado] = useState(vendedor.recebeLeads !== false);
  const [salvando, setSalvando] = useState(false);

  async function alternar() {
    const novo = !ligado;
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
function LeadsDoDiaCard({ vendedores }: { vendedores: VendedorResumo[] }) {
  const hoje = diaDaSemanaBrasilia();
  const linhas = vendedores
    .filter((v) => v.ativo !== false && v.recebeLeads)
    .map((v) => {
      const pegou = v.leadsRecebidosHoje ?? 0;
      const limite = v.limiteDiarioLeads ?? null;
      return {
        v,
        pegou,
        limite,
        faltam: limite === null ? null : Math.max(limite - pegou, 0),
        foraDoDia: !!v.diasSemanaLeads && v.diasSemanaLeads.length > 0 && !v.diasSemanaLeads.includes(hoje),
      };
    })
    .sort((a, b) => (a.foraDoDia === b.foraDoDia ? a.pegou - b.pegou || a.v.nome.localeCompare(b.v.nome) : a.foraDoDia ? 1 : -1));
  const totalPegou = linhas.reduce((n, l) => n + l.pegou, 0);
  const totalFaltam = linhas.reduce((n, l) => n + (l.foraDoDia ? 0 : (l.faltam ?? 0)), 0);
  const semLimite = linhas.filter((l) => l.limite === null && !l.foraDoDia).length;
  const semLeadHoje = linhas.filter((l) => l.pegou === 0 && !l.foraDoDia).length;
  const pagina = usePaginacao(linhas, 12);

  return (
    <Card className="p-4">
      <div className="mb-3 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold text-[var(--fg)]">Leads de hoje por vendedor</h2>
          <p className="text-xs text-[var(--fg-muted)]">Só quem está marcado para receber leads. Conta os leads de tráfego pago atribuídos hoje.</p>
        </div>
        <div className="flex flex-wrap gap-4 text-center">
          <div>
            <p className="text-xl font-semibold text-[var(--brand)]">{totalPegou}</p>
            <p className="text-[11px] text-[var(--fg-muted)]">pegos hoje</p>
          </div>
          <div title="Soma do que falta até o limite diário de quem tem limite">
            <p className="text-xl font-semibold text-[var(--fg)]">{totalFaltam}</p>
            <p className="text-[11px] text-[var(--fg-muted)]">faltam (com limite)</p>
          </div>
          <div title="Ainda não receberam nenhum lead hoje">
            <p className="text-xl font-semibold text-[var(--fg)]">{semLeadHoje}</p>
            <p className="text-[11px] text-[var(--fg-muted)]">sem lead hoje</p>
          </div>
          {semLimite > 0 && (
            <div title="Sem limite diário: recebem enquanto houver lead, na ordem do rodízio">
              <p className="text-xl font-semibold text-[var(--fg)]">{semLimite}</p>
              <p className="text-[11px] text-[var(--fg-muted)]">sem limite diário</p>
            </div>
          )}
        </div>
      </div>
      {linhas.length === 0 ? (
        <p className="text-sm text-[var(--fg-muted)]">Ninguém está marcado para receber leads.</p>
      ) : (
        <>
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            {pagina.itensDaPagina.map(({ v, pegou, limite, faltam, foraDoDia }) => (
              <div
                key={v.id}
                className={`flex items-center justify-between gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface-hover)]/60 px-3 py-2 text-sm ${foraDoDia ? "opacity-60" : ""}`}
              >
                <span className="truncate font-medium text-[var(--fg)]" title={v.nome}>
                  {v.nome}
                </span>
                <span className="shrink-0 text-right text-xs text-[var(--fg-muted)]">
                  <strong className="text-[var(--fg)]">{pegou}</strong> pegou
                  {foraDoDia ? (
                    <span className="block">não recebe hoje</span>
                  ) : faltam === null ? (
                    <span className="block">sem limite diário</span>
                  ) : (
                    <span className="block">
                      {faltam === 0 ? "limite atingido" : `faltam ${faltam}`} (de {limite})
                    </span>
                  )}
                </span>
              </div>
            ))}
          </div>
          <Pagination pagina={pagina.pagina} totalPaginas={pagina.totalPaginas} onChange={pagina.setPagina} />
        </>
      )}
    </Card>
  );
}

export function ManagementPage() {
  const [resumo, setResumo] = useState<GestaoComercialResumo | null>(null);
  const [vendedores, setVendedores] = useState<VendedorResumo[] | null>(null);
  const [historico, setHistorico] = useState<RedistribuicaoHistorico[] | null>(null);
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
    ])
      .then(([r, v, h]) => {
        setResumo(r);
        setVendedores(v);
        setHistorico(h);
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
  const [buscaNome, setBuscaNome] = useState("");
  const [filtroRegional, setFiltroRegional] = useState("");
  const [filtroSituacao, setFiltroSituacao] = useState<"" | "ativos" | "inativos">("");
  const [filtroRecebe, setFiltroRecebe] = useState<"" | "sim" | "nao">("");
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
  const paginaCarteira = usePaginacao(vendedoresFiltrados, 9);
  const paginaRanking = usePaginacao(rankingFiltrado, 10);
  const paginaParadas = usePaginacao(resumo?.oportunidadesSemMovimentacao, 8);
  const paginaHistorico = usePaginacao(historico, 10);

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

      <LeadsDoDiaCard vendedores={vendedores} />

      <div className="flex flex-wrap items-end gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
        <div className="w-56">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Consultor</label>
          <Input placeholder="Buscar pelo nome" value={buscaNome} onChange={(e) => setBuscaNome(e.target.value)} />
        </div>
        <div className="w-44">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Regional</label>
          <Select value={filtroRegional} onChange={(e) => setFiltroRegional(e.target.value)}>
            <option value="">Todas</option>
            {regionaisDisponiveis.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </Select>
        </div>
        <div className="w-40">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Situação</label>
          <Select value={filtroSituacao} onChange={(e) => setFiltroSituacao(e.target.value as typeof filtroSituacao)}>
            <option value="">Todos</option>
            <option value="ativos">Ativos</option>
            <option value="inativos">Inativos</option>
          </Select>
        </div>
        <div className="w-40">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Recebe leads</label>
          <Select value={filtroRecebe} onChange={(e) => setFiltroRecebe(e.target.value as typeof filtroRecebe)}>
            <option value="">Todos</option>
            <option value="sim">Recebe</option>
            <option value="nao">Não recebe</option>
          </Select>
        </div>
        {temFiltro && (
          <button
            type="button"
            className="focus-ring cursor-pointer pb-2 text-sm font-medium text-[var(--brand)] hover:underline"
            onClick={() => { setBuscaNome(""); setFiltroRegional(""); setFiltroSituacao(""); setFiltroRecebe(""); }}
          >
            Limpar filtros
          </button>
        )}
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="p-4">
          <h2 className="mb-2 text-sm font-semibold text-[var(--fg)]">Tempo médio até 1º contato</h2>
          {(resumo.leadsComPrimeiroContato ?? 0) === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">
              Nenhum lead do CRM teve contato neste mês. O contato conta quando o consultor move o lead de etapa no quadro ou conclui uma
              atividade.
            </p>
          ) : (
            <>
              <p className="text-2xl font-semibold text-[var(--fg)]">{formatarDuracao(resumo.tempoMedioPrimeiroContatoHoras)}</p>
              <p className="text-xs text-[var(--fg-muted)]">
                média de {resumo.leadsComPrimeiroContato} lead(s) deste mês, da chegada até o consultor mover de etapa ou concluir uma atividade
              </p>
              {contatoFiltrado.length > 0 && (
                <ul className="mt-3 space-y-1.5 border-t border-[var(--border)] pt-3">
                  {paginaContato.itensDaPagina.map((v) => (
                    <li key={v.vendedorId} className="flex items-center justify-between gap-2 text-xs">
                      <span className="truncate text-[var(--fg)]" title={v.vendedorNome}>
                        {v.vendedorNome}
                      </span>
                      <span className="shrink-0 text-[var(--fg-muted)]">
                        <strong className="text-[var(--fg)]">{formatarDuracao(v.horas)}</strong> · {v.leads} lead(s)
                      </span>
                    </li>
                  ))}
                </ul>
              )}
              <Pagination pagina={paginaContato.pagina} totalPaginas={paginaContato.totalPaginas} onChange={paginaContato.setPagina} />
            </>
          )}
        </Card>
        <Card className="p-4 lg:col-span-2">
          <h2 className="mb-2 text-sm font-semibold text-[var(--fg)]">Carteira por vendedor</h2>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {paginaCarteira.itensDaPagina.map((v) => (
              <div key={v.id} className="space-y-2 rounded-xl border border-[var(--border)] bg-[var(--surface-hover)]/60 p-3 text-sm">
                <div>
                  <p className="flex items-center gap-2 font-semibold text-[var(--fg)]" title={v.nome}>
                    <span className="truncate">{v.nome}</span>
                    {v.ativo === false && <Badge variant="danger">Inativo</Badge>}
                  </p>
                  <p className="text-xs text-[var(--fg-muted)]">
                    {v.leadsAtivos.toLocaleString("pt-BR")} leads · {v.oportunidadesAbertas} oportunidades
                  </p>
                  <p className="mt-1 text-xs" title="Leads de tráfego pago (Notion e sistema novo) que chegaram para o vendedor neste mês">
                    <span className="font-semibold text-[var(--brand)]">{(v.leadsTrafegoNoMes ?? 0).toLocaleString("pt-BR")}</span>{" "}
                    <span className="text-[var(--fg-muted)]">leads de tráfego pago no mês</span>
                    <span className="text-[var(--fg-muted)]">
                      {" "}· <span className="font-semibold text-[var(--fg)]">{(v.leadsTrafegoMesAnterior ?? 0).toLocaleString("pt-BR")}</span> no mês anterior
                    </span>
                  </p>
                </div>
                <div className="space-y-1.5 border-t border-[var(--border)] pt-2">
                  <LimiteInput tipo="mensal" vendedor={v} onSalvo={() => setRecarregar((n) => n + 1)} />
                  <LimiteInput tipo="diario" vendedor={v} onSalvo={() => setRecarregar((n) => n + 1)} />
                </div>
                <TiposLeadSelect key={`${v.id}-${v.recebeSomenteOQue?.join("|")}`} vendedor={v} onSalvo={() => setRecarregar((n) => n + 1)} />
                <JanelaRecebimento key={`${v.id}-${v.horarioInicioLeads}-${v.horarioFimLeads}-${v.diasSemanaLeads?.join("")}`} vendedor={v} onSalvo={() => setRecarregar((n) => n + 1)} />
                <RecebeLeadsSwitch vendedor={v} onSalvo={() => setRecarregar((n) => n + 1)} />
              </div>
            ))}
          </div>
          <Pagination pagina={paginaCarteira.pagina} totalPaginas={paginaCarteira.totalPaginas} onChange={paginaCarteira.setPagina} />
        </Card>
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
                    <td className="py-2 text-[var(--fg-muted)]">{r.posicao}</td>
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
    </div>
  );
}
