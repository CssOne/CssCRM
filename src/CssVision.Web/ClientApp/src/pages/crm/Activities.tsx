import { Plus } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, isAbortError, toQueryString } from "../../lib/api";
import { formatarDataHora } from "../../lib/format";
import { StatusAtividade, VisaoAtividade, type Activity, type ActivityCreateRequest, type LeadListItem, type PagedResult } from "../../lib/types";
import { Badge, Button, EmptyState, ErrorState, Modal, Pagination, Skeleton, Tabs, Textarea, useToast } from "../../components/ui";
import { ActivityForm, tipoLabel, type ActivityFormValues } from "../../components/crm/ActivityForm";
import { LeadPicker } from "../../components/crm/LeadPicker";

const abas = [
  { chave: VisaoAtividade.Minhas, rotulo: "Minhas" },
  { chave: VisaoAtividade.Hoje, rotulo: "Hoje" },
  { chave: VisaoAtividade.Proximas, rotulo: "Próximas" },
  { chave: VisaoAtividade.Atrasadas, rotulo: "Atrasadas" },
  { chave: VisaoAtividade.Concluidas, rotulo: "Concluídas" },
];

export function ActivitiesPage() {
  const { notificar } = useToast();
  const [visao, setVisao] = useState<VisaoAtividade>(VisaoAtividade.Minhas);
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<PagedResult<Activity> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const [modalNova, setModalNova] = useState(false);
  const [leadSelecionado, setLeadSelecionado] = useState<LeadListItem | null>(null);
  const [atividadeConcluir, setAtividadeConcluir] = useState<Activity | null>(null);
  const [resultado, setResultado] = useState("");
  const [salvando, setSalvando] = useState(false);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<Activity>>(`/crm/activities${toQueryString({ visao, pagina, tamanhoPagina: 20 })}`, signal)
        .then(setDados)
        .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar as atividades."); })
        .finally(() => setCarregando(false));
    },
    [visao, pagina]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

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

      {carregando ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-16" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados || dados.itens.length === 0 ? (
        <EmptyState title="Nenhuma atividade encontrada" description="Agende uma nova atividade para acompanhar seus leads." />
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
                {atividade.status === StatusAtividade.Pendente && (
                  <Button size="sm" variant="secondary" onClick={() => setAtividadeConcluir(atividade)}>
                    Concluir
                  </Button>
                )}
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
    </div>
  );
}
