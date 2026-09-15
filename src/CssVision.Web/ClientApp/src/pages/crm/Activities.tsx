import { Pencil, Plus, Trash2, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarDataHora } from "../../lib/format";
import {
  StatusAtividade,
  VisaoAtividade,
  type Activity,
  type ActivityCreateRequest,
  type ActivityUpdateRequest,
  type LeadListItem,
  type PagedResult,
  type VendedorResumo,
} from "../../lib/types";
import { useAuth } from "../../context/AuthContext";
import { Badge, Button, ConfirmDialog, EmptyState, ErrorState, Modal, Pagination, Select, Skeleton, Tabs, Textarea, useToast } from "../../components/ui";
import { ActivityForm, isoParaDatetimeLocal, tipoLabel, type ActivityFormValues } from "../../components/crm/ActivityForm";
import { LeadPicker } from "../../components/crm/LeadPicker";

const abas = [
  { chave: VisaoAtividade.Minhas, rotulo: "Minhas" },
  { chave: VisaoAtividade.Hoje, rotulo: "Hoje" },
  { chave: VisaoAtividade.Proximas, rotulo: "Próximas" },
  { chave: VisaoAtividade.Atrasadas, rotulo: "Atrasadas" },
  { chave: VisaoAtividade.Concluidas, rotulo: "Concluídas" },
];

function paraFormValues(a: Activity): ActivityFormValues {
  return {
    tipo: a.tipo,
    assunto: a.assunto,
    descricao: a.descricao ?? "",
    dataHoraPrevista: isoParaDatetimeLocal(a.dataHoraPrevista),
    lembreteMinutosAntes: a.lembreteMinutosAntes != null ? String(a.lembreteMinutosAntes) : "",
  };
}

