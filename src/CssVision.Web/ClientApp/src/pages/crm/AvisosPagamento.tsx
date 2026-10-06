import { BellRing, Check, Send, Wallet } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarDataHora, formatarMoeda } from "../../lib/format";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import type { AvisoPagamento, VendedorResumo } from "../../lib/types";
import { Badge, Button, Card, Checkbox, EmptyState, ErrorState, Input, Label, MoneyInput, Select, Skeleton, Textarea, useToast } from "../../components/ui";

/**
 * Avisos de pagamento em aberto: gestão e financeiro escolhem os consultores que enxergam (Financeiro e Gestor regional, só os da regional
 * deles), escrevem o aviso e enviam. O consultor recebe notificação (push e dentro do CRM) e vê no card "Avisos importantes" do Portal até
 * alguém marcar como resolvido aqui.
 */
export function AvisosPagamentoPage() {
  const { notificar } = useToast();
  // ?consultor=<id>: vem do atalho "Avisar" da gestão comercial — já deixa o consultor selecionado e filtra a lista.
  const [params] = useSearchParams();
  const consultorDaUrl = params.get("consultor");
  const [consultores, setConsultores] = useState<VendedorResumo[] | null>(null);
  const [avisos, setAvisos] = useState<AvisoPagamento[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [filtro, setFiltro] = useState<"abertos" | "resolvidos" | "todos">("abertos");

  const [selecionados, setSelecionados] = useState<Set<string>>(() => new Set(consultorDaUrl ? [consultorDaUrl] : []));
  const [filtroConsultor, setFiltroConsultor] = useState(consultorDaUrl ?? "");
  const [busca, setBusca] = useState("");
  const [mensagem, setMensagem] = useState("");
  const [valor, setValor] = useState<number | null>(null);
  const [referencia, setReferencia] = useState("");
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      api.get<VendedorResumo[]>("/crm/management/vendedores", controller.signal),
      api.get<AvisoPagamento[]>("/crm/avisos-pagamento", controller.signal),
    ])
      .then(([v, a]) => {
        setConsultores(v);
        setAvisos(a);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os avisos.");
      });
    return () => controller.abort();
  }, [recarregar]);
  useAtualizarAoVivo(useCallback(() => setRecarregar((n) => n + 1), []));

  const visiveis = useMemo(
    () => (consultores ?? []).filter((c) => c.ativo !== false && c.nome.toLowerCase().includes(busca.trim().toLowerCase())),
    [consultores, busca]
  );

  function alternar(id: string) {
    setSelecionados((atual) => {
      const novo = new Set(atual);
      if (novo.has(id)) novo.delete(id);
      else novo.add(id);
      return novo;
    });
  }

  async function enviar() {
    setEnviando(true);
    try {
      const enviados = await api.post<AvisoPagamento[]>("/crm/avisos-pagamento", {
        consultorIds: [...selecionados],
        mensagem: mensagem.trim(),
        valor,
        referencia: referencia.trim() || null,
      });
      notificar("success", enviados.length === 1 ? `Aviso enviado para ${enviados[0].consultorNome}.` : `Aviso enviado para ${enviados.length} consultores.`);
      setSelecionados(new Set());
      setMensagem("");
      setValor(null);
      setReferencia("");
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível enviar o aviso.");
    } finally {
      setEnviando(false);
    }
  }

  async function resolver(id: string) {
    try {
      await api.put(`/crm/avisos-pagamento/${id}/resolver`, {});
      notificar("success", "Aviso marcado como resolvido.");
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível resolver o aviso.");
    }
  }

  async function reenviar(id: string) {
    try {
      await api.put(`/crm/avisos-pagamento/${id}/reenviar`, {});
      notificar("success", "Notificação enviada de novo ao consultor.");
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível reenviar o aviso.");
    }
  }

  const lista = (avisos ?? [])
    .filter((a) => filtro === "todos" || (filtro === "abertos" ? a.status === "Aberto" : a.status === "Resolvido"))
    .filter((a) => !filtroConsultor || a.consultorId === filtroConsultor);

  return (
    <div className="space-y-4">
      <div>
        <h1 className="flex items-center gap-2 text-xl font-bold text-[var(--fg)]">
          <Wallet className="size-5 text-[var(--brand)]" aria-hidden /> Avisos de pagamento
        </h1>
        <p className="text-sm text-[var(--fg-muted)]">
          Avise os consultores de pagamentos em aberto. Eles recebem uma notificação e o aviso fica no card "Avisos importantes" até você resolver.
        </p>
      </div>

      {erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !consultores || !avisos ? (
        <div className="space-y-2">
          <Skeleton className="h-40" />
          <Skeleton className="h-40" />
        </div>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
          <Card className="space-y-3 p-4">
            <h2 className="text-sm font-semibold text-[var(--fg)]">Novo aviso</h2>
            <div>
              <Label htmlFor="aviso-busca">Consultores ({selecionados.size} selecionado(s))</Label>
              <Input id="aviso-busca" placeholder="Buscar consultor…" value={busca} onChange={(e) => setBusca(e.target.value)} />
              <div className="mt-2 max-h-48 space-y-1 overflow-y-auto rounded-lg border border-[var(--border)] p-2">
                {visiveis.length === 0 ? (
                  <p className="text-xs text-[var(--fg-muted)]">Nenhum consultor encontrado.</p>
                ) : (
                  visiveis.map((c) => <Checkbox key={c.id} label={c.nome} checked={selecionados.has(c.id)} onChange={() => alternar(c.id)} />)
                )}
              </div>
              {visiveis.length > 0 && (
                <button
                  type="button"
                  className="mt-1 cursor-pointer text-xs text-[var(--brand)] hover:underline"
                  onClick={() => setSelecionados(new Set(selecionados.size === visiveis.length ? [] : visiveis.map((c) => c.id)))}
                >
                  {selecionados.size === visiveis.length ? "Limpar seleção" : "Selecionar todos da lista"}
                </button>
              )}
            </div>
            <div>
              <Label htmlFor="aviso-mensagem" required>Mensagem</Label>
              <Textarea
                id="aviso-mensagem"
                rows={4}
                maxLength={1000}
                value={mensagem}
                onChange={(e) => setMensagem(e.target.value)}
                placeholder="Ex.: Há 2 mensalidades em aberto do cliente João. Entre em contato para regularizar."
              />
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <div>
                <Label htmlFor="aviso-valor">Valor em aberto (opcional)</Label>
                <MoneyInput id="aviso-valor" value={valor} onChange={setValor} />
              </div>
              <div>
                <Label htmlFor="aviso-ref">Cliente / placa (opcional)</Label>
                <Input id="aviso-ref" maxLength={200} value={referencia} onChange={(e) => setReferencia(e.target.value)} />
              </div>
            </div>
            <div className="flex justify-end">
              <Button onClick={enviar} loading={enviando} disabled={selecionados.size === 0 || mensagem.trim().length < 3}>
                <Send className="size-4" /> Enviar aviso
              </Button>
            </div>
          </Card>

          <Card className="space-y-3 p-4">
            <div className="flex items-center justify-between gap-2">
              <h2 className="text-sm font-semibold text-[var(--fg)]">Avisos enviados</h2>
              <div className="flex flex-wrap gap-2">
                <Select aria-label="Filtrar por consultor" value={filtroConsultor} onChange={(e) => setFiltroConsultor(e.target.value)} className="!w-auto">
                  <option value="">Todos os consultores</option>
                  {consultores.map((c) => (
                    <option key={c.id} value={c.id}>{c.nome}</option>
                  ))}
                </Select>
                <Select aria-label="Filtrar avisos" value={filtro} onChange={(e) => setFiltro(e.target.value as typeof filtro)} className="!w-auto">
                  <option value="abertos">Em aberto</option>
                  <option value="resolvidos">Resolvidos</option>
                  <option value="todos">Todos</option>
                </Select>
              </div>
            </div>
            {lista.length === 0 ? (
              <EmptyState title="Nenhum aviso" description={filtro === "abertos" ? "Não há avisos em aberto." : "Nada por aqui ainda."} />
            ) : (
              <ul className="space-y-2">
                {lista.map((a) => (
                  <li key={a.id} className="rounded-xl border border-[var(--border)] p-3 text-sm">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="font-medium text-[var(--fg)]">{a.consultorNome}</p>
                        <p className="whitespace-pre-wrap break-words text-[var(--fg-muted)]">{a.mensagem}</p>
                        {(a.valor != null || a.referencia) && (
                          <p className="mt-1 text-xs text-[var(--fg-muted)]">
                            {a.valor != null && <span className="font-medium text-[var(--fg)]">{formatarMoeda(a.valor)}</span>}
                            {a.valor != null && a.referencia && " · "}
                            {a.referencia}
                          </p>
                        )}
                        <p className="mt-1 text-xs text-[var(--fg-muted)]">
                          Enviado por {a.enviadoPorNome} · {formatarDataHora(a.criadoEm)}
                          {a.lidoEm && ` · ciente em ${formatarDataHora(a.lidoEm)}`}
                        </p>
                      </div>
                      <div className="flex shrink-0 flex-col items-end gap-2">
                        <Badge variant={a.status === "Aberto" ? "warning" : "success"}>{a.status === "Aberto" ? "Em aberto" : "Resolvido"}</Badge>
                        {a.status === "Aberto" && (
                          <div className="flex gap-2">
                            <Button size="sm" variant="ghost" onClick={() => reenviar(a.id)} title="Manda a notificação de novo ao consultor">
                              <BellRing className="size-4" /> Reenviar
                            </Button>
                            <Button size="sm" variant="secondary" onClick={() => resolver(a.id)}>
                              <Check className="size-4" /> Resolver
                            </Button>
                          </div>
                        )}
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      )}
    </div>
  );
}
