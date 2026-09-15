// Tipos espelhando os DTOs de src/CssVision.Web/Api/Contracts — mantenha em sincronia com o backend.

export enum TipoPessoa {
  Fisica = 1,
  Juridica = 2,
}

export enum TipoEtapaPipeline {
  Aberta = 1,
  Ganho = 2,
  Perdido = 3,
}

export enum TipoAtividade {
  Ligacao = 1,
  WhatsApp = 2,
  Email = 3,
  Reuniao = 4,
  Visita = 5,
  Retorno = 6,
  Tarefa = 7,
  Observacao = 8,
}

export enum StatusAtividade {
  Pendente = 1,
  Concluida = 2,
  Cancelada = 3,
}

export enum TipoEventoTimeline {
  LeadCriado = 1,
  LeadAtualizado = 2,
  TrocaResponsavel = 3,
  MudancaEtapa = 4,
  Atividade = 5,
  Nota = 6,
  Proposta = 7,
  OportunidadeGanha = 8,
  OportunidadePerdida = 9,
  OportunidadeCriada = 10,
}

export enum VisaoAtividade {
  Minhas = 1,
  Hoje = 2,
  Proximas = 3,
  Atrasadas = 4,
  Concluidas = 5,
  Semana = 6,
}

export interface PagedResult<T> {
  itens: T[];
  pagina: number;
  tamanhoPagina: number;
  totalRegistros: number;
  totalPaginas: number;
}

export interface ApiError {
  mensagem: string;
  codigo?: string | null;
  detalhes?: unknown;
}

export interface MenuItem {
  chave: string;
  rotulo: string;
  icone: string;
  rota: string;
}

export interface Session {
  id: string;
  email: string;
  nomeCompleto: string;
  fotoUrl?: string | null;
  papeis: string[];
  areaInicial: string;
  menu: MenuItem[];
}

export interface LoginResult {
  requerDoisFatores: boolean;
  sessao: Session | null;
}

export interface Profile {
  id: string;
  nomeCompleto: string;
  email: string;
  telefone?: string | null;
  fotoUrl?: string | null;
  doisFatoresAtivo: boolean;
  papeis: string[];
  podeExcluirPropriaConta: boolean;
}

export interface UpdateProfileRequest {
  nomeCompleto: string;
  email: string;
  telefone?: string | null;
}

export interface TwoFactorSetup {
  chaveManual: string;
  uriQrCode: string;
}

export interface TwoFactorEnableResult {
  codigosRecuperacao: string[];
}

// --- Leads ---

export interface LeadListItem {
  id: string;
  nomeOuRazaoSocial: string;
  tipoPessoa: TipoPessoa;
  documentoMascarado?: string | null;
  telefone?: string | null;
  email?: string | null;
  cidade?: string | null;
  estado?: string | null;
  regional?: string | null;
  origem?: string | null;
  placa?: string | null;
  temSeguro?: boolean | null;
  utilidadeVeiculo?: string | null;
  etapaId?: string | null;
  etapaNome?: string | null;
  etapaCor?: string | null;
  etapaAtual?: string | null;
  responsavelId?: string | null;
  responsavelNome?: string | null;
  tags: string[];
  criadoEm: string;
  ultimoContatoEm?: string | null;
  proximoContatoEm?: string | null;
  semContato: boolean;
  arquivado: boolean;
}

export interface LeadOpportunitySummary {
  id: string;
  titulo: string;
  etapaNome: string;
  valorEstimado: number;
  dataPrevistaFechamento?: string | null;
  ativa: boolean;
}

