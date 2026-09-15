import { Handshake, Mail, Phone, Search, ShoppingCart, Target, Wallet } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarMoeda, formatarPercentual, formatarTelefone } from "../../lib/format";
import type { ConsultorDesempenho } from "../../lib/types";
import { Badge, Card, EmptyState, ErrorState, Input, Skeleton } from "../../components/ui";

function mesAtualIso(): string {
  const hoje = new Date();
  return `${hoje.getFullYear()}-${String(hoje.getMonth() + 1).padStart(2, "0")}-01`;
}

function Avatar({ nome }: { nome: string }) {
  const iniciais = nome
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join("");
  return (
    <div className="flex size-11 shrink-0 items-center justify-center rounded-full bg-[var(--brand)] text-sm font-semibold text-white">
      {iniciais || "?"}
    </div>
  );
}

export function ConsultoresPage() {
  const [mesReferencia, setMesReferencia] = useState(mesAtualIso());
  const [busca, setBusca] = useState("");
  const [consultores, setConsultores] = useState<ConsultorDesempenho[] | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<ConsultorDesempenho[]>(`/crm/management/consultores${toQueryString({ mesReferencia })}`, signal)
        .then(setConsultores)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os consultores.");
        })
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [mesReferencia]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  const filtrados = useMemo(() => {
    if (!consultores) return [];
    const termo = busca.trim().toLowerCase();
    if (!termo) return consultores;
    return consultores.filter((c) => c.nome.toLowerCase().includes(termo) || c.email.toLowerCase().includes(termo));
  }, [consultores, busca]);

  const totais = useMemo(() => {
    const base = consultores ?? [];
    return {
      leadsAtivos: base.reduce((s, c) => s + c.leadsAtivos, 0),
      valorGanho: base.reduce((s, c) => s + c.valorGanho, 0),
      vendasGanhas: base.reduce((s, c) => s + c.vendasGanhas, 0),
    };
  }, [consultores]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Consultores</h1>
          <p className="text-sm text-[var(--fg-muted)]">Gerencie todos os consultores e acompanhe o rendimento individual de cada um.</p>
        </div>
        <Input type="month" value={mesReferencia.slice(0, 7)} onChange={(e) => setMesReferencia(`${e.target.value}-01`)} />
      </div>

      {!carregando && consultores && consultores.length > 0 && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Card className="flex items-center gap-3 p-4">
            <div className="flex size-10 items-center justify-center rounded-full bg-[var(--brand-soft)] text-[var(--brand)]">
              <Wallet className="size-5" />
            </div>
            <div>
              <p className="text-xs font-medium text-[var(--fg-muted)]">Consultores ativos</p>
              <p className="text-xl font-semibold text-[var(--fg)]">{consultores.filter((c) => c.ativo).length}</p>
            </div>
          </Card>
          <Card className="flex items-center gap-3 p-4">
            <div className="flex size-10 items-center justify-center rounded-full bg-[var(--success-soft)] text-[var(--success)]">
              <ShoppingCart className="size-5" />
            </div>
            <div>
              <p className="text-xs font-medium text-[var(--fg-muted)]">Vendas ganhas no mês</p>
              <p className="text-xl font-semibold text-[var(--fg)]">{totais.vendasGanhas}</p>
            </div>
          </Card>
          <Card className="flex items-center gap-3 p-4">
            <div className="flex size-10 items-center justify-center rounded-full bg-[var(--brand-soft)] text-[var(--brand)]">
              <Handshake className="size-5" />
            </div>
            <div>
              <p className="text-xs font-medium text-[var(--fg-muted)]">Valor ganho no mês</p>
              <p className="text-xl font-semibold text-[var(--fg)]">{formatarMoeda(totais.valorGanho)}</p>
            </div>
          </Card>
        </div>
      )}

      <div className="relative max-w-md">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" />
        <Input className="pl-9" placeholder="Buscar consultor por nome ou e-mail" value={busca} onChange={(e) => setBusca(e.target.value)} />
      </div>

      {carregando ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-56" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : filtrados.length === 0 ? (
        <EmptyState title="Nenhum consultor encontrado" description="Ajuste a busca ou cadastre novos consultores na página Usuários." />
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {filtrados.map((c) => (
            <Card key={c.id} className="p-5">
              <div className="flex items-start gap-3">
                <Avatar nome={c.nome} />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <p className="truncate font-semibold text-[var(--fg)]">{c.nome}</p>
                    <Badge variant={c.ativo ? "success" : "danger"}>{c.ativo ? "Ativo" : "Inativo"}</Badge>
                  </div>
                  {c.regionalNome && <p className="truncate text-xs text-[var(--fg-muted)]">{c.regionalNome}</p>}
                </div>
              </div>

              <div className="mt-3 space-y-1 text-xs text-[var(--fg-muted)]">
                <p className="flex items-center gap-1.5 truncate">
                  <Mail className="size-3.5 shrink-0" /> {c.email}
                </p>
                {c.telefone && (
                  <p className="flex items-center gap-1.5">
                    <Phone className="size-3.5 shrink-0" /> {formatarTelefone(c.telefone)}
                  </p>
                )}
              </div>

              <div className="mt-4 grid grid-cols-2 gap-3 border-t border-[var(--border)] pt-3 text-sm">
                <div>
                  <p className="text-xs text-[var(--fg-muted)]">Leads ativos</p>
                  <p className="font-semibold text-[var(--fg)]">{c.leadsAtivos}</p>
                </div>
                <div>
                  <p className="text-xs text-[var(--fg-muted)]">Oportunidades abertas</p>
                  <p className="font-semibold text-[var(--fg)]">{c.oportunidadesAbertas}</p>
                </div>
                <div>
                  <p className="text-xs text-[var(--fg-muted)]">Valor em pipeline</p>
                  <p className="font-semibold text-[var(--fg)]">{formatarMoeda(c.valorPipeline)}</p>
                </div>
                <div>
                  <p className="text-xs text-[var(--fg-muted)]">Vendas ganhas (mês)</p>
                  <p className="font-semibold text-[var(--success)]">
                    {c.vendasGanhas} · {formatarMoeda(c.valorGanho)}
                  </p>
                </div>
                <div>
                  <p className="text-xs text-[var(--fg-muted)]">Taxa de conversão</p>
                  <p className="font-semibold text-[var(--fg)]">{formatarPercentual(c.taxaConversao)}</p>
                </div>
                <div>
                  <p className="text-xs text-[var(--fg-muted)]">Leads recebidos (mês)</p>
                  <p className="font-semibold text-[var(--fg)]">
                    {c.leadsRecebidosNoMes}
                    {c.limiteMensalLeads != null && ` / ${c.limiteMensalLeads}`}
                  </p>
                </div>
              </div>

              <div className="mt-4 border-t border-[var(--border)] pt-3">
                <div className="mb-1 flex items-center justify-between text-xs">
                  <span className="flex items-center gap-1 font-medium text-[var(--fg-muted)]">
                    <Target className="size-3.5" /> Meta do mês
                  </span>
                  <span className="text-[var(--fg-muted)]">{formatarPercentual(Math.min(100, c.percentualMeta))}</span>
                </div>
                <div className="h-2 w-full overflow-hidden rounded-full bg-[var(--surface-hover)]">
                  <div className="h-full rounded-full bg-[var(--brand)]" style={{ width: `${Math.min(100, c.percentualMeta)}%` }} />
                </div>
                <p className="mt-1 text-xs text-[var(--fg-muted)]">
                  {formatarMoeda(c.realizadoValor)} de {formatarMoeda(c.metaValor)}
                </p>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
