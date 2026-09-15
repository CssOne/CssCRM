import { ChevronDown, ChevronRight, KeyRound, Pencil, Plus, Trash2 } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarData, formatarTelefone } from "../../lib/format";
import type { Grupo, PagedResult, Regional, UserSummary } from "../../lib/types";
import { useAuth } from "../../context/AuthContext";
import {
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  EmptyState,
  ErrorState,
  Input,
  Modal,
  Pagination,
  Select,
  Skeleton,
  Tabs,
  useToast,
} from "../../components/ui";
import { PAPEL_LABEL, UserForm, paraAtualizarRequest, paraCriarRequest, paraFormValues, type UserFormValues } from "../../components/crm/UserForm";

function MembroAvatar({ nome, fotoUrl }: { nome: string; fotoUrl?: string | null }) {
  const [erroAoCarregar, setErroAoCarregar] = useState(false);
  const iniciais = nome
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join("");

  if (fotoUrl && !erroAoCarregar) {
    return (
      <img
        src={fotoUrl}
        alt={nome}
        onError={() => setErroAoCarregar(true)}
        className="size-8 shrink-0 rounded-full object-cover"
      />
    );
  }

  return (
    <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-[var(--brand)] text-xs font-semibold text-white">
      {iniciais || "?"}
    </div>
  );
}

export function UsersPage() {
  const { temPapel } = useAuth();
  const podeGerenciarTudo = temPapel("Admin", "GestorMaster");
  const { notificar } = useToast();

  const [aba, setAba] = useState("usuarios");

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Usuários</h1>
        <p className="text-sm text-[var(--fg-muted)]">
          {podeGerenciarTudo
            ? "Cadastre consultores, gestores comerciais e administradores."
            : "Cadastre e gerencie os consultores da sua regional."}
        </p>
      </div>

      <Tabs
        tabs={[
          { chave: "usuarios", rotulo: "Usuários" },
          podeGerenciarTudo ? { chave: "regionais", rotulo: "Regionais" } : { chave: "grupos", rotulo: "Grupos" },
        ]}
        ativa={aba}
        onChange={setAba}
      />

      {aba === "usuarios" && <UsuariosTab podeGerenciarTudo={podeGerenciarTudo} notificar={notificar} />}
      {aba === "grupos" && !podeGerenciarTudo && <GruposDeRegional regionalId={null} notificar={notificar} />}
      {aba === "regionais" && podeGerenciarTudo && <RegionaisTab notificar={notificar} />}
    </div>
  );
}

