import { useEffect, useRef, useState } from "react";
import { Paperclip } from "lucide-react";
import { api, ApiRequestError, uploadFile } from "../../lib/api";
import type { ChangeStageRequest, LeadDetail, Opportunity } from "../../lib/types";
import { Button, Checkbox, CpfInput, Input, Label, Modal, MoneyInput, useToast } from "../ui";

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
};

/**
 * Dois modos de uso:
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

  useEffect(() => {
    if (!open || !leadId) return;
    setValores({ ...valoresIniciais, valorFinal: valorEstimado, rowVersion: rowVersionInicial });
    setTermoArquivo(null);
    setPagamentoArquivo(null);
    setOportunidadeResolvidaId(opportunityId);
    setResponsavelIdLead(null);

    const controller = new AbortController();
    api
      .get<LeadDetail>(`/crm/leads/${leadId}`, controller.signal)
      .then((lead) => {
        setValores((v) => ({
          ...v,
          cpf: lead.documento ?? "",
          estado: lead.estado ?? "",
          tipoIndicacao: lead.tipoIndicacao ?? "",
          veiculo: { ...v.veiculo, placa: lead.placa ?? "" },
        }));
        setResponsavelIdLead(lead.responsavelId ?? null);
        if (!opportunityId) {
          setOportunidadeResolvidaId(lead.oportunidades.find((o) => o.ativa)?.id ?? null);
        }
      })
      .catch(() => {});
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, leadId]);

  if (!open) return null;

  function set<K extends keyof DadosVendaConcluida>(campo: K, valor: DadosVendaConcluida[K]) {
    setValores((v) => ({ ...v, [campo]: valor }));
  }

  function setVeiculo(campo: keyof NonNullable<DadosVendaConcluida["veiculo"]>, valor: string | number | null) {
    setValores((v) => ({ ...v, veiculo: { ...v.veiculo, [campo]: valor } }));
  }

  const podeConfirmar =
    (valores.valorFinal ?? -1) >= 0 && !!valores.dataEfetivaFechamento && !!termoArquivo && !!pagamentoArquivo && !enviandoArquivos && !enviando;

  async function confirmar() {
    if (!leadId || !termoArquivo || !pagamentoArquivo) return;
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

      const r1 = await uploadFile<Opportunity>(`/crm/opportunities/${idOportunidade}/attachments/termo-adesao`, termoArquivo);
      rowVersionAtual = r1.rowVersion;
      const r2 = await uploadFile<Opportunity>(`/crm/opportunities/${idOportunidade}/attachments/pagamento-adesao`, pagamentoArquivo);
      rowVersionAtual = r2.rowVersion;
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível enviar os anexos.");
      setEnviandoArquivos(false);
      return;
    }

    const dadosFinais: DadosVendaConcluida = {
      ...valores,
      rowVersion: rowVersionAtual,
      ativoEm: valores.ativoEm || null,
      veiculo: valores.veiculo ? { ...valores.veiculo, dataChegada: valores.veiculo.dataChegada || null } : null,
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
    <Modal open={open} onClose={onCancel} title={`Concluir venda — mover para "${etapaNome}"`} size="lg">
      <div className="space-y-5">
        <div>
          <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">Dados da venda</h3>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="venda-data" required>
                Data da venda
              </Label>
              <Input id="venda-data" type="date" value={valores.dataEfetivaFechamento ?? ""} onChange={(e) => set("dataEfetivaFechamento", e.target.value)} />
            </div>
            <div>
              <Label htmlFor="venda-valor-final" required>
                Valor final (R$)
              </Label>
              <MoneyInput id="venda-valor-final" value={valores.valorFinal ?? 0} onChange={(v) => set("valorFinal", v ?? 0)} />
            </div>
            <div>
              <Label htmlFor="venda-cpf">CPF</Label>
              <CpfInput id="venda-cpf" value={valores.cpf ?? ""} onChange={(e) => set("cpf", e.target.value)} />
            </div>
            <div>
              <Label htmlFor="venda-estado">Estado</Label>
              <Input id="venda-estado" maxLength={2} value={valores.estado ?? ""} onChange={(e) => set("estado", e.target.value.toUpperCase())} />
            </div>
            <div>
              <Label htmlFor="venda-ativo-em">Ativo em</Label>
              <Input id="venda-ativo-em" type="date" value={valores.ativoEm ?? ""} onChange={(e) => set("ativoEm", e.target.value)} />
            </div>
            <div>
              <Label htmlFor="venda-porcentagem">Porcentagem (%)</Label>
              <Input
                id="venda-porcentagem"
                type="number"
                min={0}
                max={100}
                step="0.01"
                value={valores.porcentagem ?? ""}
                onChange={(e) => set("porcentagem", e.target.value ? Number(e.target.value) : null)}
              />
            </div>
            <div>
              <Label htmlFor="venda-mensalidade">Mensalidade (R$)</Label>
              <MoneyInput id="venda-mensalidade" value={valores.mensalidade} onChange={(v) => set("mensalidade", v)} />
            </div>
            <div>
              <Label htmlFor="venda-mensalidade-desconto">Mensalidade com desconto (R$)</Label>
              <MoneyInput id="venda-mensalidade-desconto" value={valores.mensalidadeComDesconto} onChange={(v) => set("mensalidadeComDesconto", v)} />
            </div>
            <div>
              <Label htmlFor="venda-mensalidade-cupom">Mensalidade com cupom (R$)</Label>
              <MoneyInput id="venda-mensalidade-cupom" value={valores.mensalidadeComCupom} onChange={(v) => set("mensalidadeComCupom", v)} />
            </div>
            <div>
              <Label htmlFor="venda-adesao">Pagamento de adesão (R$)</Label>
              <MoneyInput id="venda-adesao" value={valores.pagamentoAdesao} onChange={(v) => set("pagamentoAdesao", v)} />
            </div>
            <div>
              <Label htmlFor="venda-total">Total (R$)</Label>
              <MoneyInput id="venda-total" value={valores.total} onChange={(v) => set("total", v)} />
            </div>
          </div>

          <div className="mt-3 flex flex-wrap items-end gap-4">
            <Checkbox label="É uma migração" checked={valores.migracao ?? false} onChange={(e) => set("migracao", e.target.checked)} />
            <Checkbox label="Teve indicação" checked={valores.indicacao ?? false} onChange={(e) => set("indicacao", e.target.checked)} />
            {valores.indicacao && (
              <>
                <div className="w-48">
                  <Label htmlFor="venda-tipo-indicacao">Tipo de indicação</Label>
                  <Input id="venda-tipo-indicacao" value={valores.tipoIndicacao ?? ""} onChange={(e) => set("tipoIndicacao", e.target.value)} />
                </div>
                <div className="w-40">
                  <Label htmlFor="venda-valor-indicacao">Valor da indicação (R$)</Label>
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
              <Label htmlFor="venda-veiculo-descricao">Veículo (marca/modelo)</Label>
              <Input id="venda-veiculo-descricao" value={valores.veiculo?.descricao ?? ""} onChange={(e) => setVeiculo("descricao", e.target.value)} />
            </div>
            <div>
              <Label htmlFor="venda-veiculo-placa">Placa</Label>
              <Input id="venda-veiculo-placa" value={valores.veiculo?.placa ?? ""} onChange={(e) => setVeiculo("placa", e.target.value.toUpperCase())} />
            </div>
            <div>
              <Label htmlFor="venda-veiculo-fipe">Valor FIPE (R$)</Label>
              <MoneyInput id="venda-veiculo-fipe" value={valores.veiculo?.fipe} onChange={(v) => setVeiculo("fipe", v)} />
            </div>
            <div>
              <Label htmlFor="venda-veiculo-rastreador">Custo do rastreador (R$)</Label>
              <MoneyInput id="venda-veiculo-rastreador" value={valores.veiculo?.rastreador ?? null} onChange={(v) => setVeiculo("rastreador", v)} />
            </div>
            <div>
              <Label htmlFor="venda-veiculo-valor-vistoria">Custo da vistoria (R$)</Label>
              <MoneyInput id="venda-veiculo-valor-vistoria" value={valores.veiculo?.valorVistoria ?? null} onChange={(v) => setVeiculo("valorVistoria", v)} />
            </div>
            <div>
              <Label htmlFor="venda-veiculo-chegada">Data de chegada</Label>
              <Input
                id="venda-veiculo-chegada"
                type="date"
                value={valores.veiculo?.dataChegada?.slice(0, 10) ?? ""}
                onChange={(e) => setVeiculo("dataChegada", e.target.value)}
              />
            </div>
          </div>
        </div>

        <div className="border-t border-[var(--border)] pt-4">
          <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">Anexos</h3>
          <div className="grid gap-4 sm:grid-cols-2">
            <CampoArquivo id="venda-termo-adesao" label="Termo de adesão" arquivo={termoArquivo} onSelecionar={setTermoArquivo} />
            <CampoArquivo id="venda-pagamento-adesao" label="Comprovante de pagamento da adesão" arquivo={pagamentoArquivo} onSelecionar={setPagamentoArquivo} />
          </div>
        </div>

        <div className="flex justify-end gap-2 border-t border-[var(--border)] pt-4">
          <Button variant="secondary" onClick={onCancel} disabled={enviandoArquivos || enviando}>
            Cancelar
          </Button>
          <Button loading={enviandoArquivos || enviando} disabled={!podeConfirmar} onClick={confirmar}>
            Confirmar e mover
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
}: {
  id: string;
  label: string;
  arquivo: File | null;
  onSelecionar: (arquivo: File | null) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <div>
      <Label htmlFor={id} required>
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
        <span className="truncate">{arquivo ? arquivo.name : "Selecionar arquivo (PDF, JPG, PNG ou WEBP)"}</span>
      </button>
    </div>
  );
}
