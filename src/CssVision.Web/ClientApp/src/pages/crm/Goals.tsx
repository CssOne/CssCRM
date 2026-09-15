import { Plus } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarMoeda, formatarPercentual } from "../../lib/format";
import type { RegionalGoal, SalesGoal, VendedorResumo } from "../../lib/types";
import { useAuth } from "../../context/AuthContext";
import { Badge, Button, Card, EmptyState, ErrorState, Input, Label, Modal, MoneyInput, Select, Skeleton, useToast } from "../../components/ui";

function mesAtualIso(): string {
  const hoje = new Date();
  return `${hoje.getFullYear()}-${String(hoje.getMonth() + 1).padStart(2, "0")}-01`;
}

/** Cartão de meta compartilhado entre consultores e regionais — só muda o rótulo e quem pode editar. */
function CartaoMeta({
  nome,
  temMeta,
  metaQuantidade,
  metaValor,
  realizadoQuantidade,
  realizadoValor,
  podeEditar,
  onEditar,
}: {
  nome: string;
  temMeta: boolean;
  metaQuantidade: number | null | undefined;
  metaValor: number | null | undefined;
  realizadoQuantidade: number;
  realizadoValor: number;
  podeEditar: boolean;
  onEditar: () => void;
}) {
  const metaQtd = metaQuantidade ?? 0;
  const percentual = metaQtd === 0 ? 0 : Math.min(100, (realizadoQuantidade / metaQtd) * 100);
  return (
    <Card className="p-4">
      <div className="mb-2 flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <p className="font-medium text-[var(--fg)]">{nome}</p>
          {!temMeta && <Badge variant="neutral">Sem meta definida</Badge>}
        </div>
        {temMeta && <span className="text-sm text-[var(--fg-muted)]">{formatarPercentual(Math.round(percentual))}</span>}
      </div>
      {temMeta ? (
        <>
          <div className="h-2.5 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
            <div className="h-full rounded-full bg-[var(--brand)]" style={{ width: `${percentual}%` }} />
          </div>
          <p className="mt-2 text-sm text-[var(--fg-muted)]">
            {realizadoQuantidade}/{metaQuantidade} vendas
            {metaValor != null && ` · ${formatarMoeda(realizadoValor)} de ${formatarMoeda(metaValor)}`}
          </p>
        </>
      ) : (
        <p className="text-sm text-[var(--fg-muted)]">{realizadoQuantidade} venda(s) no mês, sem meta cadastrada para comparar.</p>
      )}
      {podeEditar && (
        <div className="mt-3 flex justify-end">
          <Button size="sm" variant="secondary" onClick={onEditar}>
            {temMeta ? "Editar meta" : "Definir meta"}
          </Button>
        </div>
      )}
    </Card>
  );
}

