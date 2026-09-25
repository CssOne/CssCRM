import { useEffect, useRef, useState } from "react";
import { OPCOES_TIPO_INDICACAO } from "../../lib/opcoesLead";
import { Paperclip } from "lucide-react";
import { api, ApiRequestError, uploadFile } from "../../lib/api";
import type { ChangeStageRequest, LeadDetail, Opportunity, OpportunityCreateRequest, OpportunityUpdateRequest } from "../../lib/types";
import { ESTADOS_BRASIL } from "../../lib/estados";
import { formatarData } from "../../lib/format";
import { Button, Checkbox, CpfInput, Input, Label, Modal, MoneyInput, Select, useToast } from "../ui";

export type DadosVendaConcluida = Omit<ChangeStageRequest, "novaEtapaId" | "motivoPerdaId">;

const valoresIniciais: DadosVendaConcluida = {
  rowVersion: 0,
  valorFinal: 0,
  dataEfetivaFechamento: new Date().toISOString().slice(0, 10),
  cpf: "",
  estado: "",
  indicacao: false,
  tipoIndicacao: "",
  valorIndicacao: null,
  total: null,
  ativoEm: "",
  mensalidade: null,
  mensalidadeComDesconto: null,
  mensalidadeComCupom: null,
  pagamentoAdesao: null,
  porcentagem: null,
  migracao: false,
  veiculo: { descricao: "", placa: "", fipe: null, rastreador: null, valorVistoria: null, vistoriadorId: null, dataChegada: "" },
  dataPagamentoAdesaoPrevista: null,
};

function hojeLocal() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** Preenche o formulário com o que já está salvo numa oportunidade (só os campos preenchidos nela). */
function mesclarOportunidade(v: DadosVendaConcluida, o: Opportunity): DadosVendaConcluida {
  return {
    ...v,
    rowVersion: o.rowVersion,
    cpf: o.cpf || v.cpf,
    estado: o.estado || v.estado,
    indicacao: o.indicacao ?? v.indicacao,
    tipoIndicacao: o.tipoIndicacao || v.tipoIndicacao,
    valorIndicacao: o.valorIndicacao ?? v.valorIndicacao,
    total: o.total ?? v.total,
    mensalidade: o.mensalidade ?? v.mensalidade,
    mensalidadeComDesconto: o.mensalidadeComDesconto ?? v.mensalidadeComDesconto,
    mensalidadeComCupom: o.mensalidadeComCupom ?? v.mensalidadeComCupom,
    pagamentoAdesao: o.pagamentoAdesao ?? v.pagamentoAdesao,
    porcentagem: o.porcentagem ?? v.porcentagem,
    migracao: o.migracao || v.migracao,
    dataPagamentoAdesaoPrevista: o.dataPagamentoAdesaoPrevista ?? v.dataPagamentoAdesaoPrevista,
    veiculo: {
      ...v.veiculo,
      descricao: o.veiculo?.descricao || v.veiculo?.descricao || "",
      placa: o.veiculo?.placa || v.veiculo?.placa || "",
      fipe: o.veiculo?.fipe ?? v.veiculo?.fipe ?? null,
      rastreador: o.veiculo?.rastreador ?? v.veiculo?.rastreador ?? null,
      valorVistoria: o.veiculo?.valorVistoria ?? v.veiculo?.valorVistoria ?? null,
      vistoriadorId: o.veiculo?.vistoriadorId ?? v.veiculo?.vistoriadorId ?? null,
      dataChegada: o.veiculo?.dataChegada || v.veiculo?.dataChegada || "",
    },
  };
}

/**
 * Três modos de uso (os dois primeiros são de Venda concluída; o terceiro, `modo="oportunidade"`,
 * reaproveita o mesmo formulário para criar uma oportunidade, com todos os campos opcionais
 * exceto o nome do cliente, mais Concorrente e Previsão de fechamento):
 * - Modo Pipeline (`opportunityId` conhecido): o diálogo só envia os anexos e devolve os dados via
 *   `onConfirm` — quem muda a etapa da oportunidade é o chamador (Pipeline.tsx já faz isso).
 * - Modo Quadro de leads (`opportunityId` null): a oportunidade pode nem existir ainda. O diálogo
 *   resolve sozinho (reaproveita a oportunidade aberta do lead, ou cria uma nova), envia os anexos,
 *   muda a etapa dela pra "Ganho" (`pipelineGanhoEtapaId`) e só então avisa via `onConcluido` — o
 *   chamador só precisa mover o card do LEAD depois disso.
 */