export interface LeadDetail {
  id: string;
  nomeOuRazaoSocial: string;
  tipoPessoa: TipoPessoa;
  documento?: string | null;
  telefone?: string | null;
  whatsApp?: string | null;
  email?: string | null;
  dataNascimento?: string | null;
  cidade?: string | null;
  estado?: string | null;
  regional?: string | null;
  origem?: string | null;
  campanha?: string | null;
  produtoInteresse?: string | null;
  placa?: string | null;
  temSeguro?: boolean | null;
  utilidadeVeiculo?: string | null;
  gclid?: string | null;
  utmMedium?: string | null;
  utmSource?: string | null;
  utmTerm?: string | null;
  metaClickId?: string | null;
  metaFormId?: string | null;
  metaLeadId?: string | null;
  indicadoPorLeadId?: string | null;
  indicadoPorLeadNome?: string | null;
  tipoIndicacao?: string | null;
  etapaId?: string | null;
  etapaNome?: string | null;
  etapaCor?: string | null;
  motivoPerdaId?: string | null;
  motivoPerdaDescricao?: string | null;
  motivoPerdaObservacao?: string | null;
  veiculoNaoAtendido?: string | null;
  responsavelId?: string | null;
  responsavelNome?: string | null;
  observacoes?: string | null;
  consentimentoContato: boolean;
  consentimentoDataEm?: string | null;
  consentimentoOrigem?: string | null;
  tags: string[];
  oportunidades: LeadOpportunitySummary[];
  criadoEm: string;
  atualizadoEm?: string | null;
  rowVersion: number;
  arquivado: boolean;
}

export interface LeadCreateRequest {
  nomeOuRazaoSocial: string;
  tipoPessoa: TipoPessoa;
  documento?: string | null;
  telefone?: string | null;
  whatsApp?: string | null;
  email?: string | null;
  dataNascimento?: string | null;
  cidade?: string | null;
  estado?: string | null;
  regional?: string | null;
  origem?: string | null;
  campanha?: string | null;
  produtoInteresse?: string | null;
  placa?: string | null;
  temSeguro?: boolean | null;
  utilidadeVeiculo?: string | null;
  gclid?: string | null;
  utmMedium?: string | null;
  utmSource?: string | null;
  utmTerm?: string | null;
  metaClickId?: string | null;
  metaFormId?: string | null;
  metaLeadId?: string | null;
  indicadoPorLeadId?: string | null;
  tipoIndicacao?: string | null;
  responsavelId?: string | null;
  tags?: string[];
  observacoes?: string | null;
  consentimentoContato: boolean;
  consentimentoOrigem?: string | null;
  ignorarDuplicidade?: boolean;
}

export type LeadUpdateRequest = Omit<LeadCreateRequest, "responsavelId" | "ignorarDuplicidade"> & { rowVersion: number };

export interface LeadDuplicateWarning {
  leadExistenteId: string;
  nomeExistente: string;
  campoDuplicado: string;
}

// --- Quadro de leads (kanban) ---

export interface LeadStage {
  /** Nulo representa a coluna virtual "Sem etapa" (leads novos, ainda não trabalhados). */
  id: string | null;
  nome: string;
  ordem: number;
  cor?: string | null;
  fechada: boolean;
  ativa: boolean;
}

export interface LeadKanbanCard {
  leadId: string;
  nomeOuRazaoSocial: string;
  telefone?: string | null;
  email?: string | null;
  estado?: string | null;
  origem?: string | null;
  campanha?: string | null;
  placa?: string | null;
  temSeguro?: boolean | null;
  utilidadeVeiculo?: string | null;
  responsavelId?: string | null;
  responsavelNome?: string | null;
  tags: string[];
  criadoEm: string;
  ultimoContatoEm?: string | null;
  semContato: boolean;
  arquivado: boolean;
  rowVersion: number;
}

export interface LeadKanbanColumn {
  etapa: LeadStage;
  cartoes: LeadKanbanCard[];
}

export interface LeadKanbanBoard {
  colunas: LeadKanbanColumn[];
}

export interface ChangeLeadStageRequest {
  /** Nulo move o lead de volta pra "Sem etapa" (desmarca). */
  novaEtapaId: string | null;
  rowVersion: number;
  /** Obrigatório quando a nova etapa é "Perdido". */
  motivoPerdaId?: string;
  motivoPerdaObservacao?: string;
  /** Obrigatório quando a nova etapa é "Não fazemos". */
  veiculoNaoAtendido?: string;
}

export interface LeadImportResult {
  totalLinhas: number;
  importados: number;
  duplicados: number;
  comErro: number;
  erros: string[];
}

export interface LeadTimelineItem {
  id: string;
  tipo: TipoEventoTimeline;
  titulo: string;
  descricao?: string | null;
  usuarioNome?: string | null;
  ocorridoEm: string;
}