function UsuariosTab({ podeGerenciarTudo, notificar }: { podeGerenciarTudo: boolean; notificar: (t: "success" | "error", m: string) => void }) {
  const { sessao } = useAuth();
  const [busca, setBusca] = useState("");
  const [papel, setPapel] = useState("");
  const [regionalId, setRegionalId] = useState("");
  const [pagina, setPagina] = useState(1);

  const [regionais, setRegionais] = useState<Regional[]>([]);
  const [dados, setDados] = useState<PagedResult<UserSummary> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [salvando, setSalvando] = useState(false);

  const [modalNovo, setModalNovo] = useState(false);
  const [usuarioEditando, setUsuarioEditando] = useState<UserSummary | null>(null);
  const [usuarioRedefinindo, setUsuarioRedefinindo] = useState<UserSummary | null>(null);
  const [novaSenha, setNovaSenha] = useState("");
  const [usuarioExcluindo, setUsuarioExcluindo] = useState<UserSummary | null>(null);
  const [excluindo, setExcluindo] = useState(false);

  useEffect(() => {
    if (podeGerenciarTudo) api.get<Regional[]>("/crm/settings/regionals").then(setRegionais);
  }, [podeGerenciarTudo]);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<UserSummary>>(`/crm/users${toQueryString({ busca, papel, regionalId, pagina, tamanhoPagina: 20 })}`, signal)
        .then(setDados)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os usuários.");
        })
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [busca, papel, regionalId, pagina]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  async function criarUsuario(valores: UserFormValues) {
    setSalvando(true);
    try {
      await api.post("/crm/users", paraCriarRequest(valores));
      notificar("success", "Usuário cadastrado com sucesso.");
      setModalNovo(false);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível cadastrar o usuário.");
    } finally {
      setSalvando(false);
    }
  }

  async function salvarEdicao(valores: UserFormValues) {
    if (!usuarioEditando) return;
    setSalvando(true);
    try {
      await api.put(`/crm/users/${usuarioEditando.id}`, paraAtualizarRequest(valores));
      notificar("success", "Usuário atualizado com sucesso.");
      setUsuarioEditando(null);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar as alterações.");
    } finally {
      setSalvando(false);
    }
  }

  async function excluirUsuario() {
    if (!usuarioExcluindo) return;
    setExcluindo(true);
    try {
      await api.del(`/crm/users/${usuarioExcluindo.id}`);
      notificar("success", "Usuário excluído com sucesso.");
      setUsuarioExcluindo(null);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível excluir o usuário.");
    } finally {
      setExcluindo(false);
    }
  }

  async function redefinirSenha() {
    if (!usuarioRedefinindo || novaSenha.length < 8) return;
    setSalvando(true);
    try {
      await api.post(`/crm/users/${usuarioRedefinindo.id}/reset-password`, { novaSenha });
      notificar("success", "Senha redefinida com sucesso.");
      setUsuarioRedefinindo(null);
      setNovaSenha("");
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível redefinir a senha.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap items-end gap-3">
          <div className="w-56">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Buscar</label>
            <Input placeholder="Nome ou e-mail" value={busca} onChange={(e) => { setBusca(e.target.value); setPagina(1); }} />
          </div>
          {podeGerenciarTudo && (
            <>
              <div className="w-44">
                <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Papel</label>
                <Select value={papel} onChange={(e) => { setPapel(e.target.value); setPagina(1); }}>
                  <option value="">Todos</option>
                  {Object.entries(PAPEL_LABEL).map(([valor, rotulo]) => (
                    <option key={valor} value={valor}>
                      {rotulo}
                    </option>
                  ))}
                </Select>
              </div>
              <div className="w-44">
                <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Regional</label>
                <Select value={regionalId} onChange={(e) => { setRegionalId(e.target.value); setPagina(1); }}>
                  <option value="">Todas</option>
                  {regionais.map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.nome}
                    </option>
                  ))}
                </Select>
              </div>
            </>
          )}
        </div>
        <Button onClick={() => setModalNovo(true)}>
          <Plus className="size-4" /> Novo usuário
        </Button>
      </div>

      {carregando ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-16" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados || dados.itens.length === 0 ? (
        <EmptyState title="Nenhum usuário encontrado" description="Cadastre o primeiro usuário para começar." />
      ) : (
        <>
          <div className="space-y-2">
            {dados.itens.map((usuario) => (
              <div key={usuario.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <p className="truncate font-medium text-[var(--fg)]">{usuario.nomeCompleto}</p>
                    {usuario.papeis.map((p) => (
                      <Badge key={p} variant="brand">
                        {PAPEL_LABEL[p] ?? p}
                      </Badge>
                    ))}
                    <Badge variant={usuario.ativo ? "success" : "danger"}>{usuario.ativo ? "Ativo" : "Inativo"}</Badge>
                  </div>
                  <p className="truncate text-xs text-[var(--fg-muted)]">
                    {usuario.email} · {formatarTelefone(usuario.telefone)}
                    {usuario.regionalNome && ` · ${usuario.regionalNome}`}
                    {usuario.grupoNome && ` · ${usuario.grupoNome}`}
                    {usuario.gestorComercialNome && ` · gestor: ${usuario.gestorComercialNome}`}
                  </p>
                </div>
                <div className="flex items-center gap-2 text-xs text-[var(--fg-muted)]">
                  <span>desde {formatarData(usuario.criadoEm)}</span>
                  <Button size="sm" variant="secondary" onClick={() => setUsuarioRedefinindo(usuario)}>
                    <KeyRound className="size-4" /> Senha
                  </Button>
                  <Button size="sm" variant="secondary" onClick={() => setUsuarioEditando(usuario)}>
                    <Pencil className="size-4" /> Editar
                  </Button>
                  {podeGerenciarTudo && usuario.id !== sessao?.id && (
                    <Button size="sm" variant="danger" onClick={() => setUsuarioExcluindo(usuario)}>
                      <Trash2 className="size-4" /> Excluir
                    </Button>
                  )}
                </div>
              </div>
            ))}
          </div>
          <Pagination pagina={dados.pagina} totalPaginas={dados.totalPaginas} onChange={setPagina} />
        </>
      )}

      <Modal open={modalNovo} onClose={() => setModalNovo(false)} title="Novo usuário" size="lg">
        <UserForm modoEdicao={false} podeGerenciarTudo={podeGerenciarTudo} salvando={salvando} onSubmit={criarUsuario} onCancel={() => setModalNovo(false)} />
      </Modal>

      <Modal open={!!usuarioEditando} onClose={() => setUsuarioEditando(null)} title="Editar usuário" size="lg">
        {usuarioEditando && (
          <UserForm
            modoEdicao
            podeGerenciarTudo={podeGerenciarTudo}
            valoresIniciais={paraFormValues(usuarioEditando)}
            salvando={salvando}
            onSubmit={salvarEdicao}
            onCancel={() => setUsuarioEditando(null)}
          />
        )}
      </Modal>

      <Modal open={!!usuarioRedefinindo} onClose={() => { setUsuarioRedefinindo(null); setNovaSenha(""); }} title="Redefinir senha" size="sm">
        {usuarioRedefinindo && (
          <div className="space-y-4">
            <p className="text-sm text-[var(--fg-muted)]">
              Definir uma nova senha para <strong className="text-[var(--fg)]">{usuarioRedefinindo.nomeCompleto}</strong>.
            </p>
            <Input type="password" placeholder="Nova senha (mín. 8 caracteres)" value={novaSenha} onChange={(e) => setNovaSenha(e.target.value)} />
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => { setUsuarioRedefinindo(null); setNovaSenha(""); }} disabled={salvando}>
                Cancelar
              </Button>
              <Button onClick={redefinirSenha} loading={salvando} disabled={novaSenha.length < 8}>
                Redefinir
              </Button>
            </div>
          </div>
        )}
      </Modal>

      <ConfirmDialog
        open={!!usuarioExcluindo}
        title="Excluir usuário"
        message={
          <>
            Tem certeza que deseja excluir <strong className="text-[var(--fg)]">{usuarioExcluindo?.nomeCompleto}</strong>? Essa ação
            não pode ser desfeita. Se o usuário tiver leads, oportunidades ou metas vinculados, desative a conta em vez de excluir.
          </>
        }
        confirmLabel="Excluir"
        danger
        loading={excluindo}
        onConfirm={excluirUsuario}
        onCancel={() => setUsuarioExcluindo(null)}
      />
    </div>
  );
}

