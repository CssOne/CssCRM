import { Download, LayoutGrid, Plus, Upload, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiRequestError, downloadUrl, isAbortError, toQueryString } from "../../lib/api";
import { formatarData, formatarMoeda, formatarPercentual, formatarTelefone } from "../../lib/format";
import {
  type GrupoFiltro,
  type LeadCreateRequest,
  type LeadDuplicateWarning,
  type LeadListItem,
  type LeadTotais,
  type LeadStage,
  type PagedResult,
  type Regional,
  type VendedorResumo,
} from "../../lib/types";
import { MultiSelect } from "../../components/MultiSelect";
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
  Skeleton,
  useToast,
} from "../../components/ui";
import { LeadForm, leadFormVazio, paraLeadCreateRequest, type LeadFormValues } from "../../components/crm/LeadForm";
import { ImportModal } from "../../components/crm/ImportModal";
import { AssignModal } from "../../components/crm/AssignModal";

export function LeadsPage() {
  const { temPapel } = useAuth();
  const podeGerir = temPapel("Admin", "GestorMaster", "SupervisorComercial", "GestorComercial");
  // Filtro por Origem só para administradores (o campo não é enviado aos demais).
  const podeVerOrigem = temPapel("Admin", "GestorMaster", "SupervisorComercial");
  const { notificar } = useToast();

  const [busca, setBusca] = useState("");
  // Filtros de múltipla escolha: nada marcado = todos.
  const [etapaIds, setEtapaIds] = useState<string[]>([]);
  const [origens, setOrigens] = useState<string[]>([]);
  const [consultorIds, setConsultorIds] = useState<string[]>([]);
  const [regionais, setRegionais] = useState<string[]>([]);
  const [grupoIds, setGrupoIds] = useState<string[]>([]);
  const [chegadaDe, setChegadaDe] = useState("");
  const [chegadaAte, setChegadaAte] = useState("");
  const [vendaDe, setVendaDe] = useState("");
  const [vendaAte, setVendaAte] = useState("");
  const [pagina, setPagina] = useState(1);
  const [etapas, setEtapas] = useState<LeadStage[]>([]);
  const [listaOrigens, setListaOrigens] = useState<string[]>([]);
  const [consultores, setConsultores] = useState<VendedorResumo[]>([]);
  const [listaRegionais, setListaRegionais] = useState<Regional[]>([]);
  const [listaGrupos, setListaGrupos] = useState<GrupoFiltro[]>([]);

  const [dados, setDados] = useState<PagedResult<LeadListItem> | null>(null);
  // Rodapé: contagem e somas de TODOS os leads que batem com os filtros (não só os da página).
  const [totais, setTotais] = useState<LeadTotais | null>(null);
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
    () => ({
      busca: busca || undefined,
      leadEtapaIds: etapaIds,
      origens,
      responsavelIds: consultorIds,
      regionais,
      grupoIds,
      dataInicio: chegadaDe || undefined,
      dataFim: chegadaAte || undefined,
      dataVendaInicio: vendaDe || undefined,
      dataVendaFim: vendaAte || undefined,
      pagina,
      tamanhoPagina: 20,
    }),
    [busca, etapaIds, origens, consultorIds, regionais, grupoIds, chegadaDe, chegadaAte, vendaDe, vendaAte, pagina]
  );

  useEffect(() => {
    api.get<LeadStage[]>("/crm/settings/lead-stages").then(setEtapas).catch(() => setEtapas([]));
    if (podeVerOrigem) api.get<string[]>("/crm/settings/origins").then(setListaOrigens).catch(() => setListaOrigens([]));
    if (podeGerir) {
      api.get<VendedorResumo[]>("/crm/management/vendedores?incluirInativos=true").then(setConsultores).catch(() => setConsultores([]));
      api.get<Regional[]>("/crm/settings/regionals").then(setListaRegionais).catch(() => setListaRegionais([]));
      // Grupos já criados e os que forem criados depois, de todas as regionais visíveis.
      api.get<GrupoFiltro[]>("/crm/settings/groups/filtro").then(setListaGrupos).catch(() => setListaGrupos([]));
    }
  }, [podeGerir, podeVerOrigem]);

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
        .finally(() => { if (!signal?.aborted) setCarregando(false); });
    },
    [filtro]
  );

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar, recarregar]);

  // Mesmos filtros, sem a paginação.
  const filtroTotais = useMemo(() => {
    const { pagina: _pagina, tamanhoPagina: _tamanho, ...resto } = filtro;
    return resto;
  }, [filtro]);
  useEffect(() => {
    const controller = new AbortController();
    api
      .get<LeadTotais>(`/crm/leads/totais${toQueryString(filtroTotais)}`, controller.signal)
      .then(setTotais)
      .catch((e) => { if (!isAbortError(e)) setTotais(null); });
    return () => controller.abort();
  }, [filtroTotais, recarregar]);

  function limparFiltros() {
    setBusca("");
    setEtapaIds([]);
    setOrigens([]);
    setConsultorIds([]);
    setRegionais([]);
    setGrupoIds([]);
    setChegadaDe("");
    setChegadaAte("");
    setVendaDe("");
    setVendaAte("");
    setPagina(1);
  }

  // Qualquer mudança de filtro volta para a primeira página.
  function comPagina1<T>(definir: (v: T) => void) {
    return (v: T) => {
      definir(v);
      setPagina(1);
    };
  }

  const filtrosAtivos = !!(
    busca || etapaIds.length || origens.length || consultorIds.length || regionais.length || grupoIds.length ||
    chegadaDe || chegadaAte || vendaDe || vendaAte
  );
  const dinheiro = (n?: number | null) => (n == null ? "—" : formatarMoeda(n));

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
      await api.post("/crm/leads", { ...paraLeadCreateRequest(valores), ignorarDuplicidade } satisfies LeadCreateRequest);
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

      <div className="space-y-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
        <div className="flex flex-wrap items-end gap-3">
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
          <div className="w-52">
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Etapa</label>
            <MultiSelect
              ariaLabel="Etapa"
              rotuloTodos="Todas"
              opcoes={[
                ...etapas.filter((e) => e.ativa !== false && e.id).map((e) => ({ valor: e.id!, rotulo: e.nome })),
                { valor: "00000000-0000-0000-0000-000000000000", rotulo: "Sem etapa" },
              ]}
              valores={etapaIds}
              onChange={comPagina1(setEtapaIds)}
            />
          </div>
          {podeGerir && (
            <div className="w-52">
              <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Consultor</label>
              <MultiSelect
                ariaLabel="Consultor"
                rotuloTodos="Todos"
                opcoes={consultores.map((c) => ({ valor: c.id, rotulo: c.ativo === false ? `${c.nome} (inativo)` : c.nome }))}
                valores={consultorIds}
                onChange={comPagina1(setConsultorIds)}
              />
            </div>
          )}
          {podeVerOrigem && (
            <div className="w-52">
              <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Origem</label>
              <MultiSelect
                ariaLabel="Origem"
                rotuloTodos="Todas"
                opcoes={listaOrigens.map((o) => ({ valor: o, rotulo: o }))}
                valores={origens}
                onChange={comPagina1(setOrigens)}
              />
            </div>
          )}
          {podeGerir && (
            <>
              <div className="w-44">
                <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Regional</label>
                <MultiSelect
                  ariaLabel="Regional"
                  rotuloTodos="Todas"
                  opcoes={listaRegionais.map((r) => ({ valor: r.nome, rotulo: r.nome }))}
                  valores={regionais}
                  onChange={comPagina1(setRegionais)}
                />
              </div>
              <div className="w-56">
                <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Grupo</label>
                <MultiSelect
                  ariaLabel="Grupo"
                  rotuloTodos="Todos"
                  opcoes={listaGrupos
                    .filter((g) => regionais.length === 0 || regionais.includes(g.regionalNome))
                    .map((g) => ({ valor: g.id, rotulo: `${g.regionalNome} · ${g.nome}` }))}
                  valores={grupoIds}
                  onChange={comPagina1(setGrupoIds)}
                />
              </div>
            </>
          )}
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <div>
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Chegada de</label>
            <Input type="date" className="w-40" value={chegadaDe} max={chegadaAte || undefined} onChange={(e) => comPagina1(setChegadaDe)(e.target.value)} />
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">até</label>
            <Input type="date" className="w-40" value={chegadaAte} min={chegadaDe || undefined} onChange={(e) => comPagina1(setChegadaAte)(e.target.value)} />
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">Venda de</label>
            <Input type="date" className="w-40" value={vendaDe} max={vendaAte || undefined} onChange={(e) => comPagina1(setVendaDe)(e.target.value)} />
          </div>
          <div>
            <label className="mb-1 block text-xs font-medium text-[var(--fg-muted)]">até</label>
            <Input type="date" className="w-40" value={vendaAte} min={vendaDe || undefined} onChange={(e) => comPagina1(setVendaAte)(e.target.value)} />
          </div>
          {filtrosAtivos && (
            <Button variant="ghost" size="sm" onClick={limparFiltros}>
              <X className="size-4" /> Limpar filtros
            </Button>
          )}
        </div>
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
                  <th className="px-2 py-3 text-right font-medium">Adesão</th>
                  <th className="px-2 py-3 text-right font-medium">FIPE</th>
                  <th className="px-2 py-3 text-right font-medium">Mensalidade</th>
                  <th className="px-2 py-3 text-right font-medium" title="Mensalidade com desconto">Mens. c/ desconto</th>
                  <th className="px-2 py-3 text-right font-medium">%</th>
                  <th className="px-2 py-3 text-right font-medium">Rastreador</th>
                  <th className="px-2 py-3 text-right font-medium">Indicação</th>
                  <th className="px-2 py-3 text-right font-medium">Vistoria</th>
                  <th className="px-2 py-3 text-right font-medium">Total</th>
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
                    <td className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg-muted)]">{dinheiro(lead.venda?.adesao)}</td>
                    <td className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg-muted)]">{dinheiro(lead.venda?.fipe)}</td>
                    <td className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg-muted)]">{dinheiro(lead.venda?.mensalidade)}</td>
                    <td className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg-muted)]">{dinheiro(lead.venda?.mensalidadeComDesconto)}</td>
                    <td className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg-muted)]">
                      {lead.venda?.porcentagem == null ? "—" : formatarPercentual(lead.venda.porcentagem)}
                    </td>
                    <td className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg-muted)]">{dinheiro(lead.venda?.rastreador)}</td>
                    <td className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg-muted)]">{dinheiro(lead.venda?.indicacao)}</td>
                    <td className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg-muted)]">{dinheiro(lead.venda?.vistoria)}</td>
                    <td className="whitespace-nowrap px-2 py-3 text-right font-medium text-[var(--fg)]">{dinheiro(lead.venda?.total)}</td>
                    <td className="px-2 py-3 text-[var(--fg-muted)]">{formatarData(lead.criadoEm)}</td>
                  </tr>
                ))}
              </tbody>
              {totais && (
                <tfoot>
                  <tr className="border-t-2 border-[var(--border)] bg-[var(--surface-hover)]/60 text-xs">
                    <td colSpan={(podeGerir ? 1 : 0) + 5} className="px-2 py-3 font-medium text-[var(--fg)]">
                      <span className="mr-1 text-[10px] uppercase tracking-wide text-[var(--fg-muted)]">Contagem</span>
                      {totais.contagem.toLocaleString("pt-BR")}
                    </td>
                    {[totais.adesao, totais.fipe, totais.mensalidade, totais.mensalidadeComDesconto].map((v, i) => (
                      <td key={i} className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg)]">
                        <span className="mr-1 text-[10px] uppercase tracking-wide text-[var(--fg-muted)]">Soma</span>
                        {formatarMoeda(v)}
                      </td>
                    ))}
                    <td className="px-2 py-3" />
                    {[totais.rastreador, totais.indicacao, totais.vistoria, totais.total].map((v, i) => (
                      <td key={i} className="whitespace-nowrap px-2 py-3 text-right text-[var(--fg)]">
                        <span className="mr-1 text-[10px] uppercase tracking-wide text-[var(--fg-muted)]">Soma</span>
                        {formatarMoeda(v)}
                      </td>
                    ))}
                    <td className="px-2 py-3" />
                  </tr>
                </tfoot>
              )}
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

          {totais && (
            <p className="text-xs text-[var(--fg-muted)] lg:hidden">
              <span className="uppercase tracking-wide">Contagem</span> {totais.contagem.toLocaleString("pt-BR")} ·{" "}
              <span className="uppercase tracking-wide">Soma total</span> {formatarMoeda(totais.total)}
            </p>
          )}

          <Pagination pagina={dados.pagina} totalPaginas={dados.totalPaginas} onChange={setPagina} />
        </>
      )}

      <Modal open={modalNovo} onClose={() => { setModalNovo(false); setDuplicidade(null); }} title="Novo lead" size="lg">
        {duplicidade && (
          <div className="mb-4 flex items-center justify-between gap-3 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-sm text-[var(--warning)]">
            <span>
              Já existe um lead com o mesmo {duplicidade.campoDuplicado}: <strong>{duplicidade.nomeExistente}</strong>.
              {duplicidade.campoDuplicado !== "telefone" && " Se o cliente fechou outro carro, registre como outro veículo dele."}
            </span>
            <div className="flex shrink-0 items-center gap-3">
              {duplicidade.campoDuplicado !== "telefone" && (
                <Link to={`/app/crm/leads/${duplicidade.leadExistenteId}?outroVeiculo=1`} className="font-semibold underline">
                  Outro veículo deste cliente
                </Link>
              )}
              <Link to={`/app/crm/leads/${duplicidade.leadExistenteId}`} className="underline">
                Abrir cadastro
              </Link>
            </div>
          </div>
        )}
        <LeadForm valoresIniciais={leadFormVazio} salvando={salvando} onSubmit={(v) => criarLead(v)} onCancel={() => setModalNovo(false)} idPrefix="novo" />
      </Modal>

      <ImportModal open={modalImportar} onClose={() => setModalImportar(false)} onImportado={() => setRecarregar((n) => n + 1)} />

      <AssignModal open={modalAtribuir} quantidade={selecionados.size} onClose={() => setModalAtribuir(false)} onConfirm={atribuirSelecionados} />
    </div>
  );
}