// --- Oportunidades ---

export interface Opportunity {
  id: string;
  leadId: string;
  leadNome: string;
  titulo: string;
  responsavelId: string;
  responsavelNome: string;
  etapaId: string;
  etapaNome: string;
  etapaTipo: TipoEtapaPipeline;
  etapaDesde: string;
  produtoOuServico?: string | null;
  valorEstimado: number;
  probabilidadeFechamento?: number | null;
  dataPrevistaFechamento?: string | null;
  valorFinal?: number | null;
  dataEfetivaFechamento?: string | null;
  motivoPerdaDescricao?: string | null;
  motivoPerdaObservacao?: string | null;
  concorrente?: string | null;
  observacoes?: string | null;
  dataAdesao?: string | null;
  ativoEm?: string | null;
  mensalidade?: number | null;
  mensalidadeComDesconto?: number | null;
  pagamentoAdesao?: number | null;
  porcentagem?: number | null;
  termoAdesaoAceito: boolean;
  migracao: boolean;
  veiculo?: Veiculo | null;
  criadoEm: string;
  atualizadoEm?: string | null;
  rowVersion: number;
  atrasada: boolean;
  cpf?: string | null;
  estado?: string | null;
  indicacao?: boolean | null;
  tipoIndicacao?: string | null;
  valorIndicacao?: number | null;
  total?: number | null;
  termoAdesaoArquivoUrl?: string | null;
  pagamentoAdesaoArquivoUrl?: string | null;
}

export interface Veiculo {
  id: string;
  descricao?: string | null;
  placa?: string | null;
  fipe?: number | null;
  rastreador?: string | null;
  vistoriadorId?: string | null;
  vistoriadorNome?: string | null;
  dataChegada?: string | null;
}

export interface VeiculoUpsertRequest {
  descricao?: string | null;
  placa?: string | null;
  fipe?: number | null;
  rastreador?: string | null;
  vistoriadorId?: string | null;
  dataChegada?: string | null;
}

export interface OpportunityCreateRequest {
  leadId: string;
  titulo: string;
  responsavelId: string;
  etapaId?: string | null;
  produtoOuServico?: string | null;
  valorEstimado: number;
  probabilidadeFechamento?: number | null;
  dataPrevistaFechamento?: string | null;
  concorrente?: string | null;
  observacoes?: string | null;
  dataAdesao?: string | null;
  mensalidade?: number | null;
  mensalidadeComDesconto?: number | null;
  pagamentoAdesao?: number | null;
  porcentagem?: number | null;
  termoAdesaoAceito: boolean;
  migracao: boolean;
  veiculo?: VeiculoUpsertRequest | null;
}

export interface ChangeStageRequest {
  novaEtapaId: string;
  rowVersion: number;
  motivoPerdaId?: string | null;
  motivoPerdaObservacao?: string | null;
  valorFinal?: number | null;
  dataEfetivaFechamento?: string | null;
  cpf?: string | null;
  estado?: string | null;
  indicacao?: boolean | null;
  tipoIndicacao?: string | null;
  valorIndicacao?: number | null;
  total?: number | null;
  ativoEm?: string | null;
  mensalidade?: number | null;
  mensalidadeComDesconto?: number | null;
  pagamentoAdesao?: number | null;
  porcentagem?: number | null;
  migracao?: boolean;
  veiculo?: VeiculoUpsertRequest | null;
}

// --- Pipeline ---

export interface PipelineStage {
  id: string;
  nome: string;
  ordem: number;
  tipo: TipoEtapaPipeline;
  cor?: string | null;
  ativa: boolean;
}

export interface PipelineCard {
  opportunityId: string;
  leadId: string;
  leadNome: string;
  titulo: string;
  produtoOuServico?: string | null;
  valorEstimado: number;
  responsavelId: string;
  responsavelNome: string;
  origem?: string | null;
  proximaAtividadeEm?: string | null;
  proximaAtividadeAssunto?: string | null;
  etapaDesde: string;
  atrasada: boolean;
  rowVersion: number;
}