export function VendaConcluidaDialog({
  open,
  opportunityId = null,
  leadId,
  etapaNome,
  valorEstimado,
  rowVersionInicial = 0,
  enviando = false,
  pipelineGanhoEtapaId,
  oportunidadeEditar = null,
  modo = "venda",
  onConfirm,
  onConcluido,
  onCancel,
}: {
  open: boolean;
  opportunityId?: string | null;
  leadId: string | null;
  etapaNome: string;
  valorEstimado: number;
  rowVersionInicial?: number;
  enviando?: boolean;
  pipelineGanhoEtapaId?: string;
  /** Quando informada, o diálogo abre em modo de edição: pré-preenche com os dados já salvos
   *  dessa oportunidade e, ao confirmar, salva via PUT em vez de mudar de etapa. */
  oportunidadeEditar?: Opportunity | null;
  /** "oportunidade": cria uma oportunidade com este mesmo formulário (só o nome do cliente é obrigatório). */
  modo?: "venda" | "oportunidade";
  onConfirm?: (dados: DadosVendaConcluida) => void;
  onConcluido?: () => void;
  onCancel: () => void;
}) {
  const { notificar } = useToast();
  const [valores, setValores] = useState<DadosVendaConcluida>(valoresIniciais);
  const [termoArquivo, setTermoArquivo] = useState<File | null>(null);
  const [pagamentoArquivo, setPagamentoArquivo] = useState<File | null>(null);
  const [enviandoArquivos, setEnviandoArquivos] = useState(false);
  const [oportunidadeResolvidaId, setOportunidadeResolvidaId] = useState<string | null>(null);
  const [responsavelIdLead, setResponsavelIdLead] = useState<string | null>(null);
  const [dataChegadaLead, setDataChegadaLead] = useState<string | null>(null);
  const [temRastreador, setTemRastreador] = useState(false);
  const [temVistoria, setTemVistoria] = useState(false);
  const [nomeCliente, setNomeCliente] = useState("");
  const [concorrente, setConcorrente] = useState("");
  const [previsaoFechamento, setPrevisaoFechamento] = useState("");
  const ehVenda = modo === "venda";

  useEffect(() => {
    if (!open || !leadId) return;
    setTermoArquivo(null);
    setPagamentoArquivo(null);
    setDataChegadaLead(null);
    setNomeCliente("");
    setConcorrente("");
    setPrevisaoFechamento("");

    if (oportunidadeEditar) {
      setValores({
        rowVersion: oportunidadeEditar.rowVersion,
        valorFinal: oportunidadeEditar.valorFinal ?? oportunidadeEditar.valorEstimado,
        dataEfetivaFechamento: oportunidadeEditar.dataEfetivaFechamento?.slice(0, 10) ?? new Date().toISOString().slice(0, 10),
        cpf: oportunidadeEditar.cpf ?? "",
        estado: oportunidadeEditar.estado ?? "",
        indicacao: oportunidadeEditar.indicacao ?? false,
        tipoIndicacao: oportunidadeEditar.tipoIndicacao ?? "",
        valorIndicacao: oportunidadeEditar.valorIndicacao ?? null,
        total: oportunidadeEditar.total ?? null,
        ativoEm: oportunidadeEditar.ativoEm ?? "",
        mensalidade: oportunidadeEditar.mensalidade ?? null,
        mensalidadeComDesconto: oportunidadeEditar.mensalidadeComDesconto ?? null,
        mensalidadeComCupom: oportunidadeEditar.mensalidadeComCupom ?? null,
        pagamentoAdesao: oportunidadeEditar.pagamentoAdesao ?? null,
        porcentagem: oportunidadeEditar.porcentagem ?? null,
        migracao: oportunidadeEditar.migracao ?? false,
        dataPagamentoAdesaoPrevista: oportunidadeEditar.dataPagamentoAdesaoPrevista ?? null,
        veiculo: {
          descricao: oportunidadeEditar.veiculo?.descricao ?? "",
          placa: oportunidadeEditar.veiculo?.placa ?? "",
          fipe: oportunidadeEditar.veiculo?.fipe ?? null,
          rastreador: oportunidadeEditar.veiculo?.rastreador ?? null,
          valorVistoria: oportunidadeEditar.veiculo?.valorVistoria ?? null,
          vistoriadorId: oportunidadeEditar.veiculo?.vistoriadorId ?? null,
          dataChegada: oportunidadeEditar.veiculo?.dataChegada ?? "",
        },
      });
      setTemRastreador((oportunidadeEditar.veiculo?.rastreador ?? null) != null);
      setTemVistoria((oportunidadeEditar.veiculo?.valorVistoria ?? null) != null);
      setOportunidadeResolvidaId(oportunidadeEditar.id);
      setResponsavelIdLead(oportunidadeEditar.responsavelId);
    } else {
      setValores({ ...valoresIniciais, valorFinal: valorEstimado, rowVersion: rowVersionInicial });
      setOportunidadeResolvidaId(opportunityId);
      setResponsavelIdLead(null);
      setTemRastreador(false);
      setTemVistoria(false);
    }

    const controller = new AbortController();
    api
      .get<LeadDetail>(`/crm/leads/${leadId}`, controller.signal)
      .then((lead) => {
        setDataChegadaLead(lead.criadoEm);
        setNomeCliente(lead.nomeOuRazaoSocial);
        if (oportunidadeEditar) return;
        // Dados do cadastro do cliente e o valor da adesão informado na Cotação.
        setValores((v) => ({
          ...v,
          cpf: lead.documento ?? "",
          estado: lead.estado ?? "",
          tipoIndicacao: lead.tipoIndicacao ?? "",
          pagamentoAdesao: v.pagamentoAdesao ?? lead.valorAdesao ?? null,
          veiculo: { ...v.veiculo, placa: lead.placa ?? "" },
        }));
        setResponsavelIdLead(lead.responsavelId ?? null);
        if (!ehVenda) return;

        // Venda concluída: se o lead já tem uma oportunidade, o que já foi preenchido nela vem pronto.
        const idExistente = opportunityId ?? lead.oportunidades.find((o) => o.ativa)?.id ?? null;
        if (!opportunityId) setOportunidadeResolvidaId(idExistente);
        if (!idExistente) return;
        api
          .get<Opportunity>(`/crm/opportunities/${idExistente}`, controller.signal)
          .then((o) => {
            setValores((v) => mesclarOportunidade(v, o));
            setTemRastreador((o.veiculo?.rastreador ?? null) != null);
            setTemVistoria((o.veiculo?.valorVistoria ?? null) != null);
          })
          .catch(() => {});
      })
      .catch(() => {});
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, leadId, oportunidadeEditar]);

  useEffect(() => {
    if (valores.mensalidade == null || valores.porcentagem == null) return;
    const comCupom = Math.round(valores.mensalidade * (1 - valores.porcentagem / 100) * 100) / 100;
    setValores((v) => ({ ...v, mensalidadeComCupom: comCupom }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [valores.mensalidade, valores.porcentagem]);

  useEffect(() => {
    if (valores.pagamentoAdesao == null) return;
    const desconto = (temRastreador ? valores.veiculo?.rastreador ?? 0 : 0) + (temVistoria ? valores.veiculo?.valorVistoria ?? 0 : 0);
    const total = Math.round((valores.pagamentoAdesao - desconto) * 100) / 100;
    setValores((v) => ({ ...v, total }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [valores.pagamentoAdesao, temRastreador, temVistoria, valores.veiculo?.rastreador, valores.veiculo?.valorVistoria]);

  if (!open) return null;

  function set<K extends keyof DadosVendaConcluida>(campo: K, valor: DadosVendaConcluida[K]) {
    setValores((v) => ({ ...v, [campo]: valor }));
  }

  function setVeiculo(campo: keyof NonNullable<DadosVendaConcluida["veiculo"]>, valor: string | number | null) {
    setValores((v) => ({ ...v, veiculo: { ...v.veiculo, [campo]: valor } }));
  }

  const indicacaoPreenchida = !valores.indicacao || (!!valores.tipoIndicacao && (valores.valorIndicacao ?? -1) >= 0);
  // Adesão paga depois: com a data do pagamento marcada, o comprovante deixa de ser obrigatório.
  const pagamentoAgendado = !!valores.dataPagamentoAdesaoPrevista;
  const temComprovantePagamento = !!pagamentoArquivo || !!oportunidadeEditar?.pagamentoAdesaoArquivoUrl;

  const podeConfirmar = !ehVenda
    ? !!nomeCliente.trim() && !enviandoArquivos && !enviando
    : (valores.valorFinal ?? -1) >= 0 &&
    !!valores.dataEfetivaFechamento &&
    !!valores.cpf &&
    !!valores.estado &&
    (valores.porcentagem ?? -1) >= 0 &&
    (valores.mensalidade ?? -1) >= 0 &&
    (valores.mensalidadeComCupom ?? -1) >= 0 &&
    (valores.pagamentoAdesao ?? -1) >= 0 &&
    (valores.total ?? -1) >= 0 &&
    indicacaoPreenchida &&
    !!valores.veiculo?.descricao &&
    !!valores.veiculo?.placa &&
    (valores.veiculo?.fipe ?? -1) >= 0 &&
    (!temRastreador || (valores.veiculo?.rastreador ?? -1) >= 0) &&
    (!temVistoria || (valores.veiculo?.valorVistoria ?? -1) >= 0) &&
    (!!termoArquivo || !!oportunidadeEditar?.termoAdesaoArquivoUrl) &&
    (temComprovantePagamento || pagamentoAgendado) &&
    !enviandoArquivos &&
    !enviando;

  async function criarOportunidade() {
    if (!leadId || !nomeCliente.trim()) return;
    if (!responsavelIdLead) {
      notificar("error", "Este lead não tem um responsável definido. Atribua um consultor antes de criar a oportunidade.");
      return;
    }
    setEnviandoArquivos(true);
    const v = valores.veiculo;
    const temVeiculo = !!v && [v.descricao, v.placa, v.fipe, v.rastreador, v.valorVistoria, v.vistoriadorId, v.dataChegada].some((x) => x != null && x !== "");
    const request: OpportunityCreateRequest = {
      leadId,
      titulo: nomeCliente.trim(),
      responsavelId: responsavelIdLead,
      produtoOuServico: null,
      valorEstimado: valores.mensalidade ?? 0,
      probabilidadeFechamento: null,
      dataPrevistaFechamento: previsaoFechamento || null,
      concorrente: concorrente.trim() || null,
      observacoes: null,
      dataAdesao: null,
      mensalidade: valores.mensalidade,
      mensalidadeComDesconto: valores.mensalidadeComDesconto,
      mensalidadeComCupom: valores.mensalidadeComCupom,
      pagamentoAdesao: valores.pagamentoAdesao,
      porcentagem: valores.porcentagem,
      termoAdesaoAceito: false,
      migracao: valores.migracao ?? false,
      veiculo: temVeiculo ? { ...v!, dataChegada: v!.dataChegada || null } : null,
      cpf: valores.cpf || null,
      estado: valores.estado || null,
      indicacao: valores.indicacao ?? null,
      tipoIndicacao: valores.indicacao ? valores.tipoIndicacao || null : null,
      valorIndicacao: valores.indicacao ? valores.valorIndicacao : null,
      total: valores.total,
    };
    try {
      const nova = await api.post<Opportunity>("/crm/opportunities", request);
      if (termoArquivo) await uploadFile<Opportunity>(`/crm/opportunities/${nova.id}/attachments/termo-adesao`, termoArquivo);
      if (pagamentoArquivo) await uploadFile<Opportunity>(`/crm/opportunities/${nova.id}/attachments/pagamento-adesao`, pagamentoArquivo);
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível criar a oportunidade.");
      setEnviandoArquivos(false);
      return;
    }
    setEnviandoArquivos(false);
    onConcluido?.();
  }

  async function confirmar() {
    if (!ehVenda) return criarOportunidade();
    if (!leadId) return;
    const precisaTermo = !termoArquivo && !oportunidadeEditar?.termoAdesaoArquivoUrl;
    const precisaPagamento = !temComprovantePagamento && !pagamentoAgendado;
    if (precisaTermo || precisaPagamento) return;
    setEnviandoArquivos(true);

    let idOportunidade = oportunidadeResolvidaId;
    // Cada upload de anexo (e a criação da oportunidade, quando é o caso) salva a oportunidade
    // (SaveChanges), o que avança o RowVersion — por isso sempre repassamos o valor mais recente
    // devolvido por cada chamada em vez do capturado quando o quadro foi carregado, senão o
    // servidor recusa por conflito de concorrência otimista.
    let rowVersionAtual = valores.rowVersion;

    try {
      if (!idOportunidade) {
        if (!responsavelIdLead) {
          notificar("error", "Este lead não tem um responsável definido. Atribua um consultor antes de concluir a venda.");
          setEnviandoArquivos(false);
          return;
        }
        const nova = await api.post<Opportunity>("/crm/opportunities", {
          leadId,
          titulo: "Venda concluída",
          responsavelId: responsavelIdLead,
          valorEstimado: valores.valorFinal ?? 0,
          termoAdesaoAceito: false,
          migracao: valores.migracao ?? false,
        });
        idOportunidade = nova.id;
        rowVersionAtual = nova.rowVersion;
      }

      if (termoArquivo) {
        const r1 = await uploadFile<Opportunity>(`/crm/opportunities/${idOportunidade}/attachments/termo-adesao`, termoArquivo);
        rowVersionAtual = r1.rowVersion;
      }
      if (pagamentoArquivo) {
        const r2 = await uploadFile<Opportunity>(`/crm/opportunities/${idOportunidade}/attachments/pagamento-adesao`, pagamentoArquivo);
        rowVersionAtual = r2.rowVersion;
      }
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível enviar os anexos.");
      setEnviandoArquivos(false);
      return;
    }

    const veiculoFinal = valores.veiculo ? { ...valores.veiculo, dataChegada: valores.veiculo.dataChegada || null } : null;

    if (oportunidadeEditar) {
      // Modo edição: a oportunidade já está em "Venda concluída" — só atualiza os dados salvos.
      const payload: OpportunityUpdateRequest = {
        titulo: oportunidadeEditar.titulo,
        responsavelId: oportunidadeEditar.responsavelId,
        produtoOuServico: oportunidadeEditar.produtoOuServico ?? null,
        valorEstimado: oportunidadeEditar.valorEstimado,
        probabilidadeFechamento: oportunidadeEditar.probabilidadeFechamento ?? null,
        dataPrevistaFechamento: oportunidadeEditar.dataPrevistaFechamento ?? null,
        concorrente: oportunidadeEditar.concorrente ?? null,
        observacoes: oportunidadeEditar.observacoes ?? null,
        dataAdesao: oportunidadeEditar.dataAdesao ?? null,
        termoAdesaoAceito: oportunidadeEditar.termoAdesaoAceito,
        ativoEm: valores.dataEfetivaFechamento || null,
        mensalidade: valores.mensalidade,
        mensalidadeComDesconto: valores.mensalidadeComDesconto,
        mensalidadeComCupom: valores.mensalidadeComCupom,
        pagamentoAdesao: valores.pagamentoAdesao,
        porcentagem: valores.porcentagem,
        migracao: valores.migracao ?? false,
        veiculo: veiculoFinal,
        rowVersion: rowVersionAtual,
        cpf: valores.cpf || null,
        estado: valores.estado || null,
        indicacao: valores.indicacao ?? null,
        tipoIndicacao: valores.tipoIndicacao || null,
        valorIndicacao: valores.valorIndicacao,
        total: valores.total,
        dataEfetivaFechamento: valores.dataEfetivaFechamento || null,
        dataPagamentoAdesaoPrevista: valores.dataPagamentoAdesaoPrevista || null,
      };
      try {
        await api.put(`/crm/opportunities/${idOportunidade}`, payload);
      } catch (e) {
        notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível salvar as alterações.");
        setEnviandoArquivos(false);
        return;
      }
      setEnviandoArquivos(false);
      onConcluido?.();
      return;
    }

    const dadosFinais: DadosVendaConcluida = {
      ...valores,
      rowVersion: rowVersionAtual,
      ativoEm: valores.dataEfetivaFechamento || null,
      veiculo: veiculoFinal,
      dataPagamentoAdesaoPrevista: valores.dataPagamentoAdesaoPrevista || null,
    };

    if (opportunityId) {
      // Modo Pipeline: quem muda a etapa é o chamador.
      setEnviandoArquivos(false);
      onConfirm?.(dadosFinais);
      return;
    }

    // Modo Quadro de leads: o diálogo já conclui a venda sozinho.
    try {
      await api.post(`/crm/opportunities/${idOportunidade}/change-stage`, { novaEtapaId: pipelineGanhoEtapaId, ...dadosFinais });
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível concluir a venda.");
      setEnviandoArquivos(false);
      return;
    }
    setEnviandoArquivos(false);
    onConcluido?.();
  }

  return (
    <Modal open={open} onClose={onCancel} title={!ehVenda ? "Nova oportunidade" : oportunidadeEditar ? "Editar venda concluída" : `Concluir venda — mover para "${etapaNome}"`} size="lg">
      <div className="space-y-5">
        <div>
          <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">{ehVenda ? "Dados da venda" : "Dados da oportunidade"}</h3>
          <div className="grid gap-4 sm:grid-cols-2">
            {!ehVenda && (
              <div className="sm:col-span-2">
                <Label htmlFor="venda-nome-cliente" required>
                  Nome do cliente
                </Label>
                <Input id="venda-nome-cliente" value={nomeCliente} onChange={(e) => setNomeCliente(e.target.value)} />
              </div>
            )}
            <div>
              <Label htmlFor="venda-chegada">Data de chegada</Label>
              <Input id="venda-chegada" disabled value={dataChegadaLead ? formatarData(dataChegadaLead) : "—"} />
            </div>
            {ehVenda ? (
              <div>
                <Label htmlFor="venda-data" required={ehVenda}>
                  Data da venda
                </Label>
                <Input id="venda-data" type="date" value={valores.dataEfetivaFechamento ?? ""} onChange={(e) => set("dataEfetivaFechamento", e.target.value)} />
              </div>
            ) : (
              <div>
                <Label htmlFor="venda-previsao">Previsão de fechamento</Label>
                <Input id="venda-previsao" type="date" value={previsaoFechamento} onChange={(e) => setPrevisaoFechamento(e.target.value)} />
              </div>
            )}
            {!ehVenda && (
              <div className="sm:col-span-2">
                <Label htmlFor="venda-concorrente">Concorrente</Label>
                <Input id="venda-concorrente" value={concorrente} onChange={(e) => setConcorrente(e.target.value)} />
              </div>
            )}
            <div>
              <Label htmlFor="venda-cpf" required={ehVenda}>
                CPF
              </Label>
              <CpfInput id="venda-cpf" value={valores.cpf ?? ""} onChange={(e) => set("cpf", e.target.value)} />
            </div>
            <div>
              <Label htmlFor="venda-estado" required={ehVenda}>
                Estado
              </Label>
              <Select id="venda-estado" value={valores.estado ?? ""} onChange={(e) => set("estado", e.target.value)}>
                <option value="">Selecione...</option>
                {ESTADOS_BRASIL.map((uf) => (
                  <option key={uf.sigla} value={uf.sigla}>
                    {uf.nome}
                  </option>
                ))}
              </Select>
            </div>
          </div>

          <div className="mt-4 flex flex-wrap items-end gap-4">
            <Checkbox label="É uma migração" checked={valores.migracao ?? false} onChange={(e) => set("migracao", e.target.checked)} />
            <Checkbox label="Teve indicação" checked={valores.indicacao ?? false} onChange={(e) => set("indicacao", e.target.checked)} />
            {valores.indicacao && (
              <>
                <div className="w-48">
                  <Label htmlFor="venda-tipo-indicacao" required={ehVenda}>
                    Tipo de indicação
                  </Label>
                  <Select id="venda-tipo-indicacao" value={valores.tipoIndicacao ?? ""} onChange={(e) => set("tipoIndicacao", e.target.value)}>
                    <option value="">Selecione...</option>
                    {OPCOES_TIPO_INDICACAO.map((tipo) => (
                      <option key={tipo} value={tipo}>
                        {tipo}
                      </option>
                    ))}
                  </Select>
                </div>
                <div className="w-40">
                  <Label htmlFor="venda-valor-indicacao" required={ehVenda}>
                    Valor da indicação (R$)
                  </Label>
                  <MoneyInput id="venda-valor-indicacao" value={valores.valorIndicacao} onChange={(v) => set("valorIndicacao", v)} />
                </div>
              </>
            )}
          </div>

        </div>

        <div className="border-t border-[var(--border)] pt-4">
          <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">Veículo</h3>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <Label htmlFor="venda-veiculo-descricao" required={ehVenda}>
                Veículo (marca/modelo)
              </Label>
              <Input id="venda-veiculo-descricao" value={valores.veiculo?.descricao ?? ""} onChange={(e) => setVeiculo("descricao", e.target.value)} />
            </div>
            <div>
              <Label htmlFor="venda-veiculo-placa" required={ehVenda}>
                Placa
              </Label>
              <Input id="venda-veiculo-placa" value={valores.veiculo?.placa ?? ""} onChange={(e) => setVeiculo("placa", e.target.value.toUpperCase())} />
            </div>
            <div>
              <Label htmlFor="venda-veiculo-fipe" required={ehVenda}>
                Valor FIPE (R$)
              </Label>
              <MoneyInput id="venda-veiculo-fipe" value={valores.veiculo?.fipe} onChange={(v) => setVeiculo("fipe", v)} />
            </div>
            <div>
              <Checkbox
                label="Custo do rastreador"
                checked={temRastreador}
                onChange={(e) => {
                  setTemRastreador(e.target.checked);
                  if (!e.target.checked) setVeiculo("rastreador", null);
                }}
              />
              {temRastreador && (
                <div className="mt-2">
                  <Label htmlFor="venda-veiculo-rastreador" required={ehVenda}>
                    Custo do rastreador (R$)
                  </Label>
                  <MoneyInput id="venda-veiculo-rastreador" value={valores.veiculo?.rastreador ?? null} onChange={(v) => setVeiculo("rastreador", v)} />
                </div>
              )}
            </div>
            <div>
              <Checkbox
                label="Custo da vistoria"
                checked={temVistoria}
                onChange={(e) => {
                  setTemVistoria(e.target.checked);
                  if (!e.target.checked) setVeiculo("valorVistoria", null);
                }}
              />
              {temVistoria && (
                <div className="mt-2">
                  <Label htmlFor="venda-veiculo-valor-vistoria" required={ehVenda}>
                    Custo da vistoria (R$)
                  </Label>
                  <MoneyInput id="venda-veiculo-valor-vistoria" value={valores.veiculo?.valorVistoria ?? null} onChange={(v) => setVeiculo("valorVistoria", v)} />
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="border-t border-[var(--border)] pt-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="venda-mensalidade" required={ehVenda}>
                Mensalidade (R$)
              </Label>
              <MoneyInput id="venda-mensalidade" value={valores.mensalidade} onChange={(v) => set("mensalidade", v)} />
            </div>
            <div>
              <Label htmlFor="venda-porcentagem" required={ehVenda}>
                Cupom de desconto (Porcentagem)
              </Label>
              <div className="relative">
                <Input
                  id="venda-porcentagem"
                  type="number"
                  min={0}
                  max={100}
                  step="0.01"
                  className="pr-8"
                  value={valores.porcentagem ?? ""}
                  onChange={(e) => set("porcentagem", e.target.value ? Number(e.target.value) : null)}
                />
                <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-sm text-[var(--fg-muted)]">%</span>
              </div>
            </div>
            <div>
              <Label htmlFor="venda-mensalidade-cupom" required={ehVenda}>
                Mensalidade com desconto (R$)
              </Label>
              <MoneyInput id="venda-mensalidade-cupom" value={valores.mensalidadeComCupom} onChange={(v) => set("mensalidadeComCupom", v)} />
            </div>
            <div>
              <Label htmlFor="venda-adesao" required={ehVenda}>
                Pagamento de adesão (R$)
              </Label>
              <MoneyInput id="venda-adesao" value={valores.pagamentoAdesao} onChange={(v) => set("pagamentoAdesao", v)} />
            </div>
            <div>
              <Label htmlFor="venda-total" required={ehVenda}>
                Total (R$)
              </Label>
              <MoneyInput id="venda-total" value={valores.total} onChange={(v) => set("total", v)} />
            </div>
          </div>
        </div>

        <div className="border-t border-[var(--border)] pt-4">
          <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">Anexos</h3>
          <div className="grid gap-4 sm:grid-cols-2">
            <CampoArquivo
              id="venda-termo-adesao"
              label="Termo de adesão"
              arquivo={termoArquivo}
              onSelecionar={setTermoArquivo}
              obrigatorio={ehVenda && !oportunidadeEditar?.termoAdesaoArquivoUrl}
              jaEnviado={!!oportunidadeEditar?.termoAdesaoArquivoUrl}
            />
            <CampoArquivo
              id="venda-pagamento-adesao"
              label="Comprovante de pagamento da adesão"
              arquivo={pagamentoArquivo}
              onSelecionar={setPagamentoArquivo}
              obrigatorio={ehVenda && !oportunidadeEditar?.pagamentoAdesaoArquivoUrl && !pagamentoAgendado}
              jaEnviado={!!oportunidadeEditar?.pagamentoAdesaoArquivoUrl}
            />
            {ehVenda && !temComprovantePagamento && (
              <div className="sm:col-start-2">
                <Label htmlFor="venda-data-pagamento-adesao">Adesão será paga em</Label>
                <Input
                  id="venda-data-pagamento-adesao"
                  type="date"
                  min={oportunidadeEditar ? undefined : hojeLocal()}
                  value={valores.dataPagamentoAdesaoPrevista ?? ""}
                  onChange={(e) => set("dataPagamentoAdesaoPrevista", e.target.value || null)}
                />
                <p className="mt-1 text-xs text-[var(--fg-muted)]">
                  Se a adesão não foi paga hoje, marque a data: o comprovante fica dispensado e você recebe um lembrete nesse dia.
                </p>
              </div>
            )}
          </div>
        </div>

        <div className="flex justify-end gap-2 border-t border-[var(--border)] pt-4">
          <Button variant="secondary" onClick={onCancel} disabled={enviandoArquivos || enviando}>
            Cancelar
          </Button>
          <Button loading={enviandoArquivos || enviando} disabled={!podeConfirmar} onClick={confirmar}>
            {!ehVenda ? "Criar oportunidade" : oportunidadeEditar ? "Salvar alterações" : "Confirmar e mover"}
          </Button>
        </div>
      </div>
    </Modal>
  );
}

function CampoArquivo({
  id,
  label,
  arquivo,
  onSelecionar,
  obrigatorio = true,
  jaEnviado = false,
}: {
  id: string;
  label: string;
  arquivo: File | null;
  onSelecionar: (arquivo: File | null) => void;
  obrigatorio?: boolean;
  jaEnviado?: boolean;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <div>
      <Label htmlFor={id} required={obrigatorio}>
        {label}
      </Label>
      <input
        ref={inputRef}
        id={id}
        type="file"
        accept="application/pdf,image/png,image/jpeg,image/webp"
        className="hidden"
        onChange={(e) => onSelecionar(e.target.files?.[0] ?? null)}
      />
      <button
        type="button"
        onClick={() => inputRef.current?.click()}
        className="focus-ring flex w-full items-center gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm text-[var(--fg)] hover:bg-[var(--surface-hover)] cursor-pointer"
      >
        <Paperclip className="size-4 shrink-0 text-[var(--fg-muted)]" />
        <span className="truncate">
          {arquivo ? arquivo.name : jaEnviado ? "Arquivo já enviado — selecione outro para substituir" : "Selecionar arquivo (PDF, JPG, PNG ou WEBP)"}
        </span>
      </button>
    </div>
  );
}
