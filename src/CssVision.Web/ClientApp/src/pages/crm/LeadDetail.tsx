import { ArrowLeft, Calendar, CalendarClock, Check, Car, FileText, Handshake, IdCard, Mail, MapPin, MessagesSquare, Pencil, Percent, Phone, Plus, ShieldCheck, Trash2, Wallet } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { api, ApiRequestError, isAbortError } from "../../lib/api";
import { formatarData, formatarDataHora, formatarDocumento, formatarMoeda, formatarTelefone } from "../../lib/format";
import {
  TipoEtapaPipeline,
  TipoPessoa,
  type Activity,
  type ActivityCreateRequest,
  type PagedResult,
  TipoEventoTimeline,
  type LeadDetail,
  type LeadStage,
  type LeadDuplicateWarning,
  type LeadOpportunitySummary,
  type LeadTimelineItem,
  type Opportunity,
} from "../../lib/types";
import { Badge, Button, Card, ConfirmDialog, ErrorState, Modal, Skeleton, Textarea, useToast } from "../../components/ui";
import { useAuth } from "../../context/AuthContext";
import { ConversarSobreLeadDialog } from "../../components/crm/ConversarSobreLeadDialog";
import { LeadForm, type LeadFormValues } from "../../components/crm/LeadForm";
import { ActivityForm, COR_TIPO, tipoIcone, tipoLabel, type ActivityFormValues } from "../../components/crm/ActivityForm";
import { concluirAtividade } from "../../components/crm/NotificacoesAtividades";
import { VendaConcluidaDialog } from "../../components/crm/VendaConcluidaDialog";
import { EtapaDoLead } from "../../components/crm/EtapaDoLead";
import { AlterarResponsavelDialog } from "../../components/crm/AlterarResponsavelDialog";
import { Timeline } from "../../components/crm/Timeline";
import { BotaoWhatsApp } from "../../components/crm/BotaoWhatsApp";
import { useAbrirLead, useFecharPainelLead } from "../../lib/painelLead";

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
    veiculoNaoAtendido: lead.veiculoNaoAtendido ?? "",
    gclid: lead.gclid ?? "",
    utmMedium: lead.utmMedium ?? "",
    utmSource: lead.utmSource ?? "",
    utmCampaign: lead.utmCampaign ?? "",
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

/** Mesma regra do quadro de leads: tipo "Lead" é lead (tráfego, Notion); qualquer outro tipo, ou cadastro manual, é indicação. */
function ehIndicacao(lead: LeadDetail): boolean {
  const tipo = lead.tipoIndicacao?.trim().toLowerCase();
  return tipo !== "lead" && (lead.criadoManualmente || !!tipo);
}

export function LeadDetailPage() {
  const { id } = useParams<{ id: string }>();
  return id ? <LeadDetailConteudo key={id} leadId={id} /> : null;
}

/**
 * Conteúdo do lead — na página própria (/app/crm/leads/:id) ou no painel lateral (PainelLead,
 * `noPainel`): lá não há "Voltar", e excluir fecha o painel em vez de trocar de página.
 */