export interface PipelineColumn {
  etapa: PipelineStage;
  cartoes: PipelineCard[];
  valorTotal: number;
}

export interface PipelineBoard {
  colunas: PipelineColumn[];
}

export interface LossReason {
  id: string;
  descricao: string;
  ativo: boolean;
}

// --- Atividades ---

export interface Activity {
  id: string;
  leadId: string;
  leadNome: string;
  opportunityId?: string | null;
  opportunityTitulo?: string | null;
  responsavelId: string;
  responsavelNome: string;
  tipo: TipoAtividade;
  assunto: string;
  descricao?: string | null;
  dataHoraPrevista: string;
  dataHoraConclusao?: string | null;
  resultado?: string | null;
  status: StatusAtividade;
  lembreteMinutosAntes?: number | null;
  atrasada: boolean;
  rowVersion: number;
}

export interface ActivityCreateRequest {
  leadId: string;
  opportunityId?: string | null;
  responsavelId?: string | null;
  tipo: TipoAtividade;
  assunto: string;
  descricao?: string | null;
  dataHoraPrevista: string;
  lembreteMinutosAntes?: number | null;
}

export interface ActivityUpdateRequest {
  tipo: TipoAtividade;
  assunto: string;
  descricao?: string | null;
  dataHoraPrevista: string;
  lembreteMinutosAntes?: number | null;
  rowVersion: number;
}

// --- Dashboard ---

export interface DashboardIndicadores {
  novosLeads: number;
  leadsSemContato: number;
  contatosHoje: number;
  atividadesAtrasadas: number;
  oportunidadesAbertas: number;
  valorPipeline: number;
  taxaConversao: number;
  ticketMedio: number;
  vendasGanhasValor: number;
  vendasGanhasQuantidade: number;
  vendasGanhasAdesaoValor: number;
}

export interface MetaResultado {
  metaValor: number;
  realizadoValor: number;
  percentualAtingido: number;
  metaQuantidade: number;
  realizadoQuantidade: number;
}

export interface FunilEtapa {
  etapa: string;
  quantidade: number;
  valorTotal: number;
}

export interface EvolucaoVendas {
  periodo: string;
  valorGanho: number;
  quantidade: number;
}

export interface OrigemLead {
  origem: string;
  quantidade: number;
}

export interface DesempenhoVendedor {
  vendedorId: string;
  vendedorNome: string;
  leadsAtribuidos: number;
  oportunidadesAbertas: number;
  valorPipeline: number;
  vendasGanhas: number;
  valorGanho: number;
  taxaConversao: number;
  valorAdesao: number;
}

export interface AlertaLeadParado {
  leadId: string;
  leadNome: string;
  responsavelNome?: string | null;
  diasSemContato: number;
}

export interface Dashboard {
  indicadores: DashboardIndicadores;
  meta: MetaResultado;
  funil: FunilEtapa[];
  evolucaoVendas: EvolucaoVendas[];
  origemLeads: OrigemLead[];
  desempenhoPorVendedor: DesempenhoVendedor[];
  atividadesDoDia: Activity[];
  leadsParados: AlertaLeadParado[];
}

// --- Metas ---

export interface SalesGoal {
  /** Nulo quando o consultor ainda não tem meta cadastrada para o mês. */
  id?: string | null;
  vendedorId: string;
  vendedorNome: string;
  mesReferencia: string;
  metaQuantidadeVendas?: number | null;
  metaValor?: number | null;
  realizadoValor: number;
  realizadoQuantidade: number;
}

/** Meta geral de uma regional (não de um consultor específico) — definida pelo administrador. */
export interface RegionalGoal {
  /** Nulo quando a regional ainda não tem meta geral cadastrada para o mês. */
  id?: string | null;
  regionalId: string;
  regionalNome: string;
  mesReferencia: string;
  metaQuantidadeVendas?: number | null;
  metaValor?: number | null;
  realizadoValor: number;
  realizadoQuantidade: number;
}

// --- Gestão comercial ---

export interface VendedorResumo {
  id: string;
  nome: string;
  leadsAtivos: number;
  oportunidadesAbertas: number;
  /** Teto de leads que a distribuição automática atribui por mês corrente. Nulo = sem limite. */
  limiteMensalLeads?: number | null;
  leadsRecebidosNoMes: number;
}