export function ActivitiesPage() {
  const { notificar } = useToast();
  const { temPapel } = useAuth();
  const podeGerir = temPapel("Admin", "GestorMaster", "GestorComercial");

  const [visao, setVisao] = useState<VisaoAtividade>(VisaoAtividade.Minhas);
  const [tipo, setTipo] = useState("");
  const [responsavelId, setResponsavelId] = useState("");
  const [leadFiltro, setLeadFiltro] = useState<LeadListItem | null>(null);
  const [vendedores, setVendedores] = useState<VendedorResumo[]>([]);
  const [modalLead, setModalLead] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<PagedResult<Activity> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [modalNova, setModalNova] = useState(false);
  const [leadSelecionado, setLeadSelecionado] = useState<LeadListItem | null>(null);
  const [atividadeEditando, setAtividadeEditando] = useState<Activity | null>(null);
  const [atividadeConcluir, setAtividadeConcluir] = useState<Activity | null>(null);
  const [atividadeExcluindo, setAtividadeExcluindo] = useState<Activity | null>(null);
  const [resultado, setResultado] = useState("");
  const [salvando, setSalvando] = useState(false);
  const [excluindo, setExcluindo] = useState(false);

  useEffect(() => {
    if (!podeGerir) return;
    api.get<VendedorResumo[]>("/crm/management/vendedores").then(setVendedores).catch(() => setVendedores([]));
  }, [podeGerir]);

  const filtro = useMemo(
    () => ({
      visao,
      tipo: tipo || undefined,
      responsavelId: responsavelId || undefined,
      leadId: leadFiltro?.id,
      pagina,
      tamanhoPagina: 20,
    }),
    [visao, tipo, responsavelId, leadFiltro, pagina]
  );

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<Activity>>(`/crm/activities${toQueryString(filtro)}`, signal)
        .then(setDados)
        .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar as atividades."); })
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [filtro]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  function limparFiltros() {
    setTipo("");
    setResponsavelId("");
    setLeadFiltro(null);
    setPagina(1);
  }

  function fecharModalNova() {
    setModalNova(false);
    setLeadSelecionado(null);
  }

  async function criarAtividade(valores: ActivityFormValues) {
    if (!leadSelecionado) return;
    setSalvando(true);
    try {
      const request: ActivityCreateRequest = {
        leadId: leadSelecionado.id,
        tipo: valores.tipo,
        assunto: valores.assunto,
        descricao: valores.descricao || null,
        dataHoraPrevista: new Date(valores.dataHoraPrevista).toISOString(),
        lembreteMinutosAntes: valores.lembreteMinutosAntes ? Number(valores.lembreteMinutosAntes) : null,
      };
      await api.post("/crm/activities", request);
      notificar("success", "Atividade agendada com sucesso.");
      fecharModalNova();
      setRecarregar((n) => n + 1);
    } catch {
      notificar("error", "Não foi possível agendar a atividade.");
    } finally {
      setSalvando(false);
    }
  }

  async function salvarEdicao(valores: ActivityFormValues) {
    if (!atividadeEditando) return;
    setSalvando(true);
    try {
      const request: ActivityUpdateRequest = {
        tipo: valores.tipo,
        assunto: valores.assunto,
        descricao: valores.descricao || null,
        dataHoraPrevista: new Date(valores.dataHoraPrevista).toISOString(),
        lembreteMinutosAntes: valores.lembreteMinutosAntes ? Number(valores.lembreteMinutosAntes) : null,
        rowVersion: atividadeEditando.rowVersion,
      };
      await api.put(`/crm/activities/${atividadeEditando.id}`, request);
      notificar("success", "Atividade atualizada com sucesso.");
      setAtividadeEditando(null);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível salvar as alterações.");
    } finally {
      setSalvando(false);
    }
  }

  async function concluirAtividade() {
    if (!atividadeConcluir) return;
    setSalvando(true);
    try {
      await api.post(`/crm/activities/${atividadeConcluir.id}/complete`, {
        resultado: resultado || null,
        dataHoraConclusao: new Date().toISOString(),
        rowVersion: atividadeConcluir.rowVersion,
      });
      notificar("success", "Atividade concluída.");
      setAtividadeConcluir(null);
      setResultado("");
      setRecarregar((n) => n + 1);
    } catch {
      notificar("error", "Não foi possível concluir a atividade.");
    } finally {
      setSalvando(false);
    }
  }

  async function excluirAtividade() {
    if (!atividadeExcluindo) return;
    setExcluindo(true);
    try {
      await api.del(`/crm/activities/${atividadeExcluindo.id}`);
      notificar("success", "Atividade excluída com sucesso.");
      setAtividadeExcluindo(null);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível excluir a atividade.");
    } finally {
      setExcluindo(false);
    }
  }

  const filtrosAtivos = !!(tipo || responsavelId || leadFiltro);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Atividades</h1>
          <p className="text-sm text-[var(--fg-muted)]">Ligações, reuniões, retornos e tarefas comerciais.</p>
        </div>
        <Button onClick={() => setModalNova(true)}>
          <Plus className="size-4" /> Nova atividade
        </Button>
      </div>

      <Tabs
        tabs={abas.map((a) => ({ chave: String(a.chave), rotulo: a.rotulo }))}
        ativa={String(visao)}
        onChange={(chave) => {
          setVisao(Number(chave) as VisaoAtividade);
          setPagina(1);
        }}
      />

      <div className="flex flex-wrap items-end gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
        <div className="w-44">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Tipo</label>
          <Select value={tipo} onChange={(e) => { setTipo(e.target.value); setPagina(1); }}>
            <option value="">Todos</option>
            {Object.entries(tipoLabel).map(([valor, rotulo]) => (
              <option key={valor} value={valor}>
                {rotulo}
              </option>
            ))}
          </Select>
        </div>
        {podeGerir && (
          <div className="w-48">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Responsável</label>
            <Select value={responsavelId} onChange={(e) => { setResponsavelId(e.target.value); setPagina(1); }}>
              <option value="">Todos</option>
              {vendedores.map((v) => (
                <option key={v.id} value={v.id}>
                  {v.nome}
                </option>
              ))}
            </Select>
          </div>
        )}
        <div>
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Lead</label>
          {leadFiltro ? (
            <Badge variant="brand">
              {leadFiltro.nomeOuRazaoSocial}
              <button type="button" onClick={() => { setLeadFiltro(null); setPagina(1); }} className="ml-1 align-middle">
                <X className="size-3" />
              </button>
            </Badge>
          ) : (
            <Button variant="secondary" size="sm" onClick={() => setModalLead(true)}>
              Filtrar por lead
            </Button>
          )}
        </div>
        {filtrosAtivos && (
          <Button variant="ghost" size="sm" onClick={limparFiltros}>
            <X className="size-4" /> Limpar filtros
          </Button>
        )}
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
        <EmptyState
          title="Nenhuma atividade encontrada"
          description={filtrosAtivos ? "Ajuste os filtros ou agende uma nova atividade." : "Agende uma nova atividade para acompanhar seus leads."}
        />
      ) : (
        <>
          <ul className="space-y-2">
            {dados.itens.map((atividade) => (
              <li key={atividade.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <Badge variant="brand">{tipoLabel[atividade.tipo]}</Badge>
                    {atividade.atrasada && <Badge variant="danger">Atrasada</Badge>}
                    {atividade.status === StatusAtividade.Concluida && <Badge variant="success">Concluída</Badge>}
                  </div>
                  <p className="mt-1 truncate font-medium text-[var(--fg)]">{atividade.assunto}</p>
                  <Link to={`/app/crm/leads/${atividade.leadId}`} className="text-xs text-[var(--fg-muted)] hover:text-[var(--brand)]">
                    {atividade.leadNome}
                  </Link>
                </div>
                <div className="text-right text-xs text-[var(--fg-muted)]">{formatarDataHora(atividade.dataHoraPrevista)}</div>
                <div className="flex items-center gap-2">
                  {atividade.status === StatusAtividade.Pendente && (
                    <Button size="sm" variant="secondary" onClick={() => setAtividadeConcluir(atividade)}>
                      Concluir
                    </Button>
                  )}
                  <Button size="sm" variant="secondary" onClick={() => setAtividadeEditando(atividade)}>
                    <Pencil className="size-4" />
                  </Button>
                  <Button size="sm" variant="danger" onClick={() => setAtividadeExcluindo(atividade)}>
                    <Trash2 className="size-4" />
                  </Button>
                </div>
              </li>
            ))}
          </ul>
          <Pagination pagina={dados.pagina} totalPaginas={dados.totalPaginas} onChange={setPagina} />
        </>
      )}

      <Modal open={modalNova} onClose={fecharModalNova} title="Nova atividade">
        {!leadSelecionado ? (
          <LeadPicker onSelecionar={setLeadSelecionado} />
        ) : (
          <div>
            <p className="mb-3 text-sm text-[var(--fg-muted)]">
              Lead: <strong className="text-[var(--fg)]">{leadSelecionado.nomeOuRazaoSocial}</strong>
            </p>
            <ActivityForm salvando={salvando} onSubmit={criarAtividade} onCancel={fecharModalNova} />
          </div>
        )}
      </Modal>

      <Modal open={!!atividadeEditando} onClose={() => setAtividadeEditando(null)} title="Editar atividade">
        {atividadeEditando && (
          <div>
            <p className="mb-3 text-sm text-[var(--fg-muted)]">
              Lead: <strong className="text-[var(--fg)]">{atividadeEditando.leadNome}</strong>
            </p>
            <ActivityForm
              modoEdicao
              valoresIniciais={paraFormValues(atividadeEditando)}
              salvando={salvando}
              onSubmit={salvarEdicao}
              onCancel={() => setAtividadeEditando(null)}
            />
          </div>
        )}
      </Modal>

      <Modal open={!!atividadeConcluir} onClose={() => setAtividadeConcluir(null)} title="Concluir atividade">
        <div className="space-y-4">
          <p className="text-sm text-[var(--fg-muted)]">{atividadeConcluir?.assunto}</p>
          <Textarea placeholder="Resultado (opcional)" value={resultado} onChange={(e) => setResultado(e.target.value)} />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setAtividadeConcluir(null)} disabled={salvando}>
              Cancelar
            </Button>
            <Button onClick={concluirAtividade} loading={salvando}>
              Concluir
            </Button>
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        open={!!atividadeExcluindo}
        title="Excluir atividade"
        message={
          <>
            Tem certeza que deseja excluir a atividade <strong className="text-[var(--fg)]">{atividadeExcluindo?.assunto}</strong>? Essa ação não
            pode ser desfeita.
          </>
        }
        confirmLabel="Excluir"
        danger
        loading={excluindo}
        onConfirm={excluirAtividade}
        onCancel={() => setAtividadeExcluindo(null)}
      />

      <Modal open={modalLead} onClose={() => setModalLead(false)} title="Filtrar por lead" size="sm">
        <LeadPicker
          onSelecionar={(lead) => {
            setLeadFiltro(lead);
            setPagina(1);
            setModalLead(false);
          }}
        />
      </Modal>
    </div>
  );
}
