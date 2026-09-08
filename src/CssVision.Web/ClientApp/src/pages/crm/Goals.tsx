import { Plus } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarMoeda, formatarPercentual } from "../../lib/format";
import type { SalesGoal, VendedorResumo } from "../../lib/types";
import { useAuth } from "../../context/AuthContext";
import { Button, Card, EmptyState, ErrorState, Input, Label, Modal, Select, Skeleton, useToast } from "../../components/ui";

function mesAtualIso(): string {
  const hoje = new Date();
  return `${hoje.getFullYear()}-${String(hoje.getMonth() + 1).padStart(2, "0")}-01`;
}

export function GoalsPage() {
  const { temPapel } = useAuth();
  const podeGerir = temPapel("Admin", "GestorMaster", "GestorComercial");
  const { notificar } = useToast();

  const [mesReferencia, setMesReferencia] = useState(mesAtualIso());
  const [metas, setMetas] = useState<SalesGoal[] | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [modalAberto, setModalAberto] = useState(false);
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);
  const [vendedorId, setVendedorId] = useState("");
  const [metaValor, setMetaValor] = useState("");
  const [metaQuantidade, setMetaQuantidade] = useState("");
  const [salvando, setSalvando] = useState(false);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<SalesGoal[]>(`/crm/goals${toQueryString({ mesReferencia })}`, signal)
        .then(setMetas)
        .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar as metas."); })
        .finally(() => setCarregando(false));
    },
    [mesReferencia]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  useEffect(() => {
    if (modalAberto && podeGerir) {
      api.get<VendedorResumo[]>("/crm/management/vendedores").then((lista) => {
        setVendedores(lista);
        setVendedorId(lista[0]?.id ?? "");
      });
    }
  }, [modalAberto, podeGerir]);

  async function salvarMeta() {
    if (!vendedorId || !metaValor) return;
    setSalvando(true);
    try {
      await api.put("/crm/goals", {
        vendedorId,
        mesReferencia,
        metaValor: Number(metaValor),
        metaQuantidadeVendas: metaQuantidade ? Number(metaQuantidade) : null,
      });
      notificar("success", "Meta definida com sucesso.");
      setModalAberto(false);
      setMetaValor("");
      setMetaQuantidade("");
      setRecarregar((n) => n + 1);
    } catch {
      notificar("error", "Não foi possível salvar a meta.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Metas comerciais</h1>
          <p className="text-sm text-[var(--fg-muted)]">Acompanhamento de meta versus realizado por vendedor.</p>
        </div>
        <div className="flex items-center gap-2">
          <Input type="month" value={mesReferencia.slice(0, 7)} onChange={(e) => setMesReferencia(`${e.target.value}-01`)} />
          {podeGerir && (
            <Button onClick={() => setModalAberto(true)}>
              <Plus className="size-4" /> Definir meta
            </Button>
          )}
        </div>
      </div>

      {carregando ? (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-20" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !metas || metas.length === 0 ? (
        <EmptyState title="Nenhuma meta definida para este mês" description="Defina uma meta comercial para acompanhar o desempenho." />
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {metas.map((meta) => {
            const percentual = meta.metaValor === 0 ? 0 : Math.min(100, (meta.realizadoValor / meta.metaValor) * 100);
            return (
              <Card key={meta.id} className="p-4">
                <div className="mb-2 flex items-center justify-between">
                  <p className="font-medium text-[var(--fg)]">{meta.vendedorNome}</p>
                  <span className="text-sm text-[var(--fg-muted)]">{formatarPercentual(Math.round(percentual))}</span>
                </div>
                <div className="h-2.5 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
                  <div className="h-full rounded-full bg-[var(--brand)]" style={{ width: `${percentual}%` }} />
                </div>
                <p className="mt-2 text-sm text-[var(--fg-muted)]">
                  {formatarMoeda(meta.realizadoValor)} de {formatarMoeda(meta.metaValor)}
                  {meta.metaQuantidadeVendas != null && ` · ${meta.realizadoQuantidade}/${meta.metaQuantidadeVendas} vendas`}
                </p>
              </Card>
            );
          })}
        </div>
      )}

      <Modal open={modalAberto} onClose={() => setModalAberto(false)} title="Definir meta comercial">
        <div className="space-y-4">
          <div>
            <Label htmlFor="meta-vendedor" required>
              Vendedor
            </Label>
            <Select id="meta-vendedor" value={vendedorId} onChange={(e) => setVendedorId(e.target.value)}>
              {vendedores.map((v) => (
                <option key={v.id} value={v.id}>
                  {v.nome}
                </option>
              ))}
            </Select>
          </div>
          <div>
            <Label htmlFor="meta-valor" required>
              Meta de valor (R$)
            </Label>
            <Input id="meta-valor" type="number" min={0} step="0.01" value={metaValor} onChange={(e) => setMetaValor(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="meta-qtd">Meta de quantidade de vendas (opcional)</Label>
            <Input id="meta-qtd" type="number" min={0} value={metaQuantidade} onChange={(e) => setMetaQuantidade(e.target.value)} />
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setModalAberto(false)} disabled={salvando}>
              Cancelar
            </Button>
            <Button onClick={salvarMeta} loading={salvando} disabled={!vendedorId || !metaValor}>
              Salvar
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
