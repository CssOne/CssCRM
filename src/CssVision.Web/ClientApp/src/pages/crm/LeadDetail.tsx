import { ArrowLeft, Calendar, Car, Handshake, IdCard, Mail, MapPin, Pencil, Percent, Phone, Plus, ShieldCheck, Trash2, Wallet } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api, ApiRequestError, isAbortError } from "../../lib/api";
import { formatarData, formatarDocumento, formatarMoeda, formatarTelefone } from "../../lib/format";
import {
  TipoEtapaPipeline,
  TipoPessoa,
  type ActivityCreateRequest,
  type LeadDetail,
  type LeadDuplicateWarning,
  type LeadTimelineItem,
  type Opportunity,
} from "../../lib/types";
import { Badge, Button, Card, ErrorState, Modal, Skeleton, Textarea, useToast } from "../../components/ui";
import { useAuth } from "../../context/AuthContext";
import { LeadForm, type LeadFormValues } from "../../components/crm/LeadForm";
import { ActivityForm, type ActivityFormValues } from "../../components/crm/ActivityForm";
import { VendaConcluidaDialog } from "../../components/crm/VendaConcluidaDialog";
import { Timeline } from "../../components/crm/Timeline";

const ETAPA_VENDA_CONCLUIDA = "Venda concluída";