export function LeadDetailConteudo({ leadId: id, noPainel = false }: { leadId: string; noPainel?: boolean }) {
  const navigate = useNavigate();
  const abrirLead = useAbrirLead();
  const fecharPainel = useFecharPainelLead();
  const { notificar } = useToast();

  const [lead, setLead] = useState<LeadDetail | null>(null);
  const [coresEtapas, setCoresEtapas] = useState<LeadStage[]>([]);
  useEffect(() => {
    api.get<LeadStage[]>("/crm/settings/lead-stages").then(setCoresEtapas).catch(() => setCoresEtapas([]));
  }, []);
  const [timeline, setTimeline] = useState<LeadTimelineItem[]>([]);
  // Atividades ainda pendentes deste lead (atrasadas primeiro, depois as próximas).
  const [pendentes, setPendentes] = useState<Activity[]>([]);
  const [concluindoId, setConcluindoId] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);

  const [modalEditar, setModalEditar] = useState(false);
  const [modalOportunidade, setModalOportunidade] = useState(false);
  // "Ganho" na Nova oportunidade abre o formulário de venda concluída (a oportunidade já nasce ganha).
  const [modalVendaGanho, setModalVendaGanho] = useState(false);
  const [etapaGanhoId, setEtapaGanhoId] = useState<string | undefined>(undefined);
  useEffect(() => {
    api
      .get<{ id: string; tipo: number; ativa: boolean }[]>("/crm/settings/stages")
      .then((todas) => setEtapaGanhoId(todas.find((e) => e.ativa && e.tipo === 2)?.id))
      .catch(() => setEtapaGanhoId(undefined));
  }, []);
  const [modalAtividade, setModalAtividade] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [nota, setNota] = useState("");
  const [duplicidade, setDuplicidade] = useState<LeadDuplicateWarning | null>(null);
  const [oportunidadeEditando, setOportunidadeEditando] = useState<Opportunity | null>(null);
  const [carregandoOportunidade, setCarregandoOportunidade] = useState(false);
  // Excluir lead é só para Admin/GestorMaster (ver LeadService.ExcluirAsync no back-end).
  const { temPapel, sessao } = useAuth();
  // Admin/gestores excluem qualquer oportunidade que enxergam; consultores, só as próprias.
  const podeExcluirOportunidade = (op: LeadOpportunitySummary) =>
    temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial") || (!!sessao && op.responsavelId === sessao.id);
  const podeExcluir = temPapel("Admin", "GestorMaster", "SupervisorComercial");
  const podeTrocarResponsavel = temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial");
  const [trocandoResponsavel, setTrocandoResponsavel] = useState(false);
  // Origem do lead: só administradores veem (em forma de tag) — o servidor nem a envia aos demais.
  const podeVerOrigem = temPapel("Admin", "GestorMaster", "SupervisorComercial");
  const [modalExcluir, setModalExcluir] = useState(false);
  const [oportunidadeExcluindo, setOportunidadeExcluindo] = useState<LeadOpportunitySummary | null>(null);
  const [excluindoOportunidade, setExcluindoOportunidade] = useState(false);
  const [excluindo, setExcluindo] = useState(false);
  const [modalVeiculos, setModalVeiculos] = useState(false);
  const [modalConversar, setModalConversar] = useState(false);
  const [searchParams, setSearchParams] = useSearchParams();

  // Vindo do aviso "já existe um lead com o mesmo CPF" (?outroVeiculo=1): abre a venda do outro veículo.
  useEffect(() => {
    if (searchParams.get("outroVeiculo") !== "1") return;
    setModalVeiculos(true);
    setSearchParams(
      (atual) => {
        const novo = new URLSearchParams(atual);
        novo.delete("outroVeiculo");
        return novo;
      },
      { replace: true }
    );
  }, [searchParams, setSearchParams]);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      if (!id) return;
      setCarregando(true);
      setErro(null);
      Promise.all([
        api.get<LeadDetail>(`/crm/leads/${id}`, signal),
        api.get<LeadTimelineItem[]>(`/crm/leads/${id}/timeline`, signal),
        // Visões 4 (atrasadas) e 3 (próximas): só as pendentes deste lead. Se falhar, o cartão fica vazio, sem derrubar a página.
        Promise.all([
          api.get<PagedResult<Activity>>(`/crm/activities?visao=4&leadId=${id}&tamanhoPagina=5`, signal).then((r) => r.itens).catch(() => [] as Activity[]),
          api.get<PagedResult<Activity>>(`/crm/activities?visao=3&leadId=${id}&tamanhoPagina=5`, signal).then((r) => r.itens).catch(() => [] as Activity[]),
        ]),
      ])
        .then(([leadRes, timelineRes, [atrasadas, proximas]]) => {
          setLead(leadRes);
          setTimeline(timelineRes);
          const porData = (a: Activity, b: Activity) => a.dataHoraPrevista.localeCompare(b.dataHoraPrevista);
          setPendentes([...atrasadas.sort(porData), ...proximas.sort(porData)]);
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
        // Vazio limpa o campo (o servidor só ignora quando o campo nem é enviado).
        veiculoNaoAtendido: valores.veiculoNaoAtendido,
        gclid: valores.gclid || null,
        utmMedium: valores.utmMedium || null,
        utmSource: valores.utmSource || null,
        utmCampaign: valores.utmCampaign || null,
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
        notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível salvar as alterações.");
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

  async function concluirPendente(atividade: Activity) {
    setConcluindoId(atividade.id);
    try {
      await concluirAtividade(atividade);
      notificar("success", "Atividade concluída.");
      carregar();
    } catch {
      notificar("error", "Não foi possível concluir a atividade.");
    } finally {
      setConcluindoId(null);
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

  async function excluirOportunidade() {
    if (!oportunidadeExcluindo) return;
    setExcluindoOportunidade(true);
    try {
      await api.del(`/crm/opportunities/${oportunidadeExcluindo.id}`);
      notificar("success", "Oportunidade excluída.");
      setOportunidadeExcluindo(null);
      carregar();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível excluir a oportunidade.");
    } finally {
      setExcluindoOportunidade(false);
    }
  }

  async function excluirLead() {
    if (!lead) return;
    setExcluindo(true);
    try {
      await api.del(`/crm/leads/${lead.id}`);
      notificar("success", "Lead excluído.");
      if (noPainel) fecharPainel();
      else navigate("/app/crm/leads/kanban");
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

  const corEtapa = coresEtapas.find((e) => e.id === lead.etapaId)?.cor ?? "#64748b";
  // Anotações em destaque, logo abaixo do campo onde são escritas (também aparecem na linha do tempo).
  const anotacoes = timeline.filter((i) => i.tipo === TipoEventoTimeline.Nota).slice(0, 5);

  return (
    <div className="@container space-y-4">
      {!noPainel && (
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>
          <ArrowLeft className="size-4" /> Voltar
        </Button>
      )}

      <Card className="overflow-hidden !rounded-3xl p-0">
        <div
          aria-hidden
          className="h-16 transition-colors"
          style={{ backgroundImage: `linear-gradient(90deg, ${corEtapa}, color-mix(in srgb, ${corEtapa} 70%, white) 60%, color-mix(in srgb, ${corEtapa} 38%, white))` }}
        />
        <div className="px-5 pb-5">
        <div className="-mt-8 flex flex-wrap items-start justify-between gap-3">
          <div className="w-full min-w-0 @3xl:w-auto @3xl:flex-1">
            <div className="flex items-end gap-3.5">
              <span className="flex size-16 shrink-0 items-center justify-center rounded-2xl border-4 border-[var(--surface-solid)] text-xl font-bold shadow-[var(--shadow-card)]" style={{ color: corEtapa, backgroundColor: `color-mix(in srgb, ${corEtapa} 18%, var(--surface-solid))` }}>
                {lead.nomeOuRazaoSocial.split(/\s+/).filter(Boolean).slice(0, 2).map((x) => x[0]).join("").toUpperCase()}
              </span>
              <div className="min-w-0 pb-0.5 pt-9">
                <h1 className="truncate text-xl font-bold tracking-tight text-[var(--fg)]" title={lead.nomeOuRazaoSocial}>{lead.nomeOuRazaoSocial}</h1>
                <p className="text-sm text-[var(--fg-muted)]">
                  {lead.tipoPessoa === TipoPessoa.Fisica ? "Pessoa física" : "Pessoa jurídica"}
                  {lead.documento ? ` · ${formatarDocumento(lead.documento)}` : ""}
                </p>
              </div>
            </div>
            {lead.veiculoAdicionalDeLeadId && (
              <p className="mt-1 text-sm text-[var(--fg-muted)]">
                Veículo adicional do cliente —{" "}
                <Link
                  to={`/app/crm/leads/${lead.veiculoAdicionalDeLeadId}`}
                  onClick={(e) => {
                    if (noPainel && lead.veiculoAdicionalDeLeadId) {
                      e.preventDefault();
                      abrirLead(lead.veiculoAdicionalDeLeadId);
                    }
                  }}
                  className="font-medium text-[var(--brand)] hover:underline"
                >
                  ver card original{lead.veiculoAdicionalDeLeadNome ? ` (${lead.veiculoAdicionalDeLeadNome})` : ""}
                </Link>
              </p>
            )}
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
            <EtapaDoLead lead={lead} onMudou={() => carregar()} />
          </div>
          <div className="flex flex-wrap gap-2 @3xl:pt-9">
            {sessao?.menu.some((m) => m.chave === "chat") && (
              <Button variant="secondary" onClick={() => setModalConversar(true)} title="Publica este lead numa conversa do chat para a equipe conversar sobre ele">
                <MessagesSquare className="size-4" /> Conversar
              </Button>
            )}
            <Button variant="secondary" onClick={() => setModalVeiculos(true)} title="O cliente fechou outro veículo: registra a venda dele num card novo em Venda concluída">
              <Car className="size-4" /> Outro veículo
            </Button>
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

        <div className="mt-4 grid gap-2 border-t border-[var(--border)] pt-4 @md:grid-cols-2 @4xl:grid-cols-4">
          <InfoItem icone={Phone} label="Telefone" valor={formatarTelefone(lead.telefone)} href={lead.telefone ? `tel:${lead.telefone}` : undefined} whatsapp={lead.telefone} nome={lead.nomeOuRazaoSocial} />
          {lead.telefone2 && <InfoItem icone={Phone} label="Telefone 2" valor={formatarTelefone(lead.telefone2)} href={`tel:${lead.telefone2}`} whatsapp={lead.telefone2} nome={lead.nomeOuRazaoSocial} />}
          <InfoItem icone={Mail} label="E-mail" valor={lead.email || "-"} href={lead.email ? `mailto:${lead.email}` : undefined} />
          <InfoItem icone={MapPin} label="Local" valor={[lead.cidade, lead.estado].filter(Boolean).join(" - ") || "-"} />
          <div className="flex items-start gap-2">
            <InfoItem icone={Handshake} label="Responsável" valor={lead.responsavelNome ?? "Sem responsável"} />
            {podeTrocarResponsavel && (
              <button
                type="button"
                onClick={() => setTrocandoResponsavel(true)}
                className="focus-ring mt-0.5 cursor-pointer text-xs font-medium text-[var(--brand)] hover:underline"
              >
                Alterar
              </button>
            )}
          </div>
          {lead.veiculoNaoAtendido && <InfoItem icone={Car} label="Veículo não atendido" valor={lead.veiculoNaoAtendido} />}
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
        </div>
      </Card>

      <div className="grid gap-4 @3xl:grid-cols-3">
        {/* Estreito (painel lateral/celular): as colunas "somem" e os cartões seguem a ordem order-*: oportunidades, próximo contato, anotação e só então a linha do tempo. */}
        <div className="contents @3xl:block @3xl:col-span-2 @3xl:space-y-4">
          <Card className="order-1 !rounded-3xl p-5 @3xl:order-none">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-[15px] font-bold tracking-tight text-[var(--fg)]">Oportunidades</h2>
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
                    op.total != null || op.veiculo?.descricao || op.veiculo?.placa || op.veiculo?.chassi;
                  return (
                    <li key={op.id} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-hover)] px-3.5 py-3 text-sm">
                      <div className="flex items-center justify-between">
                        <div>
                          <p className="font-medium text-[var(--fg)]">{op.titulo}</p>
                          <p className="mt-0.5 flex items-center gap-1.5 text-xs text-[var(--fg-muted)]">
                            <span
                              className={`rounded-full px-2 py-px text-[11px] font-semibold ${
                                op.etapaTipo === TipoEtapaPipeline.Ganho
                                  ? "bg-[var(--success-soft)] text-[var(--success)]"
                                  : op.etapaTipo === TipoEtapaPipeline.Perdido
                                    ? "bg-[var(--danger-soft)] text-[var(--danger)]"
                                    : "bg-[var(--brand-soft)] text-[var(--brand)]"
                              }`}
                            >
                              {op.etapaNome}
                            </span>
                            {op.dataPrevistaFechamento && <span>previsão {formatarData(op.dataPrevistaFechamento)}</span>}
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
                          <Badge variant={op.ativa ? "brand" : "neutral"}>
                            {op.etapaTipo === TipoEtapaPipeline.Ganho && op.pagamentoAdesao != null ? `Adesão ${formatarMoeda(op.pagamentoAdesao)}` : formatarMoeda(op.valorEstimado)}
                          </Badge>
                          {podeExcluirOportunidade(op) && (
                            <button
                              type="button"
                              title="Excluir oportunidade"
                              aria-label={`Excluir oportunidade ${op.titulo}`}
                              onClick={() => setOportunidadeExcluindo(op)}
                              className="focus-ring cursor-pointer rounded-md p-1.5 text-[var(--fg-muted)] hover:bg-[var(--surface)] hover:text-[var(--danger)]"
                            >
                              <Trash2 className="size-3.5" />
                            </button>
                          )}
                        </div>
                      </div>

                      {temDadosVenda && (
                        <div className="mt-3 space-y-3.5 border-t border-[var(--border)] pt-3">
                          {(op.pagamentoAdesao != null || op.mensalidade != null || op.total != null) && (
                            <div className="grid gap-2 @md:grid-cols-3">
                              {op.pagamentoAdesao != null && <Destaque label="Adesão paga" valor={formatarMoeda(op.pagamentoAdesao)} />}
                              {op.mensalidade != null && <Destaque label="Mensalidade" valor={formatarMoeda(op.mensalidade)} />}
                              {op.total != null && <Destaque label="Total" valor={formatarMoeda(op.total)} />}
                            </div>
                          )}
                          {(op.cpf || op.estado || op.ativoEm || op.porcentagem != null || op.mensalidadeComDesconto != null || op.mensalidadeComCupom != null || (op.indicacao && (op.tipoIndicacao || op.valorIndicacao != null))) && (
                            <Grupo titulo="Venda">
                              {op.cpf && <InfoItem icone={IdCard} label="CPF/CNPJ" valor={formatarDocumento(op.cpf)} />}
                              {op.estado && <InfoItem icone={MapPin} label="Estado" valor={op.estado} />}
                              {op.ativoEm && <InfoItem icone={Calendar} label="Ativo em" valor={formatarData(op.ativoEm)} />}
                              {op.porcentagem != null && <InfoItem icone={Percent} label="Porcentagem" valor={`${op.porcentagem}%`} />}
                              {op.mensalidadeComDesconto != null && <InfoItem icone={Wallet} label="Mensalidade com desconto" valor={formatarMoeda(op.mensalidadeComDesconto)} />}
                              {op.mensalidadeComCupom != null && <InfoItem icone={Wallet} label="Mensalidade com cupom" valor={formatarMoeda(op.mensalidadeComCupom)} />}
                              {op.indicacao && op.tipoIndicacao && <InfoItem icone={Handshake} label="Canal de Aquisição" valor={op.tipoIndicacao} />}
                              {op.indicacao && op.valorIndicacao != null && <InfoItem icone={Wallet} label="Valor da indicação" valor={formatarMoeda(op.valorIndicacao)} />}
                            </Grupo>
                          )}
                          {(op.veiculo?.descricao || op.veiculo?.placa || op.veiculo?.chassi || op.veiculo?.fipe != null || op.veiculo?.rastreador != null || op.veiculo?.valorVistoria != null || op.veiculo?.dataChegada) && (
                            <Grupo titulo="Veículo">
                              {op.veiculo?.descricao && <InfoItem icone={Car} label="Veículo" valor={op.veiculo.descricao} />}
                              {op.veiculo?.placa && <InfoItem icone={Car} label="Placa" valor={op.veiculo.placa} />}
                              {op.veiculo?.chassi && <InfoItem icone={Car} label="Chassi (carro zero)" valor={op.veiculo.chassi} />}
                              {op.veiculo?.fipe != null && <InfoItem icone={Wallet} label="Valor FIPE" valor={formatarMoeda(op.veiculo.fipe)} />}
                              {op.veiculo?.rastreador != null && <InfoItem icone={Wallet} label="Custo do rastreador" valor={formatarMoeda(op.veiculo.rastreador)} />}
                              {op.veiculo?.valorVistoria != null && <InfoItem icone={Wallet} label="Custo da vistoria" valor={formatarMoeda(op.veiculo.valorVistoria)} />}
                              {op.veiculo?.dataChegada && <InfoItem icone={Calendar} label="Chegada do veículo" valor={formatarData(op.veiculo.dataChegada)} />}
                            </Grupo>
                          )}
                        </div>
                      )}

                      <AnexosDaVenda op={op} />
                    </li>
                  );
                })}
              </ul>
            )}
          </Card>

          <Card className="order-4 !rounded-3xl p-5 @3xl:order-none">
            <h2 className="mb-3 text-[15px] font-bold tracking-tight text-[var(--fg)]">Linha do tempo</h2>
            <Timeline itens={timeline} />
          </Card>
        </div>

        <div className="contents @3xl:block @3xl:space-y-4">
          <Card className="order-2 !rounded-3xl p-5 @3xl:order-none">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-[15px] font-bold tracking-tight text-[var(--fg)]">Próximo contato</h2>
              <Button size="sm" variant="secondary" onClick={() => setModalAtividade(true)}>
                <Plus className="size-4" /> Agendar
              </Button>
            </div>
            {pendentes.length === 0 ? (
              <div className="flex items-center gap-3 rounded-xl border border-dashed border-[var(--border)] px-3 py-3">
                <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-[var(--surface-hover)] text-[var(--fg-muted)]">
                  <CalendarClock className="size-4" aria-hidden />
                </span>
                <p className="text-sm text-[var(--fg-muted)]">Nenhum contato agendado. Marque a próxima ligação, visita ou retorno com este cliente.</p>
              </div>
            ) : (
              <ul className="space-y-2" aria-label="Próximos contatos">
                {pendentes.slice(0, 3).map((a) => {
                  const cor = COR_TIPO[a.tipo];
                  const Icone = tipoIcone[a.tipo];
                  return (
                    <li
                      key={a.id}
                      style={{ "--cor": cor } as React.CSSProperties}
                      className={`flex items-center gap-3 rounded-xl border border-l-[3px] border-[var(--border)] border-l-[var(--cor)] bg-[var(--surface)] px-3 py-2 ${a.atrasada ? "bg-[color-mix(in_srgb,var(--danger)_6%,var(--surface))]" : ""}`}
                    >
                      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-[color-mix(in_srgb,var(--cor)_16%,transparent)] text-[var(--cor)]">
                        <Icone className="size-4" aria-hidden />
                      </span>
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-sm font-semibold text-[var(--fg)]" title={a.assunto}>{a.assunto}</p>
                        <p className={`text-xs ${a.atrasada ? "font-medium text-[var(--danger)]" : "text-[var(--fg-muted)]"}`}>
                          {a.atrasada && "Atrasada · "}
                          {tipoLabel[a.tipo]} · {formatarDataHora(a.dataHoraPrevista)}
                        </p>
                      </div>
                      <button
                        type="button"
                        title="Marcar como concluída"
                        aria-label={`Concluir ${a.assunto}`}
                        disabled={concluindoId === a.id}
                        onClick={() => concluirPendente(a)}
                        className="focus-ring flex size-8 shrink-0 cursor-pointer items-center justify-center rounded-full border border-[var(--border)] text-[var(--fg-muted)] transition-colors hover:border-[var(--success)] hover:bg-[var(--success-soft)] hover:text-[var(--success)] disabled:opacity-50"
                      >
                        <Check className="size-4" />
                      </button>
                    </li>
                  );
                })}
                {pendentes.length > 3 && <li className="px-1 text-xs text-[var(--fg-muted)]">+ {pendentes.length - 3} contato(s) agendado(s) — veja na Agenda.</li>}
              </ul>
            )}
          </Card>

          <Card className="order-3 !rounded-3xl p-5 @3xl:order-none">
            <h2 className="mb-3 text-[15px] font-bold tracking-tight text-[var(--fg)]">Nova anotação</h2>
            <Textarea value={nota} onChange={(e) => setNota(e.target.value)} placeholder="Escreva uma anotação sobre o lead..." />
            <div className="mt-2 flex justify-end">
              <Button size="sm" onClick={adicionarNota} loading={salvando} disabled={!nota.trim()}>
                Salvar anotação
              </Button>
            </div>
            {anotacoes.length > 0 && (
              <ul className="mt-4 space-y-2 border-t border-[var(--border)] pt-3" aria-label="Anotações deste lead">
                {anotacoes.map((n) => (
                  <li key={n.id} className="rounded-xl bg-[var(--surface-hover)] px-3 py-2 text-sm">
                    <p className="whitespace-pre-wrap break-words text-[var(--fg)]">{n.descricao}</p>
                    <p className="mt-1 text-[11px] text-[var(--fg-muted)]">
                      {formatarDataHora(n.ocorridoEm)}
                      {n.usuarioNome && ` · ${n.usuarioNome}`}
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      </div>

      <Modal open={modalEditar} onClose={() => { setModalEditar(false); setDuplicidade(null); }} title="Editar lead" size="lg">
        {duplicidade && (
          <p className="mb-4 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-sm text-[var(--warning)]">
            Já existe outro lead com o mesmo {duplicidade.campoDuplicado}: {duplicidade.nomeExistente}.
          </p>
        )}
        <LeadForm valoresIniciais={paraFormValues(lead)} salvando={salvando} onSubmit={salvarEdicao} onCancel={() => setModalEditar(false)} idPrefix="editar" ehIndicacao={ehIndicacao(lead)} />
      </Modal>

      {/* Nova oportunidade: mesmo formulário da Venda concluída, só o nome do cliente é obrigatório. */}
      <AlterarResponsavelDialog
        open={trocandoResponsavel}
        leadId={lead.id}
        leadNome={lead.nomeOuRazaoSocial}
        responsavelAtualId={lead.responsavelId}
        onCancel={() => setTrocandoResponsavel(false)}
        onConcluido={() => {
          setTrocandoResponsavel(false);
          carregar();
        }}
      />

      <VendaConcluidaDialog
        open={modalOportunidade}
        modo="oportunidade"
        leadId={lead.id}
        etapaNome=""
        valorEstimado={0}
        onEscolherGanho={() => {
          setModalOportunidade(false);
          setModalVendaGanho(true);
        }}
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
        open={modalVendaGanho}
        leadId={lead.id}
        pipelineGanhoEtapaId={etapaGanhoId}
        etapaNome={ETAPA_VENDA_CONCLUIDA}
        valorEstimado={0}
        permitirVeiculosAdicionais
        onCancel={() => setModalVendaGanho(false)}
        onConcluido={() => {
          setModalVendaGanho(false);
          notificar("success", "Venda concluída registrada.");
          carregar();
        }}
      />

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

      <ConversarSobreLeadDialog open={modalConversar} leadId={lead.id} nomeDoLead={lead.nomeOuRazaoSocial} onClose={() => setModalConversar(false)} />

      {/* Outro veículo: a venda do veículo novo vira um card novo já em "Venda concluída". */}
      <VendaConcluidaDialog
        open={modalVeiculos}
        novoVeiculo
        leadId={lead.id}
        etapaNome={ETAPA_VENDA_CONCLUIDA}
        valorEstimado={0}
        onCancel={() => setModalVeiculos(false)}
        onConcluido={(resultado) => {
          setModalVeiculos(false);
          notificar("success", "Venda do outro veículo registrada — card novo em \"Venda concluída\".");
          if (resultado?.novoLeadId) {
            if (noPainel) abrirLead(resultado.novoLeadId);
            else navigate(`/app/crm/leads/${resultado.novoLeadId}`);
          }
        }}
      />

      <ConfirmDialog
        open={!!oportunidadeExcluindo}
        title="Excluir oportunidade"
        danger
        confirmLabel="Excluir"
        loading={excluindoOportunidade}
        message={
          <>
            Tem certeza que deseja excluir a oportunidade <strong className="text-[var(--fg)]">{oportunidadeExcluindo?.titulo}</strong> (
            {oportunidadeExcluindo?.etapaNome}) de <strong className="text-[var(--fg)]">{lead.nomeOuRazaoSocial}</strong>? Ela sai do Pipeline,
            do painel e das metas; o lead continua no quadro de leads.
          </>
        }
        onConfirm={excluirOportunidade}
        onCancel={() => setOportunidadeExcluindo(null)}
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

/** Termo de adesão, comprovante de pagamento e demais anexos enviados ao concluir a venda. */
function AnexosDaVenda({ op }: { op: LeadOpportunitySummary }) {
  const anexos = [
    { rotulo: "Termo de adesão", url: op.termoAdesaoArquivoUrl },
    { rotulo: "Comprovante de pagamento da adesão", url: op.pagamentoAdesaoArquivoUrl },
    { rotulo: "Comprovante de indicação", url: op.comprovanteIndicacaoArquivoUrl },
    { rotulo: "Comprovante de vistoria", url: op.comprovanteVistoriaArquivoUrl },
  ].filter((a) => !!a.url);
  const pagamentoAgendado = !op.pagamentoAdesaoArquivoUrl && !!op.dataPagamentoAdesaoPrevista;
  if (anexos.length === 0 && !pagamentoAgendado) return null;

  // "2026-09-25" → "25/09/2026" sem passar por Date (que converteria de UTC e poderia voltar um dia).
  const dataPagamento = op.dataPagamentoAdesaoPrevista?.slice(0, 10).split("-").reverse().join("/");
  return (
    <div className="mt-3 border-t border-[var(--border)] pt-3">
      <p className="mb-2 text-xs font-medium text-[var(--fg-muted)]">Anexos da venda</p>
      <div className="flex flex-wrap gap-2">
        {anexos.map((a) => (
          <a
            key={a.rotulo}
            href={a.url!}
            target="_blank"
            rel="noopener noreferrer"
            className="focus-ring inline-flex items-center gap-1.5 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-2.5 py-1.5 text-xs font-medium text-[var(--fg)] hover:border-[var(--brand)] hover:text-[var(--brand)]"
          >
            <FileText className="size-3.5 shrink-0" aria-hidden />
            {a.rotulo}
          </a>
        ))}
        {pagamentoAgendado && (
          <span className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--warning-soft)] px-2.5 py-1.5 text-xs font-medium text-[var(--warning)]">
            <Calendar className="size-3.5 shrink-0" aria-hidden />
            Adesão será paga em {dataPagamento} — comprovante pendente
          </span>
        )}
      </div>
    </div>
  );
}

function InfoItem({ icone: Icone, label, valor, href, whatsapp, nome }: { icone: typeof Phone; label: string; valor: string; href?: string; whatsapp?: string | null; nome?: string }) {
  return (
    <div className="flex min-w-0 items-center gap-2.5 rounded-xl bg-[var(--surface-hover)] px-2.5 py-2">
      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand)]">
        <Icone className="size-4" aria-hidden />
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{label}</p>
        {href ? (
          <a href={href} target={href.startsWith("http") ? "_blank" : undefined} rel="noreferrer" className="focus-ring block truncate text-sm font-medium text-[var(--fg)] hover:text-[var(--brand)] hover:underline" title={valor}>
            {valor}
          </a>
        ) : (
          <p className="truncate text-sm font-medium text-[var(--fg)]" title={valor}>{valor}</p>
        )}
      </div>
      {whatsapp && <BotaoWhatsApp telefone={whatsapp} nome={nome ?? valor} className="size-8" />}
    </div>
  );
}

/** Destaque de um valor da venda (adesão, mensalidade, total). */
function Destaque({ label, valor }: { label: string; valor: string }) {
  return (
    <div className="min-w-0 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 py-2">
      <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{label}</p>
      <p className="truncate text-base font-bold tracking-tight text-[var(--fg)] [font-variant-numeric:tabular-nums]">{valor}</p>
    </div>
  );
}

/** Grupo de campos da venda com título pequeno. */
function Grupo({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="mb-1.5 text-[11px] font-bold uppercase tracking-wide text-[var(--fg-muted)]">{titulo}</p>
      <div className="grid gap-2 @md:grid-cols-2 @4xl:grid-cols-3">{children}</div>
    </div>
  );
}
