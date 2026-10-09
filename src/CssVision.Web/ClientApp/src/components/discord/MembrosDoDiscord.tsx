import { RefreshCw, Users } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, ApiRequestError } from "../../lib/api";
import { formatarDataHora } from "../../lib/format";
import type { DiscordMembros } from "../../lib/types";
import { Badge, Button, Card, Input, Skeleton } from "../ui";
import { useAuth } from "../../context/AuthContext";

type Filtro = "todos" | "vinculados" | "semVinculo" | "bots";

/** Quem está no servidor do Discord, cruzado com as contas do CRM: mostra quem ainda não vinculou e quem vinculou mas saiu do servidor. */
export function MembrosDoDiscord() {
  const { temPapel } = useAuth();
  const permitido = temPapel("Admin", "GestorMaster", "SupervisorComercial");
  const [dados, setDados] = useState<DiscordMembros | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [busca, setBusca] = useState("");
  const [filtro, setFiltro] = useState<Filtro>("todos");

  const carregar = useCallback(() => {
    setCarregando(true);
    api
      .get<DiscordMembros>("/crm/discord/membros")
      .then((d) => {
        setDados(d);
        setErro(null);
      })
      .catch((e) => setErro(e instanceof ApiRequestError ? e.message : "Não foi possível carregar os membros do servidor."))
      .finally(() => setCarregando(false));
  }, []);

  useEffect(() => {
    if (permitido) carregar();
  }, [permitido, carregar]);

  const itens = useMemo(() => {
    const termo = busca.trim().toLowerCase();
    return (dados?.itens ?? []).filter((m) => {
      if (filtro === "vinculados" && !m.vinculo) return false;
      if (filtro === "semVinculo" && (m.vinculo || m.bot)) return false;
      if (filtro === "bots" && !m.bot) return false;
      if (!termo) return true;
      return [m.nome, m.usuario, m.vinculo?.nome, m.vinculo?.regional, ...m.cargos].some((t) => t?.toLowerCase().includes(termo));
    });
  }, [dados, busca, filtro]);

  if (!permitido) return null;

  const filtros: { chave: Filtro; rotulo: string; qtd?: number }[] = [
    { chave: "todos", rotulo: "Todos", qtd: dados?.total },
    { chave: "vinculados", rotulo: "Com conta no CRM", qtd: dados?.vinculados },
    { chave: "semVinculo", rotulo: "Sem vínculo", qtd: dados?.semVinculo },
    { chave: "bots", rotulo: "Bots", qtd: dados?.bots },
  ];

  return (
    <Card className="space-y-4 p-5">
      <div className="flex items-start justify-between gap-2">
        <div>
          <h2 className="flex items-center gap-2 font-semibold text-[var(--fg)]">
            <Users className="size-4 text-[var(--brand)]" aria-hidden /> Membros do servidor
          </h2>
          <p className="text-xs text-[var(--fg-muted)]">Quem está no servidor do Discord e a conta do CRM de cada um. A lista é guardada por 30 segundos.</p>
        </div>
        <Button variant="secondary" size="sm" onClick={carregar} loading={carregando} aria-label="Atualizar membros">
          <RefreshCw className="size-4" />
        </Button>
      </div>

      {erro && <p className="text-sm text-[var(--danger)]">{erro}</p>}
      {!dados && !erro && <Skeleton className="h-24 w-full" />}

      {dados && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            {filtros.map((f) => (
              <button
                key={f.chave}
                type="button"
                onClick={() => setFiltro(f.chave)}
                className={`rounded-full border px-3 py-1 text-xs font-medium ${filtro === f.chave ? "border-[var(--brand)] bg-[var(--brand)] text-white" : "border-[var(--border)] text-[var(--fg)] hover:bg-[var(--surface-hover)]"}`}
              >
                {f.rotulo} · {f.qtd ?? 0}
              </button>
            ))}
            <Input className="ml-auto max-w-xs" value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Buscar por nome, regional ou cargo" aria-label="Buscar membro" />
          </div>

          <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
            <table className="w-full text-left text-sm">
              <thead className="bg-[var(--surface-hover)] text-xs text-[var(--fg-muted)]">
                <tr>
                  <th className="px-3 py-2">Membro</th>
                  <th className="px-3 py-2">Conta no CRM</th>
                  <th className="px-3 py-2">Cargos</th>
                  <th className="px-3 py-2">Entrou em</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border)]">
                {itens.map((m) => (
                  <tr key={m.id}>
                    <td className="px-3 py-2">
                      <div className="flex items-center gap-2">
                        {m.avatarUrl ? (
                          <img src={m.avatarUrl} alt="" className="size-7 rounded-full" loading="lazy" />
                        ) : (
                          <span className="grid size-7 place-items-center rounded-full bg-[var(--surface-hover)] text-xs">{m.nome.slice(0, 1).toUpperCase()}</span>
                        )}
                        <div>
                          <p className="font-medium text-[var(--fg)]">
                            {m.nome} {m.bot && <Badge variant="info">bot</Badge>}
                          </p>
                          <p className="text-xs text-[var(--fg-muted)]">@{m.usuario}</p>
                        </div>
                      </div>
                    </td>
                    <td className="px-3 py-2">
                      {m.vinculo ? (
                        <span className="text-[var(--fg)]">
                          {m.vinculo.nome}
                          {m.vinculo.regional && <span className="text-[var(--fg-muted)]"> · {m.vinculo.regional}</span>}
                          {!m.vinculo.ativo && <Badge variant="neutral">inativo</Badge>}
                        </span>
                      ) : m.bot ? (
                        <span className="text-[var(--fg-muted)]">—</span>
                      ) : (
                        <Badge variant="warning">sem vínculo</Badge>
                      )}
                    </td>
                    <td className="px-3 py-2">
                      <div className="flex flex-wrap gap-1">
                        {m.cargos.map((c) => (
                          <Badge key={c} variant="neutral">
                            {c}
                          </Badge>
                        ))}
                      </div>
                    </td>
                    <td className="px-3 py-2 text-xs text-[var(--fg-muted)]">{m.entrouEm ? formatarDataHora(m.entrouEm) : "—"}</td>
                  </tr>
                ))}
                {itens.length === 0 && (
                  <tr>
                    <td colSpan={4} className="px-3 py-6 text-center text-[var(--fg-muted)]">
                      Nenhum membro encontrado.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          {dados.foraDoServidor.length > 0 && (
            <div className="space-y-1">
              <p className="text-sm font-semibold text-[var(--fg)]">Vincularam a conta, mas não estão mais no servidor</p>
              <ul className="text-sm text-[var(--fg-muted)]">
                {dados.foraDoServidor.map((f) => (
                  <li key={f.usuarioId}>
                    {f.nome}
                    {f.regional ? ` · ${f.regional}` : ""}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </>
      )}
    </Card>
  );
}
