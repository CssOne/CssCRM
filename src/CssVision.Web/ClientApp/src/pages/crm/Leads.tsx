import { Download, LayoutGrid, Plus, Upload, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiRequestError, downloadUrl, isAbortError, toQueryString } from "../../lib/api";
import { formatarData, formatarTelefone } from "../../lib/format";
import { type LeadCreateRequest, type LeadDuplicateWarning, type LeadListItem, type LeadStage, type PagedResult } from "../../lib/types";
import { useAuth } from "../../context/AuthContext";
import {
  Badge,
  Button,
  Checkbox,
  EmptyState,
  ErrorState,
  Input,
  Modal,
  Pagination,
  Select,
  Skeleton,
  useToast,
} from "../../components/ui";
import { LeadForm, leadFormVazio, type LeadFormValues } from "../../components/crm/LeadForm";
import { ImportModal } from "../../components/crm/ImportModal";
import { AssignModal } from "../../components/crm/AssignModal";

function paraRequest(v: LeadFormValues): LeadCreateRequest {
  return {
    nomeOuRazaoSocial: v.nomeOuRazaoSocial,
    tipoPessoa: v.tipoPessoa,
    documento: v.documento || null,
    telefone: v.telefone || null,
    whatsApp: v.whatsApp || null,
    email: v.email || null,
    cidade: v.cidade || null,
    estado: v.estado || null,
    regional: v.regional || null,
    origem: v.origem || null,
    campanha: v.campanha || null,
    produtoInteresse: v.produtoInteresse || null,
    gclid: v.gclid || null,
    utmMedium: v.utmMedium || null,
    utmSource: v.utmSource || null,
    utmTerm: v.utmTerm || null,
    metaClickId: v.metaClickId || null,
    metaFormId: v.metaFormId || null,
    metaLeadId: v.metaLeadId || null,
    indicadoPorLeadId: v.indicadoPorLeadId || null,
    tipoIndicacao: v.tipoIndicacao || null,
    tags: v.tags ? v.tags.split(",").map((t) => t.trim()).filter(Boolean) : [],
    observacoes: v.observacoes || null,
    consentimentoContato: v.consentimentoContato,
  };
}

