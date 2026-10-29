import { AlertTriangle, BellRing, CalendarClock, ChevronRight, CircleDollarSign, FileDown, HandCoins, Search, Send, Target, Users } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarData, formatarMoeda } from "../../lib/format";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import { usePaginacao } from "../../lib/usePaginacao";
import { Avatar, Badge, Button, Card, EmptyState, ErrorState, Input, Label, Modal, MoneyInput, Pagination, Skeleton, Textarea, useToast } from "../../components/ui";
import { StatCard } from "../../components/crm/StatCard";

interface Resumo {
  consultorId: string;
  consultorNome: string;
  tipoContrato: "Pj" | "Clt" | null;
  modeloPj: "BonusPorPlaca" | "Recorrencia" | null;
  emAberto: number;
  cobrancasEmAberto: number;
  cobrancasAtrasadas: number;
  avisosAbertos: number;
  proximaCobranca: string | null;
  custoLeadMes: number;
  leadsMes: number;
  /** Tem cobranças (de contrato ou automáticas, do padrão da gestão). */
  temCobrancas: boolean;
  /** Cobranças que o consultor disse ter pago e o financeiro ainda não conferiu. */
  aguardandoConferencia: number;
}

type Situacao = "todos" | "atrasados" | "conferir" | "pendentes" | "avisos" | "emdia";

const SITUACOES: { chave: Situacao; rotulo: string }[] = [
  { chave: "todos", rotulo: "Todos" },
  { chave: "atrasados", rotulo: "Com atraso" },
  { chave: "conferir", rotulo: "Para conferir" },
  { chave: "pendentes", rotulo: "Em aberto" },
  { chave: "avisos", rotulo: "Com aviso" },
  { chave: "emdia", rotulo: "Em dia" },
];

function daSituacao(r: Resumo, situacao: Situacao) {
  switch (situacao) {
    case "atrasados": return r.cobrancasAtrasadas > 0;
    case "conferir": return r.aguardandoConferencia > 0;
    case "pendentes": return r.cobrancasEmAberto > 0;
    case "avisos": return r.avisosAbertos > 0;
    case "emdia": return (r.tipoContrato !== null || r.temCobrancas) && r.cobrancasEmAberto === 0 && r.avisosAbertos === 0;
    default: return true;
  }
}

/** Mensagem pronta do aviso em lote (o gestor pode editar antes de enviar). */
const MENSAGEM_PADRAO = "Olá! Há cobranças em aberto no seu Setor Financeiro. Confira o valor no Controle financeiro, faça o pagamento o quanto antes e avise o financeiro depois de pagar.";

