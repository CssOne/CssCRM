import { useCallback, useEffect, useState } from "react";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarTelefone } from "../../lib/format";
import type { PagedResult, UserSummary } from "../../lib/types";
import { Badge, Button, Card, EmptyState, ErrorState, Input, Pagination, Select, Skeleton, useToast } from "../../components/ui";

/**
 * Usuários do perfil Financeiro: lista os consultores da regional dele e permite só ativar ou inativar a conta de cada um
 * (cadastro, edição e senha continuam com a gestão).
 */
export function UsuariosFinanceiroPage() {
  const { notificar } = useToast();
  const [dados, setDados] = useState<PagedResult<UserSummary> | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [busca, setBusca] = useState("");
  const [situacao, setSituacao] = useState<"" | "true" | "false">("");
  const [pagina, setPagina] = useState(1);
  const [alterando, setAlterando] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<PagedResult<UserSummary>>(
        `/crm/users${toQueryString({ papel: "Comercial", busca: busca.trim() || undefined, ativo: situacao || undefined, pagina, tamanhoPagina: 15 })}`,
        controller.signal
      )
      .then((r) => {
        setDados(r);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os consultores.");
      });
    return () => controller.abort();
  }, [recarregar, busca, situacao, pagina]);

  const alternar = useCallback(
    async (u: UserSummary) => {
      setAlterando(u.id);
      try {
        await api.put(`/crm/users/${u.id}/ativo`, { ativo: !u.ativo });
        notificar("success", u.ativo ? `Conta de ${u.nomeCompleto} inativada.` : `Conta de ${u.nomeCompleto} ativada.`);
        setRecarregar((n) => n + 1);
      } catch (e) {
        notificar("error", e instanceof Error ? e.message : "Não foi possível alterar a conta.");
      } finally {
        setAlterando(null);
      }
    },
    [notificar]
  );

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Usuários</h1>
        <p className="text-sm text-[var(--fg-muted)]">Consultores da sua regional. Aqui você ativa ou inativa a conta de cada um.</p>
      </div>

      <div className="flex flex-wrap gap-3">
        <div className="w-64">
          <Input placeholder="Buscar por nome ou e-mail…" value={busca} onChange={(e) => { setBusca(e.target.value); setPagina(1); }} aria-label="Buscar consultor" />
        </div>
        <div className="w-40">
          <Select value={situacao} onChange={(e) => { setSituacao(e.target.value as typeof situacao); setPagina(1); }} aria-label="Situação da conta">
            <option value="">Todos</option>
            <option value="true">Ativos</option>
            <option value="false">Inativos</option>
          </Select>
        </div>
      </div>

      {erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados ? (
        <div className="space-y-2">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-16" />
          ))}
        </div>
      ) : dados.itens.length === 0 ? (
        <EmptyState title="Nenhum consultor" description="Não há consultores com este filtro na sua regional." />
      ) : (
        <>
          <div className="space-y-2">
            {dados.itens.map((u) => (
              <Card key={u.id} className="flex items-center justify-between gap-3 p-3 text-sm">
                <div className="min-w-0">
                  <p className="truncate font-medium text-[var(--fg)]">{u.nomeCompleto}</p>
                  <p className="truncate text-xs text-[var(--fg-muted)]">
                    {u.email}
                    {u.telefone ? ` · ${formatarTelefone(u.telefone)}` : ""}
                    {u.regionalNome ? ` · ${u.regionalNome}` : ""}
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-3">
                  <Badge variant={u.ativo ? "success" : "danger"}>{u.ativo ? "Ativo" : "Inativo"}</Badge>
                  <Button size="sm" variant="secondary" loading={alterando === u.id} onClick={() => alternar(u)}>
                    {u.ativo ? "Inativar" : "Ativar"}
                  </Button>
                </div>
              </Card>
            ))}
          </div>
          <Pagination pagina={dados.pagina} totalPaginas={Math.max(1, Math.ceil(dados.totalRegistros / dados.tamanhoPagina))} onChange={setPagina} />
        </>
      )}
    </div>
  );
}