export function LeadsPage() {
  const { temPapel } = useAuth();
  const podeGerir = temPapel("Admin", "GestorMaster", "GestorComercial");
  const { notificar } = useToast();

  const [busca, setBusca] = useState("");
  const [leadEtapaId, setLeadEtapaId] = useState("");
  const [origem, setOrigem] = useState("");
  const [pagina, setPagina] = useState(1);
  const [etapas, setEtapas] = useState<LeadStage[]>([]);

  const [dados, setDados] = useState<PagedResult<LeadListItem> | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [selecionados, setSelecionados] = useState<Set<string>>(new Set());

  const [modalNovo, setModalNovo] = useState(false);
  const [modalImportar, setModalImportar] = useState(false);
  const [modalAtribuir, setModalAtribuir] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [duplicidade, setDuplicidade] = useState<LeadDuplicateWarning | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  const filtro = useMemo(
    () => ({ busca: busca || undefined, leadEtapaId: leadEtapaId || undefined, origem: origem || undefined, pagina, tamanhoPagina: 20 }),
    [busca, leadEtapaId, origem, pagina]
  );

  useEffect(() => {
    api.get<LeadStage[]>("/crm/settings/lead-stages").then(setEtapas).catch(() => setEtapas([]));
  }, []);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setCarregando(true);
      setErro(null);
      api
        .get<PagedResult<LeadListItem>>(`/crm/leads${toQueryString(filtro)}`, signal)
        .then((res) => {
          setDados(res);
          setSelecionados(new Set());
        })
        .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os leads."); })
        .finally(() => setCarregando(false));
    },
    [filtro]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  function limparFiltros() {
    setBusca("");
    setLeadEtapaId("");
    setOrigem("");
    setPagina(1);
  }

  function alternarSelecao(id: string) {
    setSelecionados((atual) => {
      const novo = new Set(atual);
      novo.has(id) ? novo.delete(id) : novo.add(id);
      return novo;
    });
  }

  async function criarLead(valores: LeadFormValues, ignorarDuplicidade = false) {
    setSalvando(true);
    setDuplicidade(null);
    try {
      await api.post("/crm/leads", { ...paraRequest(valores), ignorarDuplicidade } satisfies LeadCreateRequest);
      setModalNovo(false);
      notificar("success", "Lead cadastrado com sucesso.");
      setRecarregar((n) => n + 1);
    } catch (e) {
      if (e instanceof ApiRequestError && e.codigo === "duplicidade") {
        setDuplicidade(e.detalhes as LeadDuplicateWarning);
      } else {
        notificar("error", e instanceof Error ? e.message : "Não foi possível cadastrar o lead.");
      }
    } finally {
      setSalvando(false);
    }
  }

  async function atribuirSelecionados(vendedorId: string, motivo: string) {
    try {
      await api.post("/crm/leads/bulk-assign", { leadIds: Array.from(selecionados), responsavelId: vendedorId, motivo: motivo || null });
      notificar("success", `${selecionados.size} lead(s) atribuído(s).`);
      setModalAtribuir(false);
      setRecarregar((n) => n + 1);
    } catch {
      notificar("error", "Não foi possível atribuir os leads selecionados.");
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-[var(--fg)]">Leads</h1>
          <p className="text-sm text-[var(--fg-muted)]">{dados?.totalRegistros ?? 0} lead(s) na sua carteira.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link to="/app/crm/leads/kanban">
            <Button variant="secondary" type="button">
              <LayoutGrid className="size-4" /> Quadro
            </Button>
          </Link>
          {podeGerir && (
            <Button variant="secondary" onClick={() => setModalImportar(true)}>
              <Upload className="size-4" /> Importar
            </Button>
          )}
          <a href={downloadUrl("/crm/leads/export", filtro)}>
            <Button variant="secondary" type="button">
              <Download className="size-4" /> Exportar
            </Button>
          </a>
          <Button onClick={() => setModalNovo(true)}>
            <Plus className="size-4" /> Novo lead
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap items-end gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
        <div className="min-w-48 flex-1">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Buscar</label>
          <Input
            placeholder="Nome, documento, telefone ou e-mail"
            value={busca}
            onChange={(e) => {
              setBusca(e.target.value);
              setPagina(1);
            }}
          />
        </div>
        <div className="w-44">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Etapa</label>
          <Select
            value={leadEtapaId}
            onChange={(e) => {
              setLeadEtapaId(e.target.value);
              setPagina(1);
            }}
          >
            <option value="">Todas</option>
            {etapas.map((etapa) => (
              <option key={etapa.id ?? ""} value={etapa.id ?? ""}>
                {etapa.nome}
              </option>
            ))}
          </Select>
        </div>
        <div className="w-44">
          <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Origem</label>
          <Input value={origem} onChange={(e) => { setOrigem(e.target.value); setPagina(1); }} />
        </div>
        {(busca || leadEtapaId || origem) && (
          <Button variant="ghost" size="sm" onClick={limparFiltros}>
            <X className="size-4" /> Limpar filtros
          </Button>
        )}
      </div>

      {podeGerir && selecionados.size > 0 && (
        <div className="flex items-center justify-between rounded-lg bg-[var(--brand-soft)] px-4 py-2 text-sm text-[var(--brand)]">
          <span>{selecionados.size} lead(s) selecionado(s)</span>
          <Button size="sm" onClick={() => setModalAtribuir(true)}>
            Atribuir
          </Button>
        </div>
      )}

      {carregando ? (
        <div className="space-y-2">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-14" />
          ))}
        </div>
      ) : erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !dados || dados.itens.length === 0 ? (
        <EmptyState title="Nenhum lead encontrado" description="Ajuste os filtros ou cadastre um novo lead." />
      ) : (
        <>
          <div className="hidden overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--surface)] lg:block">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                  {podeGerir && <th className="w-10 px-4 py-3" />}
                  <th className="px-2 py-3 font-medium">Nome</th>
                  <th className="px-2 py-3 font-medium">Contato</th>
                  <th className="px-2 py-3 font-medium">Responsável</th>
                  <th className="px-2 py-3 font-medium">Etapa</th>
                  <th className="px-2 py-3 font-medium">Oportunidade</th>
                  <th className="px-2 py-3 font-medium">Criado em</th>
                </tr>
              </thead>
              <tbody>
                {dados.itens.map((lead) => (
                  <tr key={lead.id} className="border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-hover)]">
                    {podeGerir && (
                      <td className="px-4 py-3">
                        <Checkbox label="" checked={selecionados.has(lead.id)} onChange={() => alternarSelecao(lead.id)} />
                      </td>
                    )}
                    <td className="px-2 py-3">
                      <Link to={`/app/crm/leads/${lead.id}`} className="focus-ring font-medium text-[var(--fg)] hover:text-[var(--brand)]">
                        {lead.nomeOuRazaoSocial}
                      </Link>
                      {lead.semContato && (
                        <Badge variant="warning">
                          <span className="ml-1">sem contato</span>
                        </Badge>
                      )}
                    </td>
                    <td className="px-2 py-3 text-[var(--fg-muted)]">
                      <div>{formatarTelefone(lead.telefone)}</div>
                      <div className="text-xs">{lead.email}</div>
                    </td>
                    <td className="px-2 py-3 text-[var(--fg-muted)]">{lead.responsavelNome ?? "—"}</td>
                    <td className="px-2 py-3">
                      <Badge variant="neutral">
                        <span className="mr-1 inline-block size-2 rounded-full" style={{ backgroundColor: lead.etapaCor ?? "#94a3b8" }} />
                        {lead.etapaNome ?? "Sem etapa"}
                      </Badge>
                    </td>
                    <td className="px-2 py-3 text-[var(--fg-muted)]">{lead.etapaAtual ?? "—"}</td>
                    <td className="px-2 py-3 text-[var(--fg-muted)]">{formatarData(lead.criadoEm)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="space-y-2 lg:hidden">
            {dados.itens.map((lead) => (
              <Link
                key={lead.id}
                to={`/app/crm/leads/${lead.id}`}
                className="focus-ring block rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3"
              >
                <div className="flex items-center justify-between">
                  <span className="font-medium text-[var(--fg)]">{lead.nomeOuRazaoSocial}</span>
                  <Badge variant="neutral">{lead.etapaNome ?? "Sem etapa"}</Badge>
                </div>
                <p className="mt-1 text-xs text-[var(--fg-muted)]">
                  {lead.responsavelNome ?? "Sem responsável"} · {formatarTelefone(lead.telefone)}
                </p>
              </Link>
            ))}
          </div>

          <Pagination pagina={dados.pagina} totalPaginas={dados.totalPaginas} onChange={setPagina} />
        </>
      )}

      <Modal open={modalNovo} onClose={() => { setModalNovo(false); setDuplicidade(null); }} title="Novo lead" size="lg">
        {duplicidade && (
          <div className="mb-4 flex items-center justify-between gap-3 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-sm text-[var(--warning)]">
            <span>
              Já existe um lead com o mesmo {duplicidade.campoDuplicado}: <strong>{duplicidade.nomeExistente}</strong>.
            </span>
            <Link to={`/app/crm/leads/${duplicidade.leadExistenteId}`} className="underline shrink-0">
              Abrir cadastro
            </Link>
          </div>
        )}
        <LeadForm valoresIniciais={leadFormVazio} salvando={salvando} onSubmit={(v) => criarLead(v)} onCancel={() => setModalNovo(false)} idPrefix="novo" />
      </Modal>

      <ImportModal open={modalImportar} onClose={() => setModalImportar(false)} onImportado={() => setRecarregar((n) => n + 1)} />

      <AssignModal open={modalAtribuir} quantidade={selecionados.size} onClose={() => setModalAtribuir(false)} onConfirm={atribuirSelecionados} />
    </div>
  );
}