export function GoalsPage() {
  const { temPapel } = useAuth();
  const podeGerir = temPapel("Admin", "GestorMaster", "GestorComercial");
  const ehAdministrador = temPapel("Admin", "GestorMaster");
  const { notificar } = useToast();

  const [mesReferencia, setMesReferencia] = useState(mesAtualIso());
  const [metas, setMetas] = useState<SalesGoal[] | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [modalAberto, setModalAberto] = useState(false);
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);
  const [vendedorId, setVendedorId] = useState("");
  const [vendedorPreset, setVendedorPreset] = useState<string | null>(null);
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
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
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
        setVendedorId(vendedorPreset ?? lista[0]?.id ?? "");
      });
    }
  }, [modalAberto, podeGerir, vendedorPreset]);

  function abrirModalNovaMeta() {
    setVendedorPreset(null);
    setMetaValor("");
    setMetaQuantidade("");
    setModalAberto(true);
  }

  function abrirModalParaMeta(meta: SalesGoal) {
    setVendedorPreset(meta.vendedorId);
    setMetaQuantidade(meta.metaQuantidadeVendas != null ? String(meta.metaQuantidadeVendas) : "");
    setMetaValor(meta.metaValor != null ? String(meta.metaValor) : "");
    setModalAberto(true);
  }

  async function salvarMeta() {
    if (!vendedorId || !metaQuantidade) return;
    setSalvando(true);
    try {
      await api.put("/crm/goals", {
        vendedorId,
        mesReferencia,
        metaQuantidadeVendas: Number(metaQuantidade),
        metaValor: metaValor ? Number(metaValor) : null,
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

  // --- Metas gerais por regional (administrador) ---

  const [metasRegionais, setMetasRegionais] = useState<RegionalGoal[] | null>(null);
  const [carregandoRegionais, setCarregandoRegionais] = useState(true);
  const [erroRegionais, setErroRegionais] = useState<string | null>(null);

  const [modalRegionalAberto, setModalRegionalAberto] = useState(false);
  const [regionalNomePreset, setRegionalNomePreset] = useState("");
  const [regionalIdEditando, setRegionalIdEditando] = useState("");
  const [metaRegionalValor, setMetaRegionalValor] = useState("");
  const [metaRegionalQuantidade, setMetaRegionalQuantidade] = useState("");
  const [salvandoRegional, setSalvandoRegional] = useState(false);

  const carregarRegionais = useCallback(
    (signal?: AbortSignal) => {
      if (!ehAdministrador) return;
      setCarregandoRegionais(true);
      setErroRegionais(null);
      api
        .get<RegionalGoal[]>(`/crm/goals/regionais${toQueryString({ mesReferencia })}`, signal)
        .then(setMetasRegionais)
        .catch((e) => { if (!isAbortError(e)) setErroRegionais(e instanceof Error ? e.message : "Não foi possível carregar as metas por regional."); })
        .finally(() => { if (!signal?.aborted) setCarregandoRegionais(false); });
    },
    [mesReferencia, ehAdministrador]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregarRegionais(controller.signal);
    return () => controller.abort();
  }, [carregarRegionais, recarregar]);

  function abrirModalParaRegional(meta: RegionalGoal) {
    setRegionalIdEditando(meta.regionalId);
    setRegionalNomePreset(meta.regionalNome);
    setMetaRegionalQuantidade(meta.metaQuantidadeVendas != null ? String(meta.metaQuantidadeVendas) : "");
    setMetaRegionalValor(meta.metaValor != null ? String(meta.metaValor) : "");
    setModalRegionalAberto(true);
  }

  async function salvarMetaRegional() {
    if (!regionalIdEditando || !metaRegionalQuantidade) return;
    setSalvandoRegional(true);
    try {
      await api.put("/crm/goals/regionais", {
        regionalId: regionalIdEditando,
        mesReferencia,
        metaQuantidadeVendas: Number(metaRegionalQuantidade),
        metaValor: metaRegionalValor ? Number(metaRegionalValor) : null,
      });
      notificar("success", "Meta geral da regional definida com sucesso.");
      setModalRegionalAberto(false);
      setMetaRegionalValor("");
      setMetaRegionalQuantidade("");
      setRecarregar((n) => n + 1);
    } catch {
      notificar("error", "Não foi possível salvar a meta da regional.");
    } finally {
      setSalvandoRegional(false);
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
            <Button onClick={abrirModalNovaMeta}>
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
        <EmptyState title="Nenhum consultor encontrado" description="Cadastre consultores para acompanhar a meta de vendas de cada um." />
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {metas.map((meta) => (
            <CartaoMeta
              key={meta.vendedorId}
              nome={meta.vendedorNome}
              temMeta={meta.id != null}
              metaQuantidade={meta.metaQuantidadeVendas}
              metaValor={meta.metaValor}
              realizadoQuantidade={meta.realizadoQuantidade}
              realizadoValor={meta.realizadoValor}
              podeEditar={podeGerir}
              onEditar={() => abrirModalParaMeta(meta)}
            />
          ))}
        </div>
      )}

      {ehAdministrador && (
        <div className="space-y-3 pt-2">
          <div>
            <h2 className="text-lg font-semibold text-[var(--fg)]">Metas gerais por regional</h2>
            <p className="text-sm text-[var(--fg-muted)]">
              Meta de quantidade de vendas para toda a regional, somada às metas individuais dos consultores no cálculo da meta do mês.
            </p>
          </div>

          {carregandoRegionais ? (
            <div className="space-y-2">
              {Array.from({ length: 2 }).map((_, i) => (
                <Skeleton key={i} className="h-20" />
              ))}
            </div>
          ) : erroRegionais ? (
            <ErrorState message={erroRegionais} onRetry={() => setRecarregar((n) => n + 1)} />
          ) : !metasRegionais || metasRegionais.length === 0 ? (
            <EmptyState title="Nenhuma regional encontrada" description="Cadastre regionais para definir uma meta geral por mês." />
          ) : (
            <div className="grid gap-3 lg:grid-cols-2">
              {metasRegionais.map((meta) => (
                <CartaoMeta
                  key={meta.regionalId}
                  nome={meta.regionalNome}
                  temMeta={meta.id != null}
                  metaQuantidade={meta.metaQuantidadeVendas}
                  metaValor={meta.metaValor}
                  realizadoQuantidade={meta.realizadoQuantidade}
                  realizadoValor={meta.realizadoValor}
                  podeEditar
                  onEditar={() => abrirModalParaRegional(meta)}
                />
              ))}
            </div>
          )}
        </div>
      )}

      <Modal open={modalRegionalAberto} onClose={() => setModalRegionalAberto(false)} title="Definir meta geral da regional">
        <div className="space-y-4">
          <div>
            <Label>Regional</Label>
            <p className="text-sm font-medium text-[var(--fg)]">{regionalNomePreset}</p>
          </div>
          <div>
            <Label htmlFor="meta-regional-qtd" required>
              Meta de quantidade de vendas
            </Label>
            <Input id="meta-regional-qtd" type="number" min={0} value={metaRegionalQuantidade} onChange={(e) => setMetaRegionalQuantidade(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="meta-regional-valor">Meta de valor em R$ (opcional)</Label>
            <MoneyInput
              id="meta-regional-valor"
              value={metaRegionalValor ? Number(metaRegionalValor) : null}
              onChange={(v) => setMetaRegionalValor(v != null ? String(v) : "")}
            />
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setModalRegionalAberto(false)} disabled={salvandoRegional}>
              Cancelar
            </Button>
            <Button onClick={salvarMetaRegional} loading={salvandoRegional} disabled={!metaRegionalQuantidade}>
              Salvar
            </Button>
          </div>
        </div>
      </Modal>

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
            <Label htmlFor="meta-qtd" required>
              Meta de quantidade de vendas
            </Label>
            <Input id="meta-qtd" type="number" min={0} value={metaQuantidade} onChange={(e) => setMetaQuantidade(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="meta-valor">Meta de valor em R$ (opcional)</Label>
            <MoneyInput id="meta-valor" value={metaValor ? Number(metaValor) : null} onChange={(v) => setMetaValor(v != null ? String(v) : "")} />
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setModalAberto(false)} disabled={salvando}>
              Cancelar
            </Button>
            <Button onClick={salvarMeta} loading={salvando} disabled={!vendedorId || !metaQuantidade}>
              Salvar
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
