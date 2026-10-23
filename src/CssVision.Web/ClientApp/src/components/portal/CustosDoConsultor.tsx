import { Laptop, Pencil, Plus, Trash2 } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiRequestError, api, isAbortError } from "../../lib/api";
import { formatarMoeda } from "../../lib/format";
import { Badge, Button, Card, ConfirmDialog, ErrorState, Input, Label, Modal, MoneyInput, Select, Skeleton, useToast } from "../ui";
import type { CustoAdicional, SetorFinanceiroConsultor } from "./CobrancasDoConsultor";
import { DataInput } from "../PeriodoInput";

const TIPOS: [string, string][] = [["Notebook", "Notebook"], ["Celular", "Celular"], ["Equipamento", "Equipamento"], ["Outro", "Outro custo"]];
const dataBr = (iso: string) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}`;
const hojeIso = () => new Date().toISOString().slice(0, 10);

/**
 * Custos que o financeiro lança para o consultor e que ele paga em parcelas mensais (notebook, celular...). Cada parcela vira uma cobrança no Setor
 * Financeiro (paga ou em aberto) e um custo no Controle financeiro dele. Só o financeiro e a gestão lançam; o consultor só vê.
 */
export function CustosDoConsultor({ consultorId, onMudou }: { consultorId: string; onMudou?: () => void }) {
  const { notificar } = useToast();
  const [custos, setCustos] = useState<CustoAdicional[] | null>(null);
  const [podeEditar, setPodeEditar] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [novo, setNovo] = useState(false);
  const [editando, setEditando] = useState<CustoAdicional | null>(null);
  const [remover, setRemover] = useState<CustoAdicional | null>(null);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setErro(null);
      api
        .get<SetorFinanceiroConsultor>(`/crm/setor-financeiro/cobrancas?consultorId=${consultorId}`, signal)
        .then((d) => {
          setCustos(d.custos ?? []);
          setPodeEditar(d.podeEditar);
        })
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os custos.");
        });
    },
    [consultorId]
  );

  useEffect(() => {
    setCustos(null);
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  const aposSalvar = () => {
    carregar();
    onMudou?.();
  };

  if (erro) return <ErrorState message={erro} onRetry={() => carregar()} />;
  if (!custos) return <Skeleton className="h-32 rounded-2xl" />;

  return (
    <Card className="space-y-3 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand)]"><Laptop className="size-5" aria-hidden /></span>
          <div>
            <p className="text-base font-semibold text-[var(--fg)]">Custos do consultor</p>
            <p className="text-xs text-[var(--fg-muted)]">Notebook, celular e outros custos que o consultor paga em parcelas. Cada parcela vira uma cobrança, acompanhada na aba Cobranças.</p>
          </div>
        </div>
        {podeEditar && (
          <Button onClick={() => setNovo(true)}>
            <Plus className="size-4" /> Adicionar custo
          </Button>
        )}
      </div>

      {custos.length === 0 ? (
        <p className="rounded-xl bg-[var(--surface-hover)] px-4 py-5 text-center text-sm text-[var(--fg-muted)]">
          Nenhum custo lançado.{podeEditar ? " Use \"Adicionar custo\" para lançar um notebook, por exemplo." : ""}
        </p>
      ) : (
        <ul className="space-y-2">
          {custos.map((c) => (
            <li key={c.id} className="rounded-xl border border-[var(--border)] p-3">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="font-semibold text-[var(--fg)]">
                    {c.descricao || c.rotulo} {c.descricao && <Badge variant="neutral">{c.rotulo}</Badge>}
                  </p>
                  <p className="text-xs text-[var(--fg-muted)]">
                    {formatarMoeda(c.valorTotal)} em {c.parcelas} parcela(s) de {formatarMoeda(c.valorParcela)} · 1ª vence em {dataBr(c.primeiraParcela)}
                    {c.criadoPor ? ` · lançado por ${c.criadoPor}` : ""}
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <Badge variant={c.parcelasPagas >= c.parcelas ? "success" : "brand"}>{c.parcelasPagas}/{c.parcelas} pagas</Badge>
                  {podeEditar && (
                    <button type="button" aria-label="Editar custo" title="Editar custo" onClick={() => setEditando(c)} className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--brand)]">
                      <Pencil className="size-4" />
                    </button>
                  )}
                  {podeEditar && (
                    <button type="button" aria-label="Remover custo" title="Remover custo" onClick={() => setRemover(c)} className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--danger)]">
                      <Trash2 className="size-4" />
                    </button>
                  )}
                </div>
              </div>
              <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-[var(--surface-hover)]" role="progressbar" aria-valuemin={0} aria-valuemax={c.parcelas} aria-valuenow={c.parcelasPagas} aria-label="Parcelas pagas">
                <div className="h-full rounded-full bg-[var(--success)] transition-[width] duration-700" style={{ width: `${(c.parcelasPagas / c.parcelas) * 100}%` }} />
              </div>
            </li>
          ))}
        </ul>
      )}

      <NovoCustoDialog open={novo || !!editando} custo={editando} consultorId={consultorId} onFechar={() => { setNovo(false); setEditando(null); }} onCriado={() => { notificar("success", editando ? "Custo atualizado." : "Custo lançado: as parcelas já aparecem nas cobranças."); setNovo(false); setEditando(null); aposSalvar(); }} />

      <ConfirmDialog
        open={!!remover}
        danger
        title="Remover custo"
        confirmLabel="Remover"
        message={remover ? `Remover "${remover.descricao || remover.rotulo}" (${formatarMoeda(remover.valorTotal)} em ${remover.parcelas} parcelas)? As parcelas e os pagamentos registrados dele também saem.` : ""}
        onConfirm={async () => {
          if (!remover) return;
          try {
            await api.del(`/crm/setor-financeiro/${consultorId}/custos/${remover.id}`);
            setRemover(null);
            aposSalvar();
          } catch (e) {
            notificar("error", e instanceof Error ? e.message : "Não foi possível remover.");
          }
        }}
        onCancel={() => setRemover(null)}
      />
    </Card>
  );
}

/** Lança um custo novo ou, com <c>custo</c>, edita um já lançado (as parcelas já pagas continuam registradas). */
function NovoCustoDialog({ open, custo, consultorId, onFechar, onCriado }: { open: boolean; custo: CustoAdicional | null; consultorId: string; onFechar: () => void; onCriado: () => void }) {
  const { notificar } = useToast();
  const [tipo, setTipo] = useState("Notebook");
  const [descricao, setDescricao] = useState("");
  const [valor, setValor] = useState<number | null>(null);
  const [parcelas, setParcelas] = useState("1");
  const [primeira, setPrimeira] = useState(hojeIso());
  const [salvando, setSalvando] = useState(false);
  const [duplicado, setDuplicado] = useState(false);

  useEffect(() => {
    if (!open) return;
    setTipo(custo?.tipo ?? "Notebook");
    setDescricao(custo?.descricao ?? "");
    setValor(custo?.valorTotal ?? null);
    setParcelas(String(custo?.parcelas ?? 1));
    setPrimeira(custo?.primeiraParcela ?? hojeIso());
  }, [open, custo]);

  const n = Number(parcelas);
  const parcelasValidas = Number.isInteger(n) && n >= 1 && n <= 120;
  const porParcela = useMemo(() => (valor && parcelasValidas ? Math.round((valor / n) * 100) / 100 : null), [valor, n, parcelasValidas]);

  async function salvar(forcar = false) {
    if (!valor || valor <= 0) return notificar("error", "Informe o valor do custo.");
    if (!parcelasValidas) return notificar("error", "O número de parcelas deve ficar entre 1 e 120.");
    if (!primeira) return notificar("error", "Informe a data de vencimento da primeira parcela.");
    if (tipo === "Outro" && !descricao.trim()) return notificar("error", "Descreva o custo.");
    setSalvando(true);
    try {
      const corpo = { tipo, descricao: descricao.trim() || null, valorTotal: valor, parcelas: n, primeiraParcela: primeira, forcar };
      if (custo) await api.put(`/crm/setor-financeiro/${consultorId}/custos/${custo.id}`, corpo);
      else await api.post(`/crm/setor-financeiro/${consultorId}/custos`, corpo);
      onCriado();
    } catch (e) {
      // Custo idêntico a outro já lançado: pergunta se são mesmo dois antes de lançar de novo.
      if (e instanceof ApiRequestError && e.codigo === "custo_duplicado") setDuplicado(true);
      else notificar("error", e instanceof Error ? e.message : "Não foi possível lançar o custo.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onFechar}
      title={custo ? "Editar custo do consultor" : "Adicionar custo para o consultor"}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onFechar}>Cancelar</Button>
          <Button loading={salvando} onClick={() => salvar()}>{custo ? "Salvar alterações" : "Lançar custo"}</Button>
        </>
      }
    >
      <div className="filtros-painel space-y-3">
        <div>
          <Label htmlFor="cc-tipo">Tipo do custo</Label>
          <Select id="cc-tipo" value={tipo} onChange={(e) => setTipo(e.target.value)}>
            {TIPOS.map(([v, r]) => <option key={v} value={v}>{r}</option>)}
          </Select>
        </div>
        <div>
          <Label htmlFor="cc-desc" required={tipo === "Outro"}>Descrição</Label>
          <Input id="cc-desc" maxLength={200} placeholder={tipo === "Notebook" ? "Ex.: Notebook Dell Inspiron" : "Opcional"} value={descricao} onChange={(e) => setDescricao(e.target.value)} />
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <div>
            <Label htmlFor="cc-valor" required>Valor total</Label>
            <MoneyInput id="cc-valor" value={valor} onChange={setValor} />
          </div>
          <div>
            <Label htmlFor="cc-parc" required>Número de parcelas</Label>
            <Input id="cc-parc" type="number" min={1} max={120} step={1} value={parcelas} onChange={(e) => setParcelas(e.target.value)} />
          </div>
        </div>
        <div>
          <Label htmlFor="cc-primeira" required>Vencimento da primeira parcela</Label>
          <DataInput id="cc-primeira" value={primeira} onChange={setPrimeira} />
          <p className="mt-1 text-xs text-[var(--fg-muted)]">As demais vencem no mesmo dia dos meses seguintes. O consultor é avisado 3 dias antes e no dia do vencimento.</p>
        </div>
        {custo && custo.parcelasPagas > 0 && (
          <p className="rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-xs text-[var(--fg-muted)]">
            {custo.parcelasPagas} parcela(s) já paga(s): o pagamento delas continua registrado. Se mudar o vencimento da 1ª parcela, mantenha o mesmo mês.
          </p>
        )}
        {porParcela !== null && (
          <p className="rounded-lg bg-[var(--brand-soft)] px-3 py-2 text-sm text-[var(--fg)]">
            {n} parcela(s) de <strong>{formatarMoeda(porParcela)}</strong>
            {n > 1 ? " (a diferença de arredondamento fica na última)" : ""}
          </p>
        )}
      </div>
      <ConfirmDialog
        open={duplicado}
        title="Já existe um custo igual"
        confirmLabel="Lançar mesmo assim"
        message="Este consultor já tem um custo com o mesmo tipo, valor, parcelas e vencimento. Se foi um clique duplo, cancele. Se são dois custos de verdade (ex.: dois notebooks), lance mesmo assim."
        onConfirm={async () => { setDuplicado(false); await salvar(true); }}
        onCancel={() => setDuplicado(false)}
      />
    </Modal>
  );
}
