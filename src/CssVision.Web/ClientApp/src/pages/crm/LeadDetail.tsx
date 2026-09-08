import { Handshake, Mail, MapPin, Pencil, Phone, Plus } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api, ApiRequestError, isAbortError } from "../../lib/api";
import { formatarData, formatarDocumento, formatarMoeda, formatarTelefone } from "../../lib/format";
import {
  TipoPessoa,
  type ActivityCreateRequest,
  type LeadDetail,
  type LeadDuplicateWarning,
  type LeadTimelineItem,
  type OpportunityCreateRequest,
} from "../../lib/types";
import { Badge, Button, Card, ErrorState, Modal, Skeleton, Textarea, useToast } from "../../components/ui";
import { LeadForm, type LeadFormValues } from "../../components/crm/LeadForm";
import { OpportunityForm, type OpportunityFormValues } from "../../components/crm/OpportunityForm";
import { ActivityForm, type ActivityFormValues } from "../../components/crm/ActivityForm";
import { Timeline } from "../../components/crm/Timeline";

function paraFormValues(lead: LeadDetail): LeadFormValues {
  return {
    nomeOuRazaoSocial: lead.nomeOuRazaoSocial,
    tipoPessoa: lead.tipoPessoa,
    documento: lead.documento ?? "",
    telefone: lead.telefone ?? "",
    whatsApp: lead.whatsApp ?? "",
    email: lead.email ?? "",
    cidade: lead.cidade ?? "",
    estado: lead.estado ?? "",
    regional: lead.regional ?? "",
    origem: lead.origem ?? "",
    campanha: lead.campanha ?? "",
    produtoInteresse: lead.produtoInteresse ?? "",
    tags: lead.tags.join(", "),
    observacoes: lead.observacoes ?? "",
    consentimentoContato: lead.consentimentoContato,
  };
}

