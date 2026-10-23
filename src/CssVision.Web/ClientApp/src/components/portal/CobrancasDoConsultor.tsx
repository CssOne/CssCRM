import { AlertTriangle, BellRing, CalendarClock, CheckCircle2, CircleDollarSign, FileCheck2, Laptop, Paperclip, Target, Undo2, XCircle } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, isAbortError, uploadFile } from "../../lib/api";
import { formatarData, formatarMoeda } from "../../lib/format";
import { Badge, Button, Card, EmptyState, ErrorState, Label, Modal, Select, Skeleton, Textarea, useToast } from "../ui";
import { StatCard } from "../crm/StatCard";
import { DataInput } from "../PeriodoInput";

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
                  <td className="px-4 py-2.5 text-right font-semibold">{formatarMoeda(c.valor)}</td>
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

      <PagamentoDialog cobranca={pagando} onFechar={() => setPagando(null)} onConfirmar={registrar} />
      <InformarPagamentoDialog cobranca={informando} onFechar={() => setInformando(null)} onConfirmar={informar} />
      <LembreteDialog cobranca={lembrando} onFechar={() => setLembrando(null)} onConfirmar={agendarLembrete} />
      <RecusarDialog cobranca={recusando} onFechar={() => setRecusando(null)} onConfirmar={recusar} />
    </div>
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
