import { AlertTriangle, BellRing, CalendarClock, CheckCircle2, CircleDollarSign, FileCheck2, Laptop, Paperclip, Pencil, RotateCcw, Target, Trash2, Undo2, XCircle } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, isAbortError, uploadFile } from "../../lib/api";
import { formatarData, formatarMoeda } from "../../lib/format";
import { Badge, Button, Card, ConfirmDialog, EmptyState, ErrorState, Input, Label, Modal, MoneyInput, Select, Skeleton, Textarea, useToast } from "../ui";
import { StatCard } from "../crm/StatCard";
import { DataInput, MesInput } from "../PeriodoInput";

// ---------- Tipos (espelham SetorFinanceiroController) ----------

export interface Cobranca {
  item: "Adicional" | "Coworking" | "CustoLead" | "TrafegoPago";
  rotulo: string;
  descricao: string;
  competencia: string;
  valor: number;
  pago: boolean;
  pagoEm: string | null;
  observacao: string | null;
  atrasada: boolean;
  /** Custo adicional (notebook, celular...) a que a parcela pertence. */
  custoId?: string | null;
  /** Data de vencimento da parcela (custos adicionais). */
  vencimento?: string | null;
  /** O consultor avisou que pagou: aguarda o financeiro conferir e confirmar. */
  informadoPeloConsultor?: boolean;
  dataInformada?: string | null;
  comprovanteUrl?: string | null;
  /** "Pagar depois": dia em que o consultor quer ser lembrado. */
  lembreteEm?: string | null;
  /** O financeiro não confirmou o pagamento informado. */
  motivoRecusa?: string | null;
  observacaoConsultor?: string | null;
  /** Valor ajustado à mão: o que o contrato calculava (o `valor` é o combinado). */
  valorOriginal?: number | null;
}

export interface SetorFinanceiroConsultor {
  consultorId: string;
  consultorNome: string;
  tipoContrato: "Pj" | "Clt" | null;
  podeEditar: boolean;
  emAberto: number;
  pago: number;
  cobrancasEmAberto: number;
  cobrancasAtrasadas: number;
  cobrancas: Cobranca[];
  custoLeadMes: number;
  leadsMes: number;
  custoLeadTotal: number;
  custos?: CustoAdicional[];
  /** Cobranças removidas de algum mês: dá para restaurar. */
  removidas?: Cobranca[];
}

export interface CustoAdicional {
  id: string;
  tipo: string;
  rotulo: string;
  descricao: string | null;
  valorTotal: number;
  parcelas: number;
  valorParcela: number;
  primeiraParcela: string;
  parcelasPagas: number;
  parcelaAtual: number;
  criadoPor: string | null;
  criadoEm: string;
}

const MESES = ["Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"];
const mesAno = (iso: string) => `${MESES[Number(iso.slice(5, 7)) - 1]}/${iso.slice(0, 4)}`;
const hojeISO = () => new Date().toISOString().slice(0, 10);

/**
 * O que a CSS cobra de um consultor (custos adicionais em parcelas, como o notebook; custo de lead e tráfego pago do PJ), mês a mês, e se cada
 * cobrança foi paga. A gestão e o financeiro registram os pagamentos; o consultor só vê as próprias cobranças.
 */