export function LeadDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { notificar } = useToast();

  const [lead, setLead] = useState<LeadDetail | null>(null);
  const [timeline, setTimeline] = useState<LeadTimelineItem[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);

  const [modalEditar, setModalEditar] = useState(false);
  const [modalOportunidade, setModalOportunidade] = useState(false);
  const [modalAtividade, setModalAtividade] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [nota, setNota] = useState("");
  const [duplicidade, setDuplicidade] = useState<LeadDuplicateWarning | null>(null);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      if (!id) return;
      setCarregando(true);
      setErro(null);
      Promise.all([api.get<LeadDetail>(`/crm/leads/${id}`, signal), api.get<LeadTimelineItem[]>(`/crm/leads/${id}/timeline`, signal)])
        .then(([leadRes, timelineRes]) => {
          setLead(leadRes);
          setTimeline(timelineRes);
        })
        .catch((e) => {
          if (isAbortError(e)) return;
          if (e instanceof ApiRequestError && e.status === 403) setErro("Você não tem permissão para acessar este lead.");
          else setErro("Não foi possível carregar o lead.");
        })
        .finally(() => setCarregando(false));
    },
    [id]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  async function salvarEdicao(valores: LeadFormValues) {
    if (!lead) return;
    setSalvando(true);
    setDuplicidade(null);
    try {
      const atualizado = await api.put<LeadDetail>(`/crm/leads/${lead.id}`, {
        nomeOuRazaoSocial: valores.nomeOuRazaoSocial,
        tipoPessoa: valores.tipoPessoa,
        documento: valores.documento || null,
        telefone: valores.telefone || null,
        whatsApp: valores.whatsApp || null,
        email: valores.email || null,
        cidade: valores.cidade || null,
        estado: valores.estado || null,
        regional: valores.regional || null,
        origem: valores.origem || null,
        campanha: valores.campanha || null,
        produtoInteresse: valores.produtoInteresse || null,
        tags: valores.tags ? valores.tags.split(",").map((t) => t.trim()).filter(Boolean) : [],
        observacoes: valores.observacoes || null,
        consentimentoContato: valores.consentimentoContato,
        rowVersion: lead.rowVersion,
      });
      setLead(atualizado);
      setModalEditar(false);
      notificar("success", "Lead atualizado com sucesso.");
    } catch (e) {
      if (e instanceof ApiRequestError && e.codigo === "duplicidade") {
        setDuplicidade(e.detalhes as LeadDuplicateWarning);
      } else if (e instanceof ApiRequestError && e.codigo === "concurrency") {
        notificar("error", "Este lead foi alterado por outro usuário. Recarregando dados atuais.");
        carregar();
      } else {
        notificar("error", "Não foi possível salvar as alterações.");
      }
    } finally {
      setSalvando(false);
    }
  }

  async function criarOportunidade(valores: OpportunityFormValues) {
    if (!lead) return;
    setSalvando(true);
    try {
      const request: OpportunityCreateRequest = {
        leadId: lead.id,
        titulo: valores.titulo,
        responsavelId: lead.responsavelId!,
        produtoOuServico: valores.produtoOuServico || null,
        valorEstimado: Number(valores.valorEstimado) || 0,
        probabilidadeFechamento: valores.probabilidadeFechamento ? Number(valores.probabilidadeFechamento) : null,
        dataPrevistaFechamento: valores.dataPrevistaFechamento || null,
        concorrente: valores.concorrente || null,
        observacoes: valores.observacoes || null,
      };
      await api.post("/crm/opportunities", request);
      setModalOportunidade(false);
      notificar("success", "Oportunidade criada com sucesso.");
      carregar();
    } catch {
      notificar("error", "Não foi possível criar a oportunidade.");
    } finally {
      setSalvando(false);
    }
  }

  async function criarAtividade(valores: ActivityFormValues) {
    if (!lead) return;
    setSalvando(true);
    try {
      const request: ActivityCreateRequest = {
        leadId: lead.id,
        tipo: valores.tipo,
        assunto: valores.assunto,
        descricao: valores.descricao || null,
        dataHoraPrevista: new Date(valores.dataHoraPrevista).toISOString(),
        lembreteMinutosAntes: valores.lembreteMinutosAntes ? Number(valores.lembreteMinutosAntes) : null,
      };
      await api.post("/crm/activities", request);
      setModalAtividade(false);
      notificar("success", "Atividade agendada com sucesso.");
      carregar();
    } catch {
      notificar("error", "Não foi possível agendar a atividade.");
    } finally {
      setSalvando(false);
    }
  }

  async function adicionarNota() {
    if (!lead || !nota.trim()) return;
    setSalvando(true);
    try {
      await api.post(`/crm/leads/${lead.id}/notes`, { texto: nota });
      setNota("");
      notificar("success", "Anotação adicionada.");
      carregar();
    } catch {
      notificar("error", "Não foi possível salvar a anotação.");
    } finally {
      setSalvando(false);
    }
  }

  if (carregando) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-24" />
        <Skeleton className="h-64" />
      </div>
    );
  }

  if (erro || !lead) {
    return <ErrorState message={erro ?? "Lead não encontrado."} onRetry={() => navigate("/app/crm/leads")} />;
  }

  return (
    <div className="space-y-4">
      <Card className="p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="text-xl font-semibold text-[var(--fg)]">{lead.nomeOuRazaoSocial}</h1>
            <p className="text-sm text-[var(--fg-muted)]">
              {lead.tipoPessoa === TipoPessoa.Fisica ? "Pessoa física" : "Pessoa jurídica"} · {formatarDocumento(lead.documento)}
            </p>
            <div className="mt-2 flex flex-wrap gap-1">
              {lead.tags.map((tag) => (
                <Badge key={tag} variant="brand">
                  {tag}
                </Badge>
              ))}
            </div>
          </div>
          <Button variant="secondary" onClick={() => setModalEditar(true)}>
            <Pencil className="size-4" /> Editar
          </Button>
        </div>

        <div className="mt-4 grid gap-3 border-t border-[var(--border)] pt-4 sm:grid-cols-2 lg:grid-cols-4">
          <InfoItem icone={Phone} label="Telefone" valor={formatarTelefone(lead.telefone)} />
          <InfoItem icone={Mail} label="E-mail" valor={lead.email || "-"} />
          <InfoItem icone={MapPin} label="Local" valor={[lead.cidade, lead.estado].filter(Boolean).join(" - ") || "-"} />
          <InfoItem icone={Handshake} label="Responsável" valor={lead.responsavelNome ?? "Sem responsável"} />
        </div>
      </Card>

      <div className="grid gap-4 lg:grid-cols-3">
        <div className="space-y-4 lg:col-span-2">
          <Card className="p-5">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-sm font-semibold text-[var(--fg)]">Oportunidades</h2>
              <Button size="sm" variant="secondary" onClick={() => setModalOportunidade(true)}>
                <Plus className="size-4" /> Nova
              </Button>
            </div>
            {lead.oportunidades.length === 0 ? (
              <p className="text-sm text-[var(--fg-muted)]">Nenhuma oportunidade registrada.</p>
            ) : (
              <ul className="space-y-2">
                {lead.oportunidades.map((op) => (
                  <li key={op.id} className="flex items-center justify-between rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-sm">
                    <div>
                      <p className="font-medium text-[var(--fg)]">{op.titulo}</p>
                      <p className="text-xs text-[var(--fg-muted)]">
                        {op.etapaNome} {op.dataPrevistaFechamento && `· previsão ${formatarData(op.dataPrevistaFechamento)}`}
                      </p>
                    </div>
                    <Badge variant={op.ativa ? "brand" : "neutral"}>{formatarMoeda(op.valorEstimado)}</Badge>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card className="p-5">
            <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Linha do tempo</h2>
            <Timeline itens={timeline} />
          </Card>
        </div>

        <div className="space-y-4">
          <Card className="p-5">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-sm font-semibold text-[var(--fg)]">Próximo contato</h2>
              <Button size="sm" variant="secondary" onClick={() => setModalAtividade(true)}>
                <Plus className="size-4" /> Agendar
              </Button>
            </div>
            <p className="text-sm text-[var(--fg-muted)]">
              {lead.consentimentoContato ? "Consentimento de contato registrado (LGPD)." : "Sem consentimento de contato registrado."}
            </p>
          </Card>

          <Card className="p-5">
            <h2 className="mb-3 text-sm font-semibold text-[var(--fg)]">Nova anotação</h2>
            <Textarea value={nota} onChange={(e) => setNota(e.target.value)} placeholder="Escreva uma anotação sobre o lead..." />
            <div className="mt-2 flex justify-end">
              <Button size="sm" onClick={adicionarNota} loading={salvando} disabled={!nota.trim()}>
                Salvar anotação
              </Button>
            </div>
          </Card>
        </div>
      </div>

      <Modal open={modalEditar} onClose={() => { setModalEditar(false); setDuplicidade(null); }} title="Editar lead" size="lg">
        {duplicidade && (
          <p className="mb-4 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-sm text-[var(--warning)]">
            Já existe outro lead com o mesmo {duplicidade.campoDuplicado}: {duplicidade.nomeExistente}.
          </p>
        )}
        <LeadForm valoresIniciais={paraFormValues(lead)} salvando={salvando} onSubmit={salvarEdicao} onCancel={() => setModalEditar(false)} idPrefix="editar" />
      </Modal>

      <Modal open={modalOportunidade} onClose={() => setModalOportunidade(false)} title="Nova oportunidade">
        <OpportunityForm salvando={salvando} onSubmit={criarOportunidade} onCancel={() => setModalOportunidade(false)} />
      </Modal>

      <Modal open={modalAtividade} onClose={() => setModalAtividade(false)} title="Agendar atividade">
        <ActivityForm salvando={salvando} onSubmit={criarAtividade} onCancel={() => setModalAtividade(false)} />
      </Modal>
    </div>
  );
}

function InfoItem({ icone: Icone, label, valor }: { icone: typeof Phone; label: string; valor: string }) {
  return (
    <div className="flex items-start gap-2">
      <Icone className="mt-0.5 size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />
      <div className="min-w-0">
        <p className="text-xs text-[var(--fg-muted)]">{label}</p>
        <p className="truncate text-sm text-[var(--fg)]">{valor}</p>
      </div>
    </div>
  );
}