function RegionaisTab({ notificar }: { notificar: (t: "success" | "error", m: string) => void }) {
  const [regionais, setRegionais] = useState<Regional[] | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [salvando, setSalvando] = useState(false);

  const [modalNova, setModalNova] = useState(false);
  const [nomeNova, setNomeNova] = useState("");
  const [regionalEditando, setRegionalEditando] = useState<Regional | null>(null);
  const [nomeEdicao, setNomeEdicao] = useState("");
  const [ativaEdicao, setAtivaEdicao] = useState(true);
  const [regionalExpandidaId, setRegionalExpandidaId] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    api
      .get<Regional[]>("/crm/settings/regionals", controller.signal)
      .then(setRegionais)
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar as regionais.");
      })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, [recarregar]);

  async function criarRegional() {
    if (!nomeNova.trim()) return;
    setSalvando(true);
    try {
      await api.post("/crm/settings/regionals", { nome: nomeNova.trim() });
      notificar("success", "Regional criada com sucesso.");
      setModalNova(false);
      setNomeNova("");
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível criar a regional.");
    } finally {
      setSalvando(false);
    }
  }

  function abrirEdicao(regional: Regional) {
    setRegionalEditando(regional);
    setNomeEdicao(regional.nome);
    setAtivaEdicao(regional.ativa);
  }

  async function salvarEdicao() {
    if (!regionalEditando || !nomeEdicao.trim()) return;
    setSalvando(true);
    try {
      await api.put(`/crm/settings/regionals/${regionalEditando.id}`, { nome: nomeEdicao.trim(), ativa: ativaEdicao });
      notificar("success", "Regional atualizada com sucesso.");
      setRegionalEditando(null);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar a regional.");
    } finally {
      setSalvando(false);
    }
  }

  if (carregando) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-14" />
        ))}
      </div>
    );
  }

  if (erro) return <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />;

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={() => setModalNova(true)}>
          <Plus className="size-4" /> Nova regional
        </Button>
      </div>

      {!regionais || regionais.length === 0 ? (
        <EmptyState title="Nenhuma regional cadastrada" description="Crie regionais para organizar consultores e gestores por área." />
      ) : (
        <div className="space-y-2">
          {regionais.map((r) => {
            const expandida = regionalExpandidaId === r.id;
            return (
              <div key={r.id} className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
                <div className="flex items-center justify-between gap-3">
                  <button
                    type="button"
                    onClick={() => setRegionalExpandidaId(expandida ? null : r.id)}
                    className="focus-ring flex min-w-0 flex-1 items-center gap-2 text-left cursor-pointer"
                  >
                    {expandida ? (
                      <ChevronDown className="size-4 shrink-0 text-[var(--fg-muted)]" />
                    ) : (
                      <ChevronRight className="size-4 shrink-0 text-[var(--fg-muted)]" />
                    )}
                    <div>
                      <div className="flex items-center gap-2">
                        <p className="font-medium text-[var(--fg)]">{r.nome}</p>
                        <Badge variant={r.ativa ? "success" : "danger"}>{r.ativa ? "Ativa" : "Inativa"}</Badge>
                      </div>
                      <p className="text-xs text-[var(--fg-muted)]">{r.quantidadeUsuarios} usuário(s)</p>
                    </div>
                  </button>
                  <Button size="sm" variant="secondary" onClick={() => abrirEdicao(r)}>
                    <Pencil className="size-4" /> Editar
                  </Button>
                </div>
                {expandida && (
                  <div className="mt-3 space-y-4 border-t border-[var(--border)] pt-3">
                    <div>
                      <p className="mb-2 text-xs font-medium text-[var(--fg-muted)]">Usuários desta regional</p>
                      <UsuariosDaRegional regionalId={r.id} />
                    </div>
                    <div>
                      <p className="mb-2 text-xs font-medium text-[var(--fg-muted)]">Grupos</p>
                      <GruposDeRegional regionalId={r.id} notificar={notificar} />
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      <Modal open={modalNova} onClose={() => setModalNova(false)} title="Nova regional" size="sm">
        <div className="space-y-4">
          <Input placeholder="Nome da regional" value={nomeNova} onChange={(e) => setNomeNova(e.target.value)} />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setModalNova(false)} disabled={salvando}>
              Cancelar
            </Button>
            <Button onClick={criarRegional} loading={salvando} disabled={!nomeNova.trim()}>
              Criar
            </Button>
          </div>
        </div>
      </Modal>

      <Modal open={!!regionalEditando} onClose={() => setRegionalEditando(null)} title="Editar regional" size="sm">
        <div className="space-y-4">
          <Input placeholder="Nome da regional" value={nomeEdicao} onChange={(e) => setNomeEdicao(e.target.value)} />
          <label className="flex items-center gap-2 text-sm text-[var(--fg)]">
            <input type="checkbox" className="size-4" checked={ativaEdicao} onChange={(e) => setAtivaEdicao(e.target.checked)} />
            Regional ativa
          </label>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setRegionalEditando(null)} disabled={salvando}>
              Cancelar
            </Button>
            <Button onClick={salvarEdicao} loading={salvando} disabled={!nomeEdicao.trim()}>
              Salvar
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

/** Lista todos os usuários (consultores e gestores) de uma regional, com foto/iniciais e o grupo de cada um. */
function UsuariosDaRegional({ regionalId }: { regionalId: string }) {
  const [usuarios, setUsuarios] = useState<UserSummary[] | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    api
      .get<PagedResult<UserSummary>>(`/crm/users${toQueryString({ regionalId, tamanhoPagina: 200 })}`, controller.signal)
      .then((res) => setUsuarios(res.itens))
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os usuários.");
      })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, [regionalId]);

  if (carregando) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 2 }).map((_, i) => (
          <Skeleton key={i} className="h-10" />
        ))}
      </div>
    );
  }

  if (erro) return <ErrorState message={erro} />;
  if (!usuarios || usuarios.length === 0) {
    return <p className="text-xs text-[var(--fg-muted)]">Nenhum usuário cadastrado nesta regional ainda.</p>;
  }

  return (
    <div className="flex flex-wrap gap-3">
      {usuarios.map((u) => (
        <div key={u.id} className="flex items-center gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-2 py-1.5">
          <MembroAvatar nome={u.nomeCompleto} fotoUrl={u.fotoUrl} />
          <div className="min-w-0">
            <p className="truncate text-sm text-[var(--fg)]">{u.nomeCompleto}</p>
            <p className="truncate text-xs text-[var(--fg-muted)]">
              {u.papeis.map((p) => PAPEL_LABEL[p] ?? p).join(", ")}
              {u.grupoNome ? ` · ${u.grupoNome}` : " · Sem grupo"}
            </p>
          </div>
        </div>
      ))}
    </div>
  );
}