function paraFormValues(lead: LeadDetail): LeadFormValues {
  return {
    nomeOuRazaoSocial: lead.nomeOuRazaoSocial,
    tipoPessoa: lead.tipoPessoa,
    documento: lead.documento ?? "",
    telefone: lead.telefone ?? "",
    telefone2: lead.telefone2 ?? "",
    whatsApp: lead.whatsApp ?? "",
    email: lead.email ?? "",
    cidade: lead.cidade ?? "",
    estado: lead.estado ?? "",
    regional: lead.regional ?? "",
    origem: lead.origem ?? "",
    campanha: lead.campanha ?? "",
    produtoInteresse: lead.produtoInteresse ?? "",
    placa: lead.placa ?? "",
    temSeguro: lead.temSeguro === true ? "sim" : lead.temSeguro === false ? "nao" : "",
    utilidadeVeiculo: lead.utilidadeVeiculo ?? "",
    gclid: lead.gclid ?? "",
    utmMedium: lead.utmMedium ?? "",
    utmSource: lead.utmSource ?? "",
    utmTerm: lead.utmTerm ?? "",
    metaClickId: lead.metaClickId ?? "",
    metaFormId: lead.metaFormId ?? "",
    metaLeadId: lead.metaLeadId ?? "",
    indicadoPorLeadId: lead.indicadoPorLeadId ?? "",
    indicadoPorLeadNome: lead.indicadoPorLeadNome ?? "",
    tipoIndicacao: lead.tipoIndicacao ?? "",
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
  const [oportunidadeEditando, setOportunidadeEditando] = useState<Opportunity | null>(null);
  const [carregandoOportunidade, setCarregandoOportunidade] = useState(false);
  // Excluir lead é só para Admin/GestorMaster (ver LeadService.ExcluirAsync no back-end).
  const { temPapel } = useAuth();
  const podeExcluir = temPapel("Admin", "GestorMaster");
  // Origem do lead: só administradores veem (em forma de tag) — o servidor nem a envia aos demais.
  const podeVerOrigem = temPapel("Admin", "GestorMaster");
  const [modalExcluir, setModalExcluir] = useState(false);
  const [excluindo, setExcluindo] = useState(false);

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
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
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
        telefone2: valores.telefone2 || null,
        whatsApp: valores.whatsApp || null,
        email: valores.email || null,
        cidade: valores.cidade || null,
        estado: valores.estado || null,
        regional: valores.regional || null,
        origem: valores.origem || null,
        campanha: valores.campanha || null,
        produtoInteresse: valores.produtoInteresse || null,
        placa: valores.placa || null,
        temSeguro: valores.temSeguro === "sim" ? true : valores.temSeguro === "nao" ? false : null,
        utilidadeVeiculo: valores.utilidadeVeiculo || null,
        gclid: valores.gclid || null,
        utmMedium: valores.utmMedium || null,
        utmSource: valores.utmSource || null,
        utmTerm: valores.utmTerm || null,
        metaClickId: valores.metaClickId || null,
        metaFormId: valores.metaFormId || null,
        metaLeadId: valores.metaLeadId || null,
        indicadoPorLeadId: valores.indicadoPorLeadId || null,
        tipoIndicacao: valores.tipoIndicacao || null,
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

  async function editarOportunidade(id: string) {
    setCarregandoOportunidade(true);
    try {
      const oportunidade = await api.get<Opportunity>(`/crm/opportunities/${id}`);
      setOportunidadeEditando(oportunidade);
    } catch {
      notificar("error", "Não foi possível carregar os dados da venda.");
    } finally {
      setCarregandoOportunidade(false);
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

  async function excluirLead() {
    if (!lead) return;
    setExcluindo(true);
    try {
      await api.del(`/crm/leads/${lead.id}`);
      notificar("success", "Lead excluído.");
      navigate("/app/crm/leads/kanban");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível excluir o lead.");
    } finally {
      setExcluindo(false);
      setModalExcluir(false);
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
      <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>
        <ArrowLeft className="size-4" /> Voltar
      </Button>

      <Card className="p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="text-xl font-semibold text-[var(--fg)]">{lead.nomeOuRazaoSocial}</h1>
            <p className="text-sm text-[var(--fg-muted)]">
              {lead.tipoPessoa === TipoPessoa.Fisica ? "Pessoa física" : "Pessoa jurídica"} · {formatarDocumento(lead.documento)}
            </p>
            <div className="mt-2 flex flex-wrap gap-1">
              {lead.produtoInteresse && (
                <Badge variant="info">
                  <span title="O que?">{lead.produtoInteresse}</span>
                </Badge>
              )}
              {podeVerOrigem && lead.origem && (
                <Badge variant="neutral">
                  <span title="Origem do lead">Origem: {lead.origem}</span>
                </Badge>
              )}
              {lead.tags.map((tag) => (
                <Badge key={tag} variant="brand">
                  {tag}
                </Badge>
              ))}
            </div>
          </div>
          <div className="flex shrink-0 gap-2">
            {podeExcluir && (
              <Button variant="secondary" onClick={() => setModalExcluir(true)}>
                <Trash2 className="size-4" /> Excluir
              </Button>
            )}
            <Button variant="secondary" onClick={() => setModalEditar(true)}>
              <Pencil className="size-4" /> Editar
            </Button>
          </div>
        </div>

        <div className="mt-4 grid gap-3 border-t border-[var(--border)] pt-4 sm:grid-cols-2 lg:grid-cols-4">
          <InfoItem icone={Phone} label="Telefone" valor={formatarTelefone(lead.telefone)} />
          {lead.telefone2 && <InfoItem icone={Phone} label="Telefone 2" valor={formatarTelefone(lead.telefone2)} />}
          <InfoItem icone={Mail} label="E-mail" valor={lead.email || "-"} />
          <InfoItem icone={MapPin} label="Local" valor={[lead.cidade, lead.estado].filter(Boolean).join(" - ") || "-"} />
          <InfoItem icone={Handshake} label="Responsável" valor={lead.responsavelNome ?? "Sem responsável"} />
          {(lead.placa || lead.temSeguro !== null || lead.utilidadeVeiculo) && (
            <>
              <InfoItem icone={IdCard} label="Placa" valor={lead.placa || "-"} />
              <InfoItem
                icone={ShieldCheck}
                label="Tem seguro?"
                valor={lead.temSeguro === true ? "Sim" : lead.temSeguro === false ? "Não" : "Não informado"}
              />
              <InfoItem icone={Car} label="Utilidade do veículo" valor={lead.utilidadeVeiculo || "-"} />
            </>
          )}
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
                {lead.oportunidades.map((op) => {
                  const temDadosVenda =
                    op.cpf || op.estado || op.ativoEm || op.porcentagem != null || op.mensalidade != null ||
                    op.total != null || op.veiculo?.descricao || op.veiculo?.placa;
                  return (
                    <li key={op.id} className="rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-sm">
                      <div className="flex items-center justify-between">
                        <div>
                          <p className="font-medium text-[var(--fg)]">{op.titulo}</p>
                          <p className="text-xs text-[var(--fg-muted)]">
                            {op.etapaNome} {op.dataPrevistaFechamento && `· previsão ${formatarData(op.dataPrevistaFechamento)}`}
                          </p>
                          <div className="mt-1 flex flex-wrap gap-1">
                            {op.migracao && <Badge variant="neutral">Migração</Badge>}
                            {op.indicacao && <Badge variant="brand">Indicação</Badge>}
                          </div>
                        </div>
                        <div className="flex items-center gap-2">
                          {op.etapaTipo === TipoEtapaPipeline.Ganho && (
                            <Button size="sm" variant="ghost" loading={carregandoOportunidade} onClick={() => editarOportunidade(op.id)}>
                              <Pencil className="size-3.5" /> Editar
                            </Button>
                          )}
                          <Badge variant={op.ativa ? "brand" : "neutral"}>{formatarMoeda(op.valorEstimado)}</Badge>
                        </div>
                      </div>

                      {temDadosVenda && (
                        <div className="mt-3 grid gap-3 border-t border-[var(--border)] pt-3 sm:grid-cols-2 lg:grid-cols-3">
                          {op.cpf && <InfoItem icone={IdCard} label="CPF" valor={formatarDocumento(op.cpf)} />}
                          {op.estado && <InfoItem icone={MapPin} label="Estado" valor={op.estado} />}
                          {op.ativoEm && <InfoItem icone={Calendar} label="Ativo em" valor={formatarData(op.ativoEm)} />}
                          {op.porcentagem != null && <InfoItem icone={Percent} label="Porcentagem" valor={`${op.porcentagem}%`} />}
                          {op.mensalidade != null && <InfoItem icone={Wallet} label="Mensalidade" valor={formatarMoeda(op.mensalidade)} />}
                          {op.mensalidadeComDesconto != null && (
                            <InfoItem icone={Wallet} label="Mensalidade com desconto" valor={formatarMoeda(op.mensalidadeComDesconto)} />
                          )}
                          {op.mensalidadeComCupom != null && (
                            <InfoItem icone={Wallet} label="Mensalidade com cupom" valor={formatarMoeda(op.mensalidadeComCupom)} />
                          )}
                          {op.pagamentoAdesao != null && <InfoItem icone={Wallet} label="Pagamento de adesão" valor={formatarMoeda(op.pagamentoAdesao)} />}
                          {op.total != null && <InfoItem icone={Wallet} label="Total" valor={formatarMoeda(op.total)} />}
                          {op.indicacao && op.tipoIndicacao && <InfoItem icone={Handshake} label="Tipo de indicação" valor={op.tipoIndicacao} />}
                          {op.indicacao && op.valorIndicacao != null && (
                            <InfoItem icone={Wallet} label="Valor da indicação" valor={formatarMoeda(op.valorIndicacao)} />
                          )}
                          {op.veiculo?.descricao && <InfoItem icone={Car} label="Veículo" valor={op.veiculo.descricao} />}
                          {op.veiculo?.placa && <InfoItem icone={Car} label="Placa" valor={op.veiculo.placa} />}
                          {op.veiculo?.fipe != null && <InfoItem icone={Wallet} label="Valor FIPE" valor={formatarMoeda(op.veiculo.fipe)} />}
                          {op.veiculo?.rastreador != null && <InfoItem icone={Wallet} label="Custo do rastreador" valor={formatarMoeda(op.veiculo.rastreador)} />}
                          {op.veiculo?.valorVistoria != null && (
                            <InfoItem icone={Wallet} label="Custo da vistoria" valor={formatarMoeda(op.veiculo.valorVistoria)} />
                          )}
                          {op.veiculo?.dataChegada && <InfoItem icone={Calendar} label="Chegada do veículo" valor={formatarData(op.veiculo.dataChegada)} />}
                        </div>
                      )}
                    </li>
                  );
                })}
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

      {/* Nova oportunidade: mesmo formulário da Venda concluída, só o nome do cliente é obrigatório. */}
      <VendaConcluidaDialog
        open={modalOportunidade}
        modo="oportunidade"
        leadId={lead.id}
        etapaNome=""
        valorEstimado={0}
        onCancel={() => setModalOportunidade(false)}
        onConcluido={() => {
          setModalOportunidade(false);
          notificar("success", "Oportunidade criada com sucesso.");
          carregar();
        }}
      />

      <Modal open={modalAtividade} onClose={() => setModalAtividade(false)} title="Agendar atividade">
        <ActivityForm salvando={salvando} onSubmit={criarAtividade} onCancel={() => setModalAtividade(false)} />
      </Modal>

      <VendaConcluidaDialog
        open={!!oportunidadeEditando}
        leadId={lead.id}
        etapaNome={ETAPA_VENDA_CONCLUIDA}
        valorEstimado={oportunidadeEditando?.valorEstimado ?? 0}
        oportunidadeEditar={oportunidadeEditando}
        onCancel={() => setOportunidadeEditando(null)}
        onConcluido={() => {
          setOportunidadeEditando(null);
          notificar("success", "Venda atualizada com sucesso.");
          carregar();
        }}
      />

      <Modal open={modalExcluir} onClose={() => setModalExcluir(false)} title="Excluir lead" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-[var(--fg)]">
            Tem certeza que deseja excluir <strong>{lead.nomeOuRazaoSocial}</strong>? O lead sai do quadro de leads, as
            oportunidades dele saem do Pipeline e ele não pode ser recuperado por aqui.
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setModalExcluir(false)} disabled={excluindo}>
              Cancelar
            </Button>
            <Button variant="danger" loading={excluindo} onClick={excluirLead}>
              <Trash2 className="size-4" /> Excluir
            </Button>
          </div>
        </div>
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