export function CobrancasDoConsultor({ consultorId, onMudou, onCobrar }: { consultorId: string; onMudou?: () => void; onCobrar?: (cobranca: Cobranca) => void }) {
  const { notificar } = useToast();
  const [dados, setDados] = useState<SetorFinanceiroConsultor | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [filtro, setFiltro] = useState<"abertas" | "conferir" | "pagas" | "todas">("abertas");
  const [item, setItem] = useState("");
  const [pagando, setPagando] = useState<Cobranca | null>(null);
  const [informando, setInformando] = useState<Cobranca | null>(null);
  const [lembrando, setLembrando] = useState<Cobranca | null>(null);
  const [recusando, setRecusando] = useState<Cobranca | null>(null);
  // Edição pela gestão: valor / início da cobrança, o custo (tipo, parcelas, data) e remoção de um mês ou do custo inteiro.
  const [editando, setEditando] = useState<Cobranca | null>(null);
  const [removendo, setRemovendo] = useState<Cobranca | null>(null);
  const [mostrarRemovidas, setMostrarRemovidas] = useState(false);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setErro(null);
      api
        .get<SetorFinanceiroConsultor>(`/crm/setor-financeiro/cobrancas?consultorId=${consultorId}`, signal)
        .then(setDados)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar as cobranças.");
        });
    },
    [consultorId]
  );

  useEffect(() => {
    setDados(null);
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  const aposSalvar = () => {
    carregar();
    onMudou?.();
  };

  const lista = useMemo(
    () =>
      (dados?.cobrancas ?? [])
        .filter((c) => filtro === "todas" || (filtro === "abertas" ? !c.pago : filtro === "conferir" ? !!c.informadoPeloConsultor : c.pago))
        .filter((c) => !item || c.item === item),
    [dados, filtro, item]
  );

  async function registrar(c: Cobranca, pago: boolean, pagoEm: string | null, observacao: string | null) {
    try {
      await api.put(`/crm/setor-financeiro/${consultorId}/pagamentos`, { item: c.item, competencia: c.competencia, pago, pagoEm, observacao, custoId: c.custoId ?? null });
      notificar("success", pago ? `${c.rotulo} de ${mesAno(c.competencia)} marcado como pago.` : `${c.rotulo} de ${mesAno(c.competencia)} voltou para em aberto.`);
      setPagando(null);
      aposSalvar();
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar.");
    }
  }

  async function ajustar(c: Cobranca, valor: number | null, remover: boolean, ok: string) {
    try {
      await api.put(`/crm/setor-financeiro/${consultorId}/cobrancas/ajuste`, { item: c.item, competencia: c.competencia, custoId: c.custoId ?? null, valor, remover });
      notificar("success", ok);
      setEditando(null);
      setRemovendo(null);
      aposSalvar();
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar.");
    }
  }

  async function informar(c: Cobranca, dataPagamento: string, observacao: string | null, arquivo: File | null) {
    try {
      await uploadFile(`/crm/setor-financeiro/cobrancas/informar`, arquivo, {
        item: c.item, competencia: c.competencia, ...(c.custoId ? { custoId: c.custoId } : {}), dataPagamento, ...(observacao ? { observacao } : {}),
      });
      notificar("success", "Pagamento informado. O financeiro vai conferir e confirmar.");
      setInformando(null);
      aposSalvar();
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível informar o pagamento.");
    }
  }

  async function agendarLembrete(c: Cobranca, data: string | null) {
    try {
      await api.put(`/crm/setor-financeiro/cobrancas/lembrete`, { item: c.item, competencia: c.competencia, custoId: c.custoId ?? null, data });
      notificar("success", data ? `Combinado: você será lembrado em ${formatarData(data)}.` : "Lembrete removido.");
      setLembrando(null);
      aposSalvar();
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível agendar o lembrete.");
    }
  }

  async function recusar(c: Cobranca, motivo: string) {
    try {
      await api.put(`/crm/setor-financeiro/${consultorId}/pagamentos/recusar`, { item: c.item, competencia: c.competencia, custoId: c.custoId ?? null, motivo });
      notificar("success", "Devolvido ao consultor, com o motivo.");
      setRecusando(null);
      aposSalvar();
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível devolver a cobrança.");
    }
  }

  if (erro) return <ErrorState message={erro} onRetry={() => carregar()} />;
  if (!dados) return <Skeleton className="h-56 rounded-2xl" />;

  if (dados.tipoContrato !== "Pj" && dados.cobrancas.length === 0) {
    return (
      <EmptyState
        title="Nenhuma cobrança"
        description={dados.podeEditar
          ? "Não há cobranças para este consultor. Lance um custo (notebook, celular...) em \"Contrato e custos\", ou defina o contrato PJ para gerar as de lead e tráfego."
          : "Você não tem cobranças da CSS no momento."}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <StatCard titulo="Custo de lead no mês" valor={formatarMoeda(dados.custoLeadMes)} icone={Target} tom="brand" subtitulo={`${dados.leadsMes.toLocaleString("pt-BR")} lead(s) · total ${formatarMoeda(dados.custoLeadTotal)}`} />
        <StatCard titulo="Em aberto" valor={formatarMoeda(dados.emAberto)} icone={CircleDollarSign} tom={dados.emAberto > 0 ? "warning" : "success"} subtitulo={`${dados.cobrancasEmAberto} cobrança(s)`} />
        <StatCard titulo="Atrasadas" valor={String(dados.cobrancasAtrasadas)} icone={AlertTriangle} tom={dados.cobrancasAtrasadas > 0 ? "danger" : "success"} subtitulo="de meses que já acabaram" />
        <StatCard titulo="Já pago" valor={formatarMoeda(dados.pago)} icone={CheckCircle2} tom="success" subtitulo="desde o início do contrato" />
        <StatCard
          titulo="Custos lançados"
          valor={String(dados.custos?.length ?? 0)}
          icone={Laptop}
          tom="brand"
          subtitulo={dados.custos?.length ? `${dados.custos.reduce((n, c) => n + c.parcelasPagas, 0)} parcela(s) paga(s)` : "notebook, celular…"}
        />
      </div>

      <Card className="overflow-x-auto">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--border)] px-5 py-3">
          <div>
            <p className="text-base font-semibold text-[var(--fg)]">Cobranças da CSS</p>
            <p className="text-xs text-[var(--fg-muted)]">Coworking, custo de lead (valor por lead × leads do mês), tráfego pago e os custos lançados pelo financeiro (em parcelas). Aqui se registra se foi pago.</p>
          </div>
          <div className="filtros-painel flex flex-wrap gap-2">
            <Select aria-label="Item" value={item} onChange={(e) => setItem(e.target.value)} className="!w-auto">
              <option value="">Todos os itens</option>
              <option value="Adicional">Custos lançados (notebook…)</option>
              <option value="Coworking">Coworking</option>
              <option value="CustoLead">Custo de lead</option>
              <option value="TrafegoPago">Tráfego pago</option>
            </Select>
            <Select aria-label="Situação" value={filtro} onChange={(e) => setFiltro(e.target.value as typeof filtro)} className="!w-auto">
              <option value="abertas">Em aberto</option>
              <option value="conferir">Aguardando conferência</option>
              <option value="pagas">Pagas</option>
              <option value="todas">Todas</option>
            </Select>
          </div>
        </div>
        {lista.length === 0 ? (
          <p className="px-5 py-8 text-center text-sm text-[var(--fg-muted)]">
            {dados.cobrancas.length === 0 ? "Nenhuma cobrança ainda: o coworking, o custo de lead e o tráfego começam a valer conforme o contrato." : filtro === "abertas" ? "Nenhuma cobrança em aberto." : "Nada por aqui."}
          </p>
        ) : (
          <table className="w-full min-w-[40rem] text-sm [font-variant-numeric:tabular-nums]">
            <thead>
              <tr className="text-left">
                {["Mês", "Item", "Valor", "Situação", ""].map((h, i) => (
                  <th key={`${h}${i}`} className={`border-b border-[var(--border)] px-4 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)] ${i === 2 ? "text-right" : ""}`}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {lista.map((c) => (
                <tr key={`${c.item}${c.custoId ?? ""}${c.competencia}`} className="border-b border-[var(--border)] last:border-0 hover:bg-[var(--brand-soft)]">
                  <td className="whitespace-nowrap px-4 py-2.5 font-medium text-[var(--fg)]">{mesAno(c.competencia)}</td>
                  <td className="px-4 py-2.5">
                    <span className="font-medium text-[var(--fg)]">{c.rotulo}</span>
                    {c.item !== "TrafegoPago" && <span className="block text-xs text-[var(--fg-muted)]">{c.descricao.replace("Custo de leads — ", "").replace(" (fixo)", "")}{c.vencimento ? ` · vence em ${formatarData(c.vencimento)}` : ""}</span>}
                  </td>
                  <td className="px-4 py-2.5 text-right font-semibold">
                    {c.valorOriginal != null && <span className="mr-1.5 text-xs font-normal text-[var(--fg-muted)] line-through" title="Valor calculado pelo contrato">{formatarMoeda(c.valorOriginal)}</span>}
                    {formatarMoeda(c.valor)}
                    {c.valorOriginal != null && <span className="block text-[11px] font-normal text-[var(--warning)]">valor ajustado</span>}
                  </td>
                  <td className="px-4 py-2.5">
                    {c.pago ? (
                      <span>
                        <Badge variant="success">Pago{c.pagoEm ? ` em ${formatarData(c.pagoEm)}` : ""}</Badge>
                        {c.observacao && <span className="block text-xs text-[var(--fg-muted)]">{c.observacao}</span>}
                      </span>
                    ) : c.informadoPeloConsultor ? (
                      <span>
                        <Badge variant="info">Aguardando conferência</Badge>
                        <span className="block text-xs text-[var(--fg-muted)]">
                          {dados.podeEditar ? "Consultor informou" : "Você informou"} que pagou{c.dataInformada ? ` em ${formatarData(c.dataInformada)}` : ""}
                          {c.comprovanteUrl && (
                            <>
                              {" · "}
                              <a href={c.comprovanteUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-0.5 font-medium text-[var(--brand)] hover:underline">
                                <Paperclip className="size-3" aria-hidden /> comprovante
                              </a>
                            </>
                          )}
                        </span>
                        {c.observacaoConsultor && <span className="block text-xs text-[var(--fg-muted)]">“{c.observacaoConsultor}”</span>}
                      </span>
                    ) : (
                      <span>
                        {c.atrasada ? <Badge variant="danger">Atrasada</Badge> : <Badge variant="warning">Em aberto</Badge>}
                        {c.lembreteEm && (
                          <span className="mt-0.5 flex items-center gap-1 text-xs text-[var(--fg-muted)]">
                            <CalendarClock className="size-3 text-[var(--brand)]" aria-hidden /> lembrete em {formatarData(c.lembreteEm)}
                          </span>
                        )}
                        {c.motivoRecusa && <span className="mt-0.5 block text-xs text-[var(--danger)]">Não confirmado: {c.motivoRecusa}</span>}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-2.5 text-right">
                    {dados.podeEditar && (
                      <span className="mr-2 inline-flex items-center gap-1 align-middle">
                        <button type="button" aria-label={`Editar ${c.rotulo} de ${mesAno(c.competencia)}`} title="Editar valor, tipo e início da cobrança" onClick={() => setEditando(c)} className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--brand)]">
                          <Pencil className="size-4" />
                        </button>
                        {!c.pago && (
                          <button type="button" aria-label={`Remover ${c.rotulo} de ${mesAno(c.competencia)}`} title="Remover esta cobrança" onClick={() => setRemovendo(c)} className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--danger)]">
                            <Trash2 className="size-4" />
                          </button>
                        )}
                      </span>
                    )}
                    {!dados.podeEditar && !c.pago && !c.informadoPeloConsultor && (
                      <span className="inline-flex items-center gap-2">
                        <Button size="sm" variant="ghost" onClick={() => setLembrando(c)} title="Escolha um dia para o sistema te lembrar de pagar">
                          <CalendarClock className="size-4" /> {c.lembreteEm ? "Alterar lembrete" : "Pagar depois"}
                        </Button>
                        <Button size="sm" variant="secondary" onClick={() => setInformando(c)} title="Avise que pagou (pode anexar o comprovante): o financeiro confere">
                          <CheckCircle2 className="size-4" /> Já paguei
                        </Button>
                      </span>
                    )}
                    {!dados.podeEditar && !c.pago && c.informadoPeloConsultor && (
                      <Button size="sm" variant="secondary" onClick={() => setInformando(c)} title="Anexe ou troque o comprovante de pagamento">
                        <Paperclip className="size-4" /> {c.comprovanteUrl ? "Trocar comprovante" : "Anexar comprovante"}
                      </Button>
                    )}
                    {dados.podeEditar && c.informadoPeloConsultor && !c.pago ? (
                      <span className="inline-flex items-center gap-2">
                        <Button size="sm" variant="ghost" onClick={() => setRecusando(c)} title="Não conferiu: devolve ao consultor com o motivo">
                          <XCircle className="size-4" /> Não confirmar
                        </Button>
                        <Button size="sm" onClick={() => setPagando(c)} title="Conferiu que foi pago: marca como pago">
                          <FileCheck2 className="size-4" /> Confirmar pagamento
                        </Button>
                      </span>
                    ) : null}
                    {dados.podeEditar &&
                      !(c.informadoPeloConsultor && !c.pago) &&
                      (c.pago ? (
                        <Button size="sm" variant="ghost" onClick={() => registrar(c, false, null, null)} title="Volta a cobrança para em aberto">
                          <Undo2 className="size-4" /> Desfazer
                        </Button>
                      ) : (
                        <span className="inline-flex items-center gap-2">
                          {onCobrar && (
                            <Button size="sm" variant="ghost" onClick={() => onCobrar(c)} title="Abre o aviso de pagamento já com a mensagem e o valor desta cobrança">
                              <BellRing className="size-4" /> Cobrar
                            </Button>
                          )}
                          <Button size="sm" variant="secondary" onClick={() => setPagando(c)}>
                            <CheckCircle2 className="size-4" /> Marcar como pago
                          </Button>
                        </span>
                      ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      {dados.podeEditar && (dados.removidas?.length ?? 0) > 0 && (
        <Card className="p-4">
          <button type="button" onClick={() => setMostrarRemovidas((v) => !v)} aria-expanded={mostrarRemovidas} className="focus-ring flex w-full cursor-pointer items-center justify-between rounded text-left text-sm font-semibold text-[var(--fg)]">
            Cobranças removidas ({dados.removidas!.length})
            <span className="text-xs font-medium text-[var(--brand)]">{mostrarRemovidas ? "Ocultar" : "Mostrar"}</span>
          </button>
          {mostrarRemovidas && (
            <ul className="mt-3 divide-y divide-[var(--border)] text-sm">
              {dados.removidas!.map((c) => (
                <li key={`${c.item}${c.custoId ?? ""}${c.competencia}`} className="flex flex-wrap items-center justify-between gap-2 py-2">
                  <span className="text-[var(--fg)]">
                    <strong>{c.rotulo}</strong> de {mesAno(c.competencia)} · <span className="[font-variant-numeric:tabular-nums]">{formatarMoeda(c.valorOriginal ?? c.valor)}</span>
                    <span className="block text-xs text-[var(--fg-muted)]">{c.descricao.replace("Custo de leads — ", "").replace(" (fixo)", "")}</span>
                  </span>
                  <Button size="sm" variant="secondary" onClick={() => ajustar(c, null, false, `${c.rotulo} de ${mesAno(c.competencia)} voltou às cobranças.`)}>
                    <RotateCcw className="size-4" /> Restaurar
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </Card>
      )}

      <EditarCobrancaDialog
        cobranca={editando}
        consultorId={consultorId}
        custo={editando?.custoId ? dados.custos?.find((x) => x.id === editando.custoId) ?? null : null}
        onFechar={() => setEditando(null)}
        onMudou={() => { setEditando(null); aposSalvar(); }}
      />
      <ConfirmDialog
        open={!!removendo}
        danger
        title="Remover cobrança"
        confirmLabel="Remover"
        message={removendo ? `Remover ${removendo.rotulo} de ${mesAno(removendo.competencia)} (${formatarMoeda(removendo.valor)})? Ela deixa de ser cobrada neste mês e some do em aberto. Dá para restaurar depois, em "Cobranças removidas".` : ""}
        onConfirm={async () => { if (removendo) await ajustar(removendo, null, true, `${removendo.rotulo} de ${mesAno(removendo.competencia)} removida.`); }}
        onCancel={() => setRemovendo(null)}
      />
      <PagamentoDialog cobranca={pagando} onFechar={() => setPagando(null)} onConfirmar={registrar} />
      <InformarPagamentoDialog cobranca={informando} onFechar={() => setInformando(null)} onConfirmar={informar} />
      <LembreteDialog cobranca={lembrando} onFechar={() => setLembrando(null)} onConfirmar={agendarLembrete} />
      <RecusarDialog cobranca={recusando} onFechar={() => setRecusando(null)} onConfirmar={recusar} />
    </div>
  );
}

const mesISO = (iso: string) => iso.slice(0, 7);
const somarMeses = (mes: string, n: number) => {
  const d = new Date(Number(mes.slice(0, 4)), Number(mes.slice(5, 7)) - 1 + n, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
};
const mesesEntre = (de: string, ate: string) => (Number(ate.slice(0, 4)) - Number(de.slice(0, 4))) * 12 + Number(ate.slice(5, 7)) - Number(de.slice(5, 7));
const TIPOS_DE_CUSTO: [string, string][] = [["Notebook", "Notebook"], ["Celular", "Celular"], ["Equipamento", "Equipamento"], ["Outro", "Outro custo"]];

/**
 * Edição de uma cobrança pela gestão, num formulário só com um Salvar:
 * - em todas: o valor deste mês (com volta ao valor do contrato);
 * - coworking, tráfego pago e custo de lead: o início da cobrança (prazo em meses contado da entrada do consultor);
 * - custos lançados (notebook…): o tipo, o nome, a forma de cobrança (à vista ou parcelado), o valor total e a data de início (vencimento da 1ª parcela).
 */
function EditarCobrancaDialog({ cobranca, consultorId, custo, onFechar, onMudou }: {
  cobranca: Cobranca | null; consultorId: string; custo: CustoAdicional | null; onFechar: () => void; onMudou: () => void;
}) {
  const { notificar } = useToast();
  const [valor, setValor] = useState<number | null>(null);
  const [salvando, setSalvando] = useState(false);
  const [inicio, setInicio] = useState("");
  const [entrada, setEntrada] = useState<string | null>(null);   // mês em que o contrato começou a contar (início da cobrança − prazo)
  const [inicioOriginal, setInicioOriginal] = useState("");
  const [apagarCusto, setApagarCusto] = useState(false);
  // Custo lançado: os campos do custo (o nome, o tipo e a forma de cobrança).
  const [tipo, setTipo] = useState("Notebook");
  const [nome, setNome] = useState("");
  const [forma, setForma] = useState<"avista" | "parcelado">("parcelado");
  const [total, setTotal] = useState<number | null>(null);
  const [parcelas, setParcelas] = useState("1");
  const [primeira, setPrimeira] = useState("");
  const automatica = !!cobranca && cobranca.item !== "Adicional";

  useEffect(() => {
    if (!cobranca) return;
    setValor(cobranca.valor);
    setInicio("");
    setInicioOriginal("");
    setEntrada(null);
    if (cobranca.item === "Adicional") return;
    // Início da cobrança do item: vem do contrato (prazo em meses depois da entrada do consultor).
    api.get<{ atuais: { parametro: string; carenciaMeses?: number | null; inicioCobranca?: string | null }[] }>(`/portal/financeiro/contrato?consultor=${consultorId}&ano=${cobranca.competencia.slice(0, 4)}`)
      .then((d) => {
        const a = d.atuais.find((x) => x.parametro === cobranca.item);
        if (a?.inicioCobranca && a.carenciaMeses != null) {
          setInicio(mesISO(a.inicioCobranca));
          setInicioOriginal(mesISO(a.inicioCobranca));
          setEntrada(somarMeses(mesISO(a.inicioCobranca), -a.carenciaMeses));
        }
      })
      .catch(() => {});
  }, [cobranca, consultorId]);

  useEffect(() => {
    if (!custo) return;
    setTipo(custo.tipo);
    setNome(custo.descricao ?? "");
    setForma(custo.parcelas === 1 ? "avista" : "parcelado");
    setTotal(custo.valorTotal);
    setParcelas(String(custo.parcelas));
    setPrimeira(custo.primeiraParcela);
  }, [custo]);

  const n = forma === "avista" ? 1 : Number(parcelas);
  const parcelasValidas = Number.isInteger(n) && n >= 1 && n <= 120;
  const valorMudou = !!cobranca && valor !== null && valor !== cobranca.valor;
  const inicioMudou = automatica && !!entrada && !!inicio && inicio !== inicioOriginal;
  const custoMudou = !!custo && (tipo !== custo.tipo || nome.trim() !== (custo.descricao ?? "") || total !== custo.valorTotal || n !== custo.parcelas || primeira !== custo.primeiraParcela);
  const alteracoes = (valorMudou ? 1 : 0) + (inicioMudou ? 1 : 0) + (custoMudou ? 1 : 0);

  async function salvar() {
    if (!cobranca) return;
    if (custoMudou) {
      if (!total || total <= 0) return notificar("error", "Informe o valor total do custo.");
      if (!parcelasValidas) return notificar("error", "O número de parcelas deve ficar entre 1 e 120.");
      if (!primeira) return notificar("error", "Informe a data de início (vencimento da primeira parcela).");
      if (tipo === "Outro" && !nome.trim()) return notificar("error", "Dê um nome ao custo.");
    }
    let meses = 0;
    if (inicioMudou) {
      meses = mesesEntre(entrada!, inicio);
      if (meses < 0 || meses > 60) return notificar("error", "O início da cobrança deve ficar entre a entrada do consultor e 60 meses depois.");
    }
    setSalvando(true);
    try {
      // Primeiro o custo (pode mudar as parcelas), depois o valor do mês e o início da cobrança.
      if (custoMudou && custo) {
        await api.put(`/crm/setor-financeiro/${consultorId}/custos/${custo.id}`, { tipo, descricao: nome.trim() || null, valorTotal: total, parcelas: n, primeiraParcela: primeira, forcar: true });
      }
      if (valorMudou) await api.put(`/crm/setor-financeiro/${consultorId}/cobrancas/ajuste`, { item: cobranca.item, competencia: cobranca.competencia, custoId: cobranca.custoId ?? null, valor, remover: false });
      if (inicioMudou) await api.put(`/portal/financeiro/contrato/${consultorId}/prazos`, { parametro: cobranca.item, carenciaMeses: meses });
      notificar("success", "Cobrança atualizada.");
      onMudou();
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar.");
    } finally {
      setSalvando(false);
    }
  }

  async function voltarAoContrato() {
    if (!cobranca) return;
    try {
      await api.put(`/crm/setor-financeiro/${consultorId}/cobrancas/ajuste`, { item: cobranca.item, competencia: cobranca.competencia, custoId: cobranca.custoId ?? null, valor: null, remover: false });
      notificar("success", "Valor voltou ao do contrato.");
      onMudou();
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar.");
    }
  }

  return (
    <>
      <Modal
        open={!!cobranca}
        onClose={onFechar}
        title="Editar cobrança"
        size="md"
        footer={
          <>
            {alteracoes > 0 && <span className="mr-auto text-xs text-[var(--fg-muted)]">{alteracoes} alteração(ões) para salvar</span>}
            <Button variant="secondary" onClick={onFechar}>Cancelar</Button>
            <Button loading={salvando} disabled={alteracoes === 0} onClick={salvar}>Salvar alterações</Button>
          </>
        }
      >
        {cobranca && (
          <div className="filtros-painel space-y-4">
            <p className="text-sm text-[var(--fg-muted)]">
              <strong className="text-[var(--fg)]">{cobranca.rotulo}</strong> de {mesAno(cobranca.competencia)}
            </p>

            {custo && (
              <div className="space-y-3 rounded-xl bg-[var(--surface-hover)] p-3">
                <p className="text-sm font-semibold text-[var(--fg)]">Custo lançado</p>
                <div className="grid gap-3 sm:grid-cols-2">
                  <div>
                    <Label htmlFor="ec-tipo">Tipo do custo</Label>
                    <Select id="ec-tipo" value={tipo} onChange={(e) => setTipo(e.target.value)}>
                      {TIPOS_DE_CUSTO.map(([v, r]) => <option key={v} value={v}>{r}</option>)}
                    </Select>
                  </div>
                  <div>
                    <Label htmlFor="ec-nome" required={tipo === "Outro"}>Nome do custo</Label>
                    <Input id="ec-nome" maxLength={200} placeholder={tipo === "Notebook" ? "Ex.: Notebook Dell Inspiron" : "Opcional"} value={nome} onChange={(e) => setNome(e.target.value)} />
                  </div>
                </div>
                <div>
                  <p className="mb-1 text-sm font-medium text-[var(--fg)]">Forma de cobrança</p>
                  <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Forma de cobrança">
                    {([["avista", "À vista", "Uma cobrança só"], ["parcelado", "Parcelado", "Uma parcela por mês"]] as const).map(([v, t, d]) => (
                      <button
                        key={v}
                        type="button"
                        role="radio"
                        aria-checked={forma === v}
                        onClick={() => { setForma(v); if (v === "parcelado" && Number(parcelas) < 2) setParcelas("2"); }}
                        className={`focus-ring flex-1 cursor-pointer rounded-xl border px-3 py-2 text-left transition-colors ${forma === v ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-[var(--border)]"}`}
                      >
                        <span className="block text-sm font-bold text-[var(--fg)]">{t}</span>
                        <span className="block text-xs text-[var(--fg-muted)]">{d}</span>
                      </button>
                    ))}
                  </div>
                </div>
                <div className="grid gap-3 sm:grid-cols-3">
                  <div>
                    <Label htmlFor="ec-total" required>Valor total</Label>
                    <MoneyInput id="ec-total" value={total} onChange={setTotal} />
                  </div>
                  {forma === "parcelado" && (
                    <div>
                      <Label htmlFor="ec-parcelas" required>Parcelas</Label>
                      <Input id="ec-parcelas" type="number" min={2} max={120} step={1} value={parcelas} onChange={(e) => setParcelas(e.target.value)} />
                    </div>
                  )}
                  <div>
                    <Label htmlFor="ec-primeira" required>{forma === "avista" ? "Vencimento" : "Início (1ª parcela)"}</Label>
                    <DataInput id="ec-primeira" value={primeira} onChange={setPrimeira} />
                  </div>
                </div>
                {total && parcelasValidas && forma === "parcelado" && (
                  <p className="text-xs text-[var(--fg-muted)]">{n} parcela(s) de <strong className="text-[var(--fg)]">{formatarMoeda(Math.round((total / n) * 100) / 100)}</strong>; as demais vencem no mesmo dia dos meses seguintes.</p>
                )}
                {custo.parcelasPagas > 0 && <p className="text-xs text-[var(--fg-muted)]">{custo.parcelasPagas} parcela(s) já paga(s): o pagamento delas continua registrado. Mantenha o mês da 1ª parcela e um número de parcelas que as inclua.</p>}
                <div><Button size="sm" variant="ghost" onClick={() => setApagarCusto(true)}><Trash2 className="size-4" /> Apagar o custo inteiro</Button></div>
              </div>
            )}

            <div>
              <Label htmlFor="ec-valor" required>Valor {custo ? "desta parcela" : "desta cobrança"}</Label>
              <MoneyInput id="ec-valor" value={valor} onChange={setValor} />
              {cobranca.valorOriginal != null ? (
                <p className="mt-1 text-xs text-[var(--fg-muted)]">
                  {custo ? "O custo calcula" : "O contrato calculava"} {formatarMoeda(cobranca.valorOriginal)}.{" "}
                  <button type="button" onClick={voltarAoContrato} className="focus-ring cursor-pointer rounded font-semibold text-[var(--brand)] underline">Voltar ao valor calculado</button>
                </p>
              ) : (
                <p className="mt-1 text-xs text-[var(--fg-muted)]">Vale só para este mês; os outros meses continuam com o valor calculado.</p>
              )}
            </div>

            {automatica && (
              <div className="space-y-2 rounded-xl bg-[var(--surface-hover)] p-3">
                <Label htmlFor="ec-inicio">Início da cobrança de {cobranca.rotulo.toLowerCase()}</Label>
                {entrada ? (
                  <>
                    <div className="w-44"><MesInput id="ec-inicio" value={inicio} onChange={setInicio} /></div>
                    <p className="text-xs text-[var(--fg-muted)]">Muda o prazo de todos os meses deste item no contrato do consultor: {mesesEntre(entrada, inicio || entrada)} {mesesEntre(entrada, inicio || entrada) === 1 ? "mês" : "meses"} depois da entrada.</p>
                  </>
                ) : (
                  <p className="text-xs text-[var(--fg-muted)]">O início desta cobrança segue o padrão e é ajustado no contrato do consultor (aba Contrato e custos).</p>
                )}
              </div>
            )}
          </div>
        )}
      </Modal>
      <ConfirmDialog
        open={apagarCusto}
        danger
        title="Apagar o custo inteiro"
        confirmLabel="Apagar"
        message={custo ? `Apagar "${custo.descricao || custo.rotulo}" (${formatarMoeda(custo.valorTotal)} em ${custo.parcelas} parcelas)? Todas as parcelas e os pagamentos registrados dele saem.` : ""}
        onConfirm={async () => {
          if (!custo) return;
          try {
            await api.del(`/crm/setor-financeiro/${consultorId}/custos/${custo.id}`);
            setApagarCusto(false);
            notificar("success", "Custo apagado.");
            onMudou();
          } catch (e) {
            notificar("error", e instanceof Error ? e.message : "Não foi possível apagar.");
          }
        }}
        onCancel={() => setApagarCusto(false)}
      />
    </>
  );
}

function PagamentoDialog({ cobranca, onFechar, onConfirmar }: { cobranca: Cobranca | null; onFechar: () => void; onConfirmar: (c: Cobranca, pago: boolean, pagoEm: string | null, obs: string | null) => Promise<void> }) {
  const [data, setData] = useState(hojeISO());
  const [obs, setObs] = useState("");
  const [salvando, setSalvando] = useState(false);
  useEffect(() => {
    if (cobranca) {
      setData(cobranca.dataInformada ?? hojeISO());
      setObs("");
    }
  }, [cobranca]);

  return (
    <Modal
      open={!!cobranca}
      onClose={onFechar}
      title={cobranca?.informadoPeloConsultor ? "Confirmar pagamento" : "Registrar pagamento"}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onFechar}>Cancelar</Button>
          <Button
            loading={salvando}
            disabled={!data}
            onClick={async () => {
              if (!cobranca) return;
              setSalvando(true);
              await onConfirmar(cobranca, true, data, obs.trim() || null);
              setSalvando(false);
            }}
          >
            {cobranca?.informadoPeloConsultor ? "Confirmar: foi pago" : "Marcar como pago"}
          </Button>
        </>
      }
    >
      {cobranca && (
        <div className="space-y-3">
          {cobranca.informadoPeloConsultor && (
            <p className="rounded-lg bg-[var(--brand-soft)] px-3 py-2 text-xs text-[var(--fg)]">
              Confira o comprovante{cobranca.comprovanteUrl ? <> (<a href={cobranca.comprovanteUrl} target="_blank" rel="noopener noreferrer" className="font-medium text-[var(--brand)] underline">abrir</a>)</> : " (o consultor não anexou nenhum)"} e o recebimento antes de confirmar.
            </p>
          )}
          <p className="text-sm text-[var(--fg-muted)]">
            <strong className="text-[var(--fg)]">{cobranca.rotulo}</strong> de {mesAno(cobranca.competencia)} — <strong className="text-[var(--fg)]">{formatarMoeda(cobranca.valor)}</strong>
          </p>
          <div>
            <Label htmlFor="pg-data">Pago em</Label>
            <DataInput id="pg-data" value={data} max={hojeISO()} onChange={setData} />
          </div>
          <div>
            <Label htmlFor="pg-obs">Observação</Label>
            <Textarea id="pg-obs" rows={2} maxLength={300} placeholder="Opcional: forma de pagamento, comprovante…" value={obs} onChange={(e) => setObs(e.target.value)} />
          </div>
        </div>
      )}
    </Modal>
  );
}

/** O consultor avisa que pagou: data, observação e (opcional) o comprovante. Vai para o financeiro conferir. */
function InformarPagamentoDialog({ cobranca, onFechar, onConfirmar }: { cobranca: Cobranca | null; onFechar: () => void; onConfirmar: (c: Cobranca, data: string, obs: string | null, arquivo: File | null) => Promise<void> }) {
  const [data, setData] = useState(hojeISO());
  const [obs, setObs] = useState("");
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [salvando, setSalvando] = useState(false);
  useEffect(() => {
    if (cobranca) {
      setData(cobranca.dataInformada ?? hojeISO());
      setObs(cobranca.observacaoConsultor ?? "");
      setArquivo(null);
    }
  }, [cobranca]);
  const jaInformado = !!cobranca?.informadoPeloConsultor;

  return (
    <Modal
      open={!!cobranca}
      onClose={onFechar}
      title={jaInformado ? "Comprovante de pagamento" : "Já paguei"}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onFechar}>Cancelar</Button>
          <Button
            loading={salvando}
            disabled={!data}
            onClick={async () => {
              if (!cobranca) return;
              setSalvando(true);
              await onConfirmar(cobranca, data, obs.trim() || null, arquivo);
              setSalvando(false);
            }}
          >
            {jaInformado ? "Salvar" : "Enviar para o financeiro"}
          </Button>
        </>
      }
    >
      {cobranca && (
        <div className="space-y-3">
          <p className="text-sm text-[var(--fg-muted)]">
            <strong className="text-[var(--fg)]">{cobranca.rotulo}</strong> de {mesAno(cobranca.competencia)} — <strong className="text-[var(--fg)]">{formatarMoeda(cobranca.valor)}</strong>
          </p>
          <div>
            <Label htmlFor="inf-data" required>Quando você pagou</Label>
            <DataInput id="inf-data" value={data} max={hojeISO()} onChange={setData} />
          </div>
          <div>
            <Label htmlFor="inf-arq">Comprovante de pagamento{jaInformado ? "" : " (opcional)"}</Label>
            {cobranca?.comprovanteUrl && !arquivo && (
              <p className="mb-1 text-xs text-[var(--fg-muted)]">
                Já anexado:{" "}
                <a href={cobranca.comprovanteUrl} target="_blank" rel="noopener noreferrer" className="font-medium text-[var(--brand)] hover:underline">ver comprovante</a>. Escolha outro arquivo para trocar.
              </p>
            )}
            <label
              htmlFor="inf-arq"
              className="focus-within:ring-2 flex cursor-pointer items-center gap-2 rounded-lg border border-dashed border-[var(--border)] bg-[var(--field)] px-3 py-2.5 text-sm text-[var(--fg-muted)] transition-colors hover:border-[var(--brand)] hover:text-[var(--brand)]"
            >
              <Paperclip className="size-4 shrink-0" aria-hidden />
              <span className="min-w-0 truncate">{arquivo ? arquivo.name : "Anexar PDF, JPG, PNG ou WEBP (até 10 MB)"}</span>
            </label>
            <input id="inf-arq" type="file" accept=".pdf,.jpg,.jpeg,.png,.webp,application/pdf,image/*" className="sr-only" onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} />
          </div>
          <div>
            <Label htmlFor="inf-obs">Observação</Label>
            <Textarea id="inf-obs" rows={2} maxLength={300} placeholder="Opcional: forma de pagamento…" value={obs} onChange={(e) => setObs(e.target.value)} />
          </div>
          <p className="text-xs text-[var(--fg-muted)]">{jaInformado ? "O financeiro vê o comprovante ao conferir." : "O financeiro vai conferir e confirmar. Até lá, a cobrança fica \"aguardando conferência\"."}</p>
        </div>
      )}
    </Modal>
  );
}

/** "Pagar depois": o consultor escolhe o dia em que quer ser lembrado. */
function LembreteDialog({ cobranca, onFechar, onConfirmar }: { cobranca: Cobranca | null; onFechar: () => void; onConfirmar: (c: Cobranca, data: string | null) => Promise<void> }) {
  const [data, setData] = useState("");
  const [salvando, setSalvando] = useState(false);
  useEffect(() => {
    if (cobranca) setData(cobranca.lembreteEm ?? "");
  }, [cobranca]);

  return (
    <Modal
      open={!!cobranca}
      onClose={onFechar}
      title="Pagar depois"
      size="sm"
      footer={
        <>
          {cobranca?.lembreteEm && (
            <Button variant="ghost" disabled={salvando} onClick={async () => { if (cobranca) { setSalvando(true); await onConfirmar(cobranca, null); setSalvando(false); } }}>
              Remover lembrete
            </Button>
          )}
          <Button variant="secondary" onClick={onFechar}>Cancelar</Button>
          <Button
            loading={salvando}
            disabled={!data}
            onClick={async () => {
              if (!cobranca) return;
              setSalvando(true);
              await onConfirmar(cobranca, data);
              setSalvando(false);
            }}
          >
            Me lembrar neste dia
          </Button>
        </>
      }
    >
      {cobranca && (
        <div className="space-y-3">
          <p className="text-sm text-[var(--fg-muted)]">
            <strong className="text-[var(--fg)]">{cobranca.rotulo}</strong> de {mesAno(cobranca.competencia)} — <strong className="text-[var(--fg)]">{formatarMoeda(cobranca.valor)}</strong>
          </p>
          <div>
            <Label htmlFor="lem-data" required>Quando quer ser lembrado?</Label>
            <DataInput id="lem-data" value={data} min={hojeISO()} onChange={setData} />
          </div>
          <p className="text-xs text-[var(--fg-muted)]">No dia escolhido o sistema avisa você (no CRM e por notificação), se a cobrança ainda estiver em aberto.</p>
        </div>
      )}
    </Modal>
  );
}

/** O financeiro não confirmou o pagamento: devolve ao consultor com o motivo. */
function RecusarDialog({ cobranca, onFechar, onConfirmar }: { cobranca: Cobranca | null; onFechar: () => void; onConfirmar: (c: Cobranca, motivo: string) => Promise<void> }) {
  const [motivo, setMotivo] = useState("");
  const [salvando, setSalvando] = useState(false);
  useEffect(() => {
    if (cobranca) setMotivo("");
  }, [cobranca]);

  return (
    <Modal
      open={!!cobranca}
      onClose={onFechar}
      title="Não confirmar o pagamento"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onFechar}>Cancelar</Button>
          <Button
            variant="danger"
            loading={salvando}
            disabled={motivo.trim().length < 3}
            onClick={async () => {
              if (!cobranca) return;
              setSalvando(true);
              await onConfirmar(cobranca, motivo.trim());
              setSalvando(false);
            }}
          >
            Devolver ao consultor
          </Button>
        </>
      }
    >
      {cobranca && (
        <div className="space-y-3">
          <p className="text-sm text-[var(--fg-muted)]">
            <strong className="text-[var(--fg)]">{cobranca.rotulo}</strong> de {mesAno(cobranca.competencia)} — <strong className="text-[var(--fg)]">{formatarMoeda(cobranca.valor)}</strong>
          </p>
          <div>
            <Label htmlFor="rec-motivo" required>Motivo</Label>
            <Textarea id="rec-motivo" rows={3} maxLength={300} placeholder="Ex.: O pagamento não apareceu na conta; o comprovante é de outro valor." value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </div>
          <p className="text-xs text-[var(--fg-muted)]">A cobrança volta a ficar em aberto e o consultor recebe um aviso com o motivo.</p>
        </div>
      )}
    </Modal>
  );
}