/**
 * Lista e gerencia os grupos de uma regional. Quando `regionalId` é nulo, o backend infere a
 * regional do próprio GestorComercial autenticado (usado no caso do gestor, que não navega por
 * uma lista de regionais — só enxerga a sua).
 */
function GruposDeRegional({ regionalId, notificar }: { regionalId: string | null; notificar: (t: "success" | "error", m: string) => void }) {
  const [grupos, setGrupos] = useState<Grupo[] | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [salvando, setSalvando] = useState(false);

  const [modalNovo, setModalNovo] = useState(false);
  const [nomeNovo, setNomeNovo] = useState("");
  const [grupoEditando, setGrupoEditando] = useState<Grupo | null>(null);
  const [nomeEdicao, setNomeEdicao] = useState("");
  const [ativoEdicao, setAtivoEdicao] = useState(true);
  const [consultores, setConsultores] = useState<UserSummary[]>([]);
  const [membrosSelecionados, setMembrosSelecionados] = useState<Set<string>>(new Set());
  const [gruposExpandidos, setGruposExpandidos] = useState<Set<string>>(new Set());

  function alternarExpansao(grupoId: string) {
    setGruposExpandidos((atual) => {
      const novo = new Set(atual);
      if (novo.has(grupoId)) novo.delete(grupoId);
      else novo.add(grupoId);
      return novo;
    });
  }

  useEffect(() => {
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    api
      .get<Grupo[]>(`/crm/settings/groups${regionalId ? `?regionalId=${regionalId}` : ""}`, controller.signal)
      .then(setGrupos)
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os grupos.");
      })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, [regionalId, recarregar]);

  async function criarGrupo() {
    if (!nomeNovo.trim()) return;
    setSalvando(true);
    try {
      await api.post("/crm/settings/groups", { regionalId, nome: nomeNovo.trim() });
      notificar("success", "Grupo criado com sucesso.");
      setModalNovo(false);
      setNomeNovo("");
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível criar o grupo.");
    } finally {
      setSalvando(false);
    }
  }

  function abrirEdicao(grupo: Grupo) {
    setGrupoEditando(grupo);
    setNomeEdicao(grupo.nome);
    setAtivoEdicao(grupo.ativo);
    setMembrosSelecionados(new Set(grupo.consultores.map((c) => c.id)));
    api
      .get<PagedResult<UserSummary>>(
        `/crm/users${toQueryString({ papel: "Comercial", regionalId: regionalId || undefined, tamanhoPagina: 200 })}`
      )
      .then((res) => setConsultores(res.itens));
  }

  function alternarMembro(id: string) {
    setMembrosSelecionados((atual) => {
      const novo = new Set(atual);
      if (novo.has(id)) novo.delete(id);
      else novo.add(id);
      return novo;
    });
  }

  async function salvarEdicao() {
    if (!grupoEditando || !nomeEdicao.trim()) return;
    setSalvando(true);
    try {
      await api.put(`/crm/settings/groups/${grupoEditando.id}`, { nome: nomeEdicao.trim(), ativo: ativoEdicao });
      await api.put(`/crm/settings/groups/${grupoEditando.id}/members`, { consultorIds: Array.from(membrosSelecionados) });
      notificar("success", "Grupo atualizado com sucesso.");
      setGrupoEditando(null);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar o grupo.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button size="sm" onClick={() => setModalNovo(true)}>
          <Plus className="size-4" /> Novo grupo
        </Button>
      </div>

      {carregando ? (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-16" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !grupos || grupos.length === 0 ? (
        <EmptyState title="Nenhum grupo cadastrado" description="Crie grupos para organizar os consultores desta regional (ex: Externos, Internos)." />
      ) : (
        <div className="space-y-2">
          {grupos.map((g) => {
            const expandido = gruposExpandidos.has(g.id);
            return (
              <div key={g.id} className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
                <div className="flex items-center justify-between gap-3">
                  <button
                    type="button"
                    onClick={() => alternarExpansao(g.id)}
                    className="focus-ring flex min-w-0 flex-1 items-center gap-2 text-left cursor-pointer"
                  >
                    {expandido ? (
                      <ChevronDown className="size-4 shrink-0 text-[var(--fg-muted)]" />
                    ) : (
                      <ChevronRight className="size-4 shrink-0 text-[var(--fg-muted)]" />
                    )}
                    <p className="font-medium text-[var(--fg)]">{g.nome}</p>
                    <Badge variant={g.ativo ? "success" : "danger"}>{g.ativo ? "Ativo" : "Inativo"}</Badge>
                    <span className="text-xs text-[var(--fg-muted)]">{g.consultores.length} consultor(es)</span>
                  </button>
                  <Button size="sm" variant="secondary" onClick={() => abrirEdicao(g)}>
                    <Pencil className="size-4" /> Editar
                  </Button>
                </div>
                {expandido && (
                  <div className="mt-3 border-t border-[var(--border)] pt-3">
                    {g.consultores.length === 0 ? (
                      <p className="text-xs text-[var(--fg-muted)]">Nenhum consultor neste grupo.</p>
                    ) : (
                      <div className="flex flex-wrap gap-3">
                        {g.consultores.map((c) => (
                          <div key={c.id} className="flex items-center gap-2">
                            <MembroAvatar nome={c.nomeCompleto} fotoUrl={c.fotoUrl} />
                            <span className="text-sm text-[var(--fg)]">{c.nomeCompleto}</span>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      <Modal open={modalNovo} onClose={() => setModalNovo(false)} title="Novo grupo" size="sm">
        <div className="space-y-4">
          <Input placeholder="Nome do grupo (ex: Externos)" value={nomeNovo} onChange={(e) => setNomeNovo(e.target.value)} />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setModalNovo(false)} disabled={salvando}>
              Cancelar
            </Button>
            <Button onClick={criarGrupo} loading={salvando} disabled={!nomeNovo.trim()}>
              Criar
            </Button>
          </div>
        </div>
      </Modal>

      <Modal open={!!grupoEditando} onClose={() => setGrupoEditando(null)} title="Editar grupo" size="sm">
        <div className="space-y-4">
          <Input placeholder="Nome do grupo" value={nomeEdicao} onChange={(e) => setNomeEdicao(e.target.value)} />
          <Checkbox label="Grupo ativo" checked={ativoEdicao} onChange={(e) => setAtivoEdicao(e.target.checked)} />

          <div>
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Consultores no grupo</label>
            {consultores.length === 0 ? (
              <p className="text-xs text-[var(--fg-muted)]">Nenhum consultor cadastrado nesta regional ainda.</p>
            ) : (
              <div className="max-h-52 space-y-1 overflow-y-auto rounded-lg border border-[var(--border)] p-2">
                {consultores.map((c) => (
                  <Checkbox
                    key={c.id}
                    label={c.nomeCompleto}
                    checked={membrosSelecionados.has(c.id)}
                    onChange={() => alternarMembro(c.id)}
                  />
                ))}
              </div>
            )}
          </div>

          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setGrupoEditando(null)} disabled={salvando}>
              Cancelar
            </Button>
            <Button onClick={salvarEdicao} loading={salvando} disabled={!nomeEdicao.trim()}>
              Salvar
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