function baixarCsv(nome: string, linhas: string[][]) {
  const texto = linhas.map((l) => l.map((c) => `"${c.replace(/"/g, '""')}"`).join(";")).join("\r\n");
  const url = URL.createObjectURL(new Blob(["\uFEFF" + texto], { type: "text/csv;charset=utf-8" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = nome;
  a.click();
  URL.revokeObjectURL(url);
}

function tipoRotulo(r: Resumo) {
  if (r.tipoContrato === "Clt") return "CLT";
  if (r.tipoContrato === "Pj") return `PJ · ${r.modeloPj === "Recorrencia" ? "recorrência" : "bônus por placa"}`;
  if (r.temCobrancas) return "Sem contrato · valores padrão";
  return "Sem contrato";
}

/**
 * Setor Financeiro: a visão do financeiro e da gestão sobre cada consultor — o que ele deve à CSS (notebook em parcelas, custo de lead, tráfego
 * pago), o que está atrasado e os avisos de pagamento. Cada consultor abre a própria página, onde tudo isso é registrado e editado.
 */
export function SetorFinanceiroPage() {
  const navigate = useNavigate();
  const { notificar } = useToast();
  const [dados, setDados] = useState<Resumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [busca, setBusca] = useState("");
  const [situacao, setSituacao] = useState<Situacao>("todos");
  // Aviso em lote: consultores marcados na tabela e a mensagem/valor do aviso.
  const [marcados, setMarcados] = useState<Set<string>>(new Set());
  const [avisoAberto, setAvisoAberto] = useState(false);
  const [mensagem, setMensagem] = useState(MENSAGEM_PADRAO);
  const [valorAviso, setValorAviso] = useState<number | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<Resumo[]>("/crm/setor-financeiro/resumo", controller.signal)
      .then((r) => {
        setDados(r);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o Setor Financeiro.");
      });
    return () => controller.abort();
  }, [recarregar]);
  useAtualizarAoVivo(useCallback(() => setRecarregar((n) => n + 1), []));

  const totais = useMemo(() => {
    const lista = dados ?? [];
    return {
      consultores: lista.length,
      emAberto: lista.reduce((n, r) => n + r.emAberto, 0),
      atrasadas: lista.reduce((n, r) => n + r.cobrancasAtrasadas, 0),
      avisos: lista.reduce((n, r) => n + r.avisosAbertos, 0),
      custoLead: lista.reduce((n, r) => n + r.custoLeadMes, 0),
      leads: lista.reduce((n, r) => n + r.leadsMes, 0),
      conferir: lista.reduce((n, r) => n + r.aguardandoConferencia, 0),
    };
  }, [dados]);

  const filtrados = useMemo(() => {
    const termo = busca.trim().toLowerCase();
    return (dados ?? [])
      .filter((r) => r.consultorNome.toLowerCase().includes(termo))
      .filter((r) => daSituacao(r, situacao))
      // Quem mais precisa de atenção primeiro: atrasados, depois maior valor em aberto.
      .sort((a, b) => b.aguardandoConferencia - a.aguardandoConferencia || b.cobrancasAtrasadas - a.cobrancasAtrasadas || b.emAberto - a.emAberto || a.consultorNome.localeCompare(b.consultorNome));
  }, [dados, busca, situacao]);
  const pagina = usePaginacao(filtrados, 12);
  const contagens = useMemo(
    () => Object.fromEntries(SITUACOES.map((x) => [x.chave, (dados ?? []).filter((r) => daSituacao(r, x.chave)).length])) as Record<Situacao, number>,
    [dados],
  );
  const marcadosVisiveis = filtrados.filter((r) => marcados.has(r.consultorId));
  const todosMarcados = filtrados.length > 0 && marcadosVisiveis.length === filtrados.length;
  const alternar = (id: string) => setMarcados((atual) => {
    const novo = new Set(atual);
    if (!novo.delete(id)) novo.add(id);
    return novo;
  });
  const marcarTodos = () => setMarcados(todosMarcados ? new Set() : new Set(filtrados.slice(0, 100).map((r) => r.consultorId)));

  async function enviarAviso() {
    setEnviando(true);
    try {
      const ids = marcadosVisiveis.map((r) => r.consultorId);
      await api.post("/crm/avisos-pagamento", { consultorIds: ids, mensagem: mensagem.trim(), valor: ids.length === 1 ? valorAviso : null, referencia: null });
      notificar("success", ids.length === 1 ? "Aviso enviado." : `Aviso enviado para ${ids.length} consultores.`);
      setAvisoAberto(false);
      setMarcados(new Set());
      setMensagem(MENSAGEM_PADRAO);
      setValorAviso(null);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível enviar o aviso.");
    } finally {
      setEnviando(false);
    }
  }

  function exportar() {
    const numero = (v: number) => v.toFixed(2).replace(".", ",");
    baixarCsv(`setor-financeiro-${new Date().toISOString().slice(0, 10)}.csv`, [
      ["Consultor", "Contrato", "Leads no mês", "Custo de lead", "Em aberto", "Cobranças em aberto", "Atrasadas", "Para conferir", "Avisos em aberto", "Próxima cobrança"],
      ...filtrados.map((r) => [
        r.consultorNome, tipoRotulo(r), String(r.leadsMes), numero(r.custoLeadMes), numero(r.emAberto),
        String(r.cobrancasEmAberto), String(r.cobrancasAtrasadas), String(r.aguardandoConferencia), String(r.avisosAbertos), r.proximaCobranca ? formatarData(r.proximaCobranca) : "",
      ]),
    ]);
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h1 className="text-xl font-bold text-[var(--fg)]">Setor Financeiro</h1>
          <p className="text-sm text-[var(--fg-muted)]">
            O que cada consultor deve à CSS — coworking, custo de lead, tráfego pago e custos em parcelas, como o notebook — e os avisos de pagamento. Abra um consultor para registrar pagamentos e editar os custos.
          </p>
        </div>
        <Link to="/app/crm/setor-financeiro/grupos" className="focus-ring inline-flex items-center gap-1.5 rounded-lg border border-[var(--border)] px-3 py-2 text-sm font-semibold text-[var(--brand)] hover:bg-[var(--surface-hover)]">
          <Users className="size-4" aria-hidden /> Contrato dos grupos
        </Link>
      </div>

      {erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados ? (
        <div className="space-y-3">
          <Skeleton className="h-20" />
          <Skeleton className="h-40" />
        </div>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
            <StatCard titulo="Custo de lead no mês" valor={formatarMoeda(totais.custoLead)} icone={Target} tom="brand" subtitulo={`${totais.leads.toLocaleString("pt-BR")} lead(s) de tráfego pago e Notion`} />
            <StatCard titulo="Consultores" valor={String(totais.consultores)} icone={Users} tom="brand" subtitulo="ativos no seu escopo" />
            <StatCard titulo="Total em aberto" valor={formatarMoeda(totais.emAberto)} icone={CircleDollarSign} tom={totais.emAberto > 0 ? "warning" : "success"} subtitulo="cobranças ainda não pagas" />
            <StatCard titulo="Para conferir" valor={String(totais.conferir)} icone={HandCoins} tom={totais.conferir > 0 ? "warning" : "success"} subtitulo="pagamentos informados" clicavel={{ onClick: () => setSituacao("conferir"), className: "cursor-pointer" }} />
            <StatCard titulo="Cobranças atrasadas" valor={String(totais.atrasadas)} icone={AlertTriangle} tom={totais.atrasadas > 0 ? "danger" : "success"} subtitulo="de meses que já acabaram" />
            <StatCard titulo="Avisos em aberto" valor={String(totais.avisos)} icone={BellRing} tom={totais.avisos > 0 ? "warning" : "success"} subtitulo="enviados aos consultores" />
          </div>

          <div className="filtros-painel flex flex-wrap items-center gap-2">
            <div className="relative w-full sm:w-72">
              <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
              <Input className="!pl-9" placeholder="Buscar consultor…" aria-label="Buscar consultor" value={busca} onChange={(e) => setBusca(e.target.value)} />
            </div>
            <div className="flex flex-wrap items-center gap-1.5" role="group" aria-label="Situação">
              {SITUACOES.map((x) => (
                <button
                  key={x.chave}
                  type="button"
                  aria-pressed={situacao === x.chave}
                  onClick={() => setSituacao(x.chave)}
                  className={`focus-ring inline-flex cursor-pointer items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-semibold transition ${
                    situacao === x.chave ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]" : "border-[var(--border)] text-[var(--fg-muted)] hover:bg-[var(--surface-hover)]"
                  }`}
                >
                  {x.rotulo}
                  <span className="rounded-full bg-[var(--surface-hover)] px-1.5 text-[11px] [font-variant-numeric:tabular-nums]">{contagens[x.chave]}</span>
                </button>
              ))}
            </div>
            <Button variant="secondary" size="sm" className="ml-auto" onClick={exportar} disabled={filtrados.length === 0} title="Baixa a lista filtrada em CSV (abre no Excel)">
              <FileDown className="size-4" /> Exportar
            </Button>
          </div>

          {filtrados.length === 0 ? (
            <EmptyState title="Nenhum consultor" description={dados.length === 0 ? "Não há consultores para mostrar." : "Nenhum consultor com esse filtro."} />
          ) : (
            <>
              {marcadosVisiveis.length > 0 && (
                <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-[var(--brand)] bg-[var(--brand-soft)] px-4 py-2.5 text-sm">
                  <span className="font-medium text-[var(--fg)]">{marcadosVisiveis.length} consultor(es) marcado(s)</span>
                  <div className="flex gap-2">
                    <Button size="sm" variant="ghost" onClick={() => setMarcados(new Set())}>Limpar</Button>
                    <Button size="sm" onClick={() => setAvisoAberto(true)}><Send className="size-4" /> Enviar aviso</Button>
                  </div>
                </div>
              )}
              <Card className="overflow-x-auto">
                <table className="w-full min-w-[60rem] text-sm [font-variant-numeric:tabular-nums]">
                  <thead>
                    <tr className="text-left">
                      <th className="w-10 border-b border-[var(--border)] py-2.5 pl-4">
                        <input type="checkbox" aria-label="Marcar todos os consultores da lista" checked={todosMarcados} onChange={marcarTodos} className="size-4 cursor-pointer accent-[var(--brand)]" />
                      </th>
                      {["Consultor", "Contrato", "Leads no mês", "Custo de lead", "Em aberto", "Situação", ""].map((h, i) => (
                        <th key={`${h}${i}`} className={`border-b border-[var(--border)] px-4 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)] ${i >= 2 && i <= 4 ? "text-right" : ""}`}>{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {pagina.itensDaPagina.map((r) => {
                      const pj = r.tipoContrato === "Pj" || (r.tipoContrato === null && r.temCobrancas);
                      const semContrato = r.tipoContrato === null && !r.temCobrancas;
                      return (
                        <tr
                          key={r.consultorId}
                          onClick={() => navigate(`/app/crm/setor-financeiro/${r.consultorId}`)}
                          className="group cursor-pointer border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--brand-soft)]"
                        >
                          <td className="w-10 py-2.5 pl-4" onClick={(e) => e.stopPropagation()}>
                            <input type="checkbox" aria-label={`Marcar ${r.consultorNome}`} checked={marcados.has(r.consultorId)} onChange={() => alternar(r.consultorId)} className="size-4 cursor-pointer accent-[var(--brand)]" />
                          </td>
                          <td className="px-4 py-2.5">
                            <Link
                              to={`/app/crm/setor-financeiro/${r.consultorId}`}
                              onClick={(e) => e.stopPropagation()}
                              className="focus-ring flex items-center gap-3 rounded"
                              aria-label={`Abrir o Setor Financeiro de ${r.consultorNome}`}
                            >
                              <Avatar nome={r.consultorNome} usuarioId={r.consultorId} className="size-8 text-xs" />
                              <span className="min-w-0 truncate font-semibold text-[var(--fg)]" title={r.consultorNome}>{r.consultorNome}</span>
                            </Link>
                          </td>
                          <td className="whitespace-nowrap px-4 py-2.5 text-[var(--fg-muted)]">{tipoRotulo(r)}</td>
                          <td className="px-4 py-2.5 text-right">{pj ? r.leadsMes.toLocaleString("pt-BR") : "—"}</td>
                          <td className="px-4 py-2.5 text-right font-medium text-[var(--fg)]" title="Valor por lead × leads de tráfego pago e Notion do mês">{pj ? formatarMoeda(r.custoLeadMes) : "—"}</td>
                          <td className={`px-4 py-2.5 text-right font-bold ${semContrato ? "text-[var(--fg-muted)]" : r.emAberto > 0 ? "text-[var(--fg)]" : "text-[var(--success)]"}`}>
                            {semContrato ? "—" : formatarMoeda(r.emAberto)}
                          </td>
                          <td className="px-4 py-2.5">
                            <div className="flex flex-wrap items-center gap-1">
                              {r.aguardandoConferencia > 0 && <Badge variant="info">{r.aguardandoConferencia} p/ conferir</Badge>}
                              {r.cobrancasAtrasadas > 0 && <Badge variant="danger">{r.cobrancasAtrasadas} atrasada(s)</Badge>}
                              {r.cobrancasAtrasadas === 0 && r.cobrancasEmAberto > 0 && <Badge variant="warning">{r.cobrancasEmAberto} em aberto</Badge>}
                              {r.avisosAbertos > 0 && <Badge variant="warning">{r.avisosAbertos} aviso(s)</Badge>}
                              {r.cobrancasEmAberto === 0 && r.avisosAbertos === 0 && (semContrato ? <Badge variant="neutral">Sem contrato</Badge> : <Badge variant="success">Em dia</Badge>)}
                              {r.proximaCobranca && (
                                <span className="inline-flex items-center gap-1 text-xs text-[var(--fg-muted)]" title="Uma nova cobrança começa nesta data">
                                  <CalendarClock className="size-3.5 text-[var(--warning)]" aria-hidden /> {formatarData(r.proximaCobranca)}
                                </span>
                              )}
                            </div>
                          </td>
                          <td className="px-4 py-2.5 text-right">
                            <ChevronRight className="inline size-4 text-[var(--fg-muted)] transition-transform group-hover:translate-x-0.5" aria-hidden />
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </Card>
              <Pagination pagina={pagina.pagina} totalPaginas={pagina.totalPaginas} onChange={pagina.setPagina} />
            </>
          )}
        </>
      )}

      <Modal
        open={avisoAberto}
        onClose={() => setAvisoAberto(false)}
        title={marcadosVisiveis.length === 1 ? `Aviso para ${marcadosVisiveis[0].consultorNome}` : `Aviso para ${marcadosVisiveis.length} consultores`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setAvisoAberto(false)}>Cancelar</Button>
            <Button onClick={enviarAviso} loading={enviando} disabled={mensagem.trim().length < 3 || marcadosVisiveis.length === 0}>
              <Send className="size-4" /> Enviar aviso
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-[var(--fg-muted)]">Cada consultor recebe a notificação e vê o aviso no card de avisos importantes até ser resolvido.</p>
          <div>
            <Label htmlFor="lote-mensagem" required>Mensagem</Label>
            <Textarea id="lote-mensagem" rows={4} maxLength={1000} value={mensagem} onChange={(e) => setMensagem(e.target.value)} />
          </div>
          {marcadosVisiveis.length === 1 && (
            <div>
              <Label htmlFor="lote-valor">Valor em aberto (opcional)</Label>
              <MoneyInput id="lote-valor" value={valorAviso} onChange={setValorAviso} />
            </div>
          )}
        </div>
      </Modal>
    </div>
  );
}