export interface ConsultorDesempenho {
  id: string;
  nome: string;
  email: string;
  telefone?: string | null;
  regionalNome?: string | null;
  ativo: boolean;
  leadsAtivos: number;
  oportunidadesAbertas: number;
  valorPipeline: number;
  vendasGanhas: number;
  valorGanho: number;
  taxaConversao: number;
  limiteMensalLeads?: number | null;
  leadsRecebidosNoMes: number;
  metaValor: number;
  realizadoValor: number;
  percentualMeta: number;
}

export interface RankingComercial {
  vendedorId: string;
  vendedorNome: string;
  posicao: number;
  valorGanho: number;
  vendasGanhas: number;
  taxaConversao: number;
}

export interface TempoMedioEtapa {
  etapa: string;
  diasMedios: number;
}

export interface MotivoPerdaResumo {
  motivo: string;
  quantidade: number;
  valorPerdido: number;
}

export interface OportunidadeParada {
  opportunityId: string;
  leadNome: string;
  etapaNome: string;
  responsavelNome: string;
  diasSemMovimentacao: number;
  valorEstimado: number;
}

export interface RedistribuicaoHistorico {
  leadId: string;
  leadNome: string;
  responsavelAnteriorNome?: string | null;
  responsavelNovoNome: string;
  alteradoPorNome?: string | null;
  motivo?: string | null;
  alteradoEm: string;
}

export interface GestaoComercialResumo {
  tempoMedioPrimeiroContatoHoras: number;
  tempoMedioPorEtapa: TempoMedioEtapa[];
  oportunidadesSemMovimentacao: OportunidadeParada[];
  ranking: RankingComercial[];
  motivosPerda: MotivoPerdaResumo[];
}

// --- Regionais e gestão de usuários ---

export interface Regional {
  id: string;
  nome: string;
  ativa: boolean;
  quantidadeUsuarios: number;
}

export interface CreateRegionalRequest {
  nome: string;
}

export interface UpdateRegionalRequest {
  nome: string;
  ativa: boolean;
}

export interface UserSummary {
  id: string;
  nomeCompleto: string;
  email: string;
  telefone?: string | null;
  papeis: string[];
  regionalId?: string | null;
  regionalNome?: string | null;
  gestorComercialId?: string | null;
  gestorComercialNome?: string | null;
  grupoId?: string | null;
  grupoNome?: string | null;
  ativo: boolean;
  limiteMensalLeads?: number | null;
  fotoUrl?: string | null;
  criadoEm: string;
}

export interface UserFilterRequest {
  busca?: string;
  papel?: string;
  regionalId?: string;
  grupoId?: string;
  ativo?: boolean;
  pagina?: number;
  tamanhoPagina?: number;
}

export interface UserCreateRequest {
  nomeCompleto: string;
  email: string;
  senha: string;
  telefone?: string | null;
  papel: string;
  regionalId?: string | null;
  gestorComercialId?: string | null;
  grupoId?: string | null;
  limiteMensalLeads?: number | null;
}

export interface UserUpdateRequest {
  nomeCompleto: string;
  telefone?: string | null;
  papel: string;
  regionalId?: string | null;
  gestorComercialId?: string | null;
  grupoId?: string | null;
  limiteMensalLeads?: number | null;
  ativo: boolean;
}

export interface GrupoMembro {
  id: string;
  nomeCompleto: string;
  fotoUrl?: string | null;
}

export interface Grupo {
  id: string;
  regionalId: string;
  nome: string;
  ativo: boolean;
  consultores: GrupoMembro[];
}

export interface CreateGrupoRequest {
  regionalId?: string | null;
  nome: string;
}

export interface UpdateGrupoRequest {
  nome: string;
  ativo: boolean;
}

export interface UpdateGrupoMembrosRequest {
  consultorIds: string[];
}

// --- Portal do consultor ---

export enum TipoAnuncio {
  Aviso = 1,
  Flashcard = 2,
}

export interface Announcement {
  id: string;
  tipo: TipoAnuncio;
  titulo: string;
  descricao: string;
  cor?: string | null;
}
