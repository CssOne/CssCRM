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
  Periodo = 7,
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
  telefone2?: string | null;
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
  /** Dados da venda (colunas dos Relatórios do Notion); nulo se o lead não tem oportunidade. */
  venda?: LeadVendaResumo | null;
}

/** Rodapé da lista de leads: contagem e somas de todos os leads que batem com os filtros. */
/** Painel da TV (/tv/comercial): dados direto do CRM. */
export interface TvRanking {
  posicao: number;
  consultorId: string;
  nome: string;
  fotoUrl?: string | null;
  regional: string;
  quantidadeVendas: number;
  /** Adesão paga das vendas. */
  valorVendido: number;
  quantidadeMeta?: number | null;
  percentualMeta?: number | null;
}

export interface TvConversao {
  posicao: number;
  consultorId: string;
  nome: string;
  fotoUrl?: string | null;
  regional: string;
  leadsAtendidos: number;
  vendasFechadas: number;
  leadsPerdidos: number;
  taxaConversao: number;
}

export interface TvRegional {
  posicao: number;
  regionalId: string;
  nome: string;
  quantidadeVendas: number;
  valorTotal: number;
  percentualParticipacao: number;
  quantidadeMeta?: number | null;
  percentualMeta?: number | null;
}

export interface TvVenda {
  vendaId: string;
  consultor: string;
  fotoUrl?: string | null;
  regional: string;
  cliente?: string | null;
  numeroContrato?: string | null;
  origem?: string | null;
  valor: number;
  dataVenda: string;
  atualizadaEm: string;
}

export interface TvEvolucao {
  data: string;
  quantidadeVendasDia: number;
  valorVendidoDia: number;
  quantidadeAcumulada: number;
  valorAcumulado: number;
}

/** Situação da integração com o Discord para o usuário logado. */
export interface DiscordStatus {
  /** O servidor tem as credenciais do Discord; sem isso a tela só avisa que não foi ativado. */
  configurado: boolean;
  vinculado: boolean;
  discordNome?: string | null;
  avisosAtivos: boolean;
  /** A conta está no servidor da empresa (o bot a colocou lá ao vincular). */
  noServidor: boolean;
  vinculadoEm?: string | null;
}

export interface DiscordIniciar {
  url: string;
}

export interface DiscordTeste {
  enviado: boolean;
  mensagem: string;
}

/** Grupo do CRM que existe como canal no Discord. */
export interface DiscordCanal {
  chave: string;
  nome: string;
  ativo: boolean;
}

/** O que a sincronização dos grupos do Discord fez e o que não conseguiu fazer. */
export interface DiscordSincronizacao {
  canaisCriados: number;
  cargosCriados: number;
  membrosAtualizados: number;
  membrosForaDoServidor: number;
  falhas: string[];
  canaisDeVozCriados: number;
}

/** Aviso de pagamento em aberto (visão de quem envia). */
export interface AvisoPagamento {
  id: string;
  consultorId: string;
  consultorNome: string;
  titulo: string;
  mensagem: string;
  valor?: number | null;
  referencia?: string | null;
  enviadoPorNome: string;
  criadoEm: string;
  status: "Aberto" | "Resolvido";
  lidoEm?: string | null;
  resolvidoEm?: string | null;
}

/** Aviso de pagamento visto pelo consultor (notificação e card "Avisos importantes"). */
export interface MeuAviso {
  id: string;
  titulo: string;
  mensagem: string;
  valor?: number | null;
  referencia?: string | null;
  enviadoPorNome: string;
  criadoEm: string;
  lidoEm?: string | null;
}

export interface TvAdministrativoRegistro {
  pessoa: string;
  fotoUrl?: string | null;
  cliente?: string | null;
  placa?: string | null;
  tipoEvento?: string | null;
  data: string;
  /** O registro foi criado hoje. */
  hoje?: boolean;
}

export interface TvAdministrativoIndicador {
  id: "reintegration" | "claim" | "tracker" | string;
  rotulo: string;
  acao: string;
  total: number;
  hoje: number;
  ultimo?: TvAdministrativoRegistro | null;
  /** Todos os registros do mês (detalhe por pessoa). */
  registros?: TvAdministrativoRegistro[] | null;
}

export interface TvComercial {
  periodo: { mes: number; ano: number };
  resumo: { vendasHoje: number; vendasNoMes: number; valorHoje: number; valorNoMes: number; percentualMetaGeral?: number | null };
  rankingConsultores: TvRanking[];
  rankingValorAdesao: TvRanking[];
  rankingConversao: TvConversao[];
  rankingRegionais: TvRegional[];
  evolucaoMensal: TvEvolucao[];
  ultimasVendas: TvVenda[];
  atualizadoEm: string;
  /** Reintegrações, eventos finalizados e rastreadores (Notion); nulo se o Notion não respondeu. */
  administrativo?: { indicadores: TvAdministrativoIndicador[] } | null;
  /** Vendas do painel que existem só no Notion (base MG134), já sem as que o CRM também tem. */
  vendasSoNoNotion?: number;
  /** Todas as vendas do mês exibido, com o consultor (detalhes ao clicar nos cards). */
  vendasDoMes?: TvVendaMes[] | null;
}

export interface TvVendaMes {
  vendaId: string;
  consultorId: string;
  consultor: string;
  fotoUrl?: string | null;
  regional: string;
  cliente?: string | null;
  placa?: string | null;
  origem?: string | null;
  valor: number;
  dataVenda: string;
}

export interface LeadTotais {
  contagem: number;
  adesao: number;
  fipe: number;
  mensalidade: number;
  mensalidadeComDesconto: number;
  rastreador: number;
  indicacao: number;
  vistoria: number;
  total: number;
  mediaPorcentagem?: number;
}

export interface LeadVendaResumo {
  adesao?: number | null;
  fipe?: number | null;
  mensalidade?: number | null;
  mensalidadeComDesconto?: number | null;
  porcentagem?: number | null;
  rastreador?: number | null;
  indicacao?: number | null;
  vistoria?: number | null;
  total?: number | null;
  dataVenda?: string | null;
}

/** Grupo com o nome da regional — alimenta o filtro "Grupo" do quadro e da lista de leads. */
export interface GrupoFiltro {
  id: string;
  regionalId: string;
  regionalNome: string;
  nome: string;
}

export interface LeadOpportunitySummary {
  id: string;
  titulo: string;
  etapaNome: string;
  etapaTipo: TipoEtapaPipeline;
  valorEstimado: number;
  dataPrevistaFechamento?: string | null;
  ativa: boolean;
  migracao: boolean;
  indicacao?: boolean | null;
  cpf?: string | null;
  estado?: string | null;
  ativoEm?: string | null;
  porcentagem?: number | null;
  mensalidade?: number | null;
  mensalidadeComDesconto?: number | null;
  mensalidadeComCupom?: number | null;
  pagamentoAdesao?: number | null;
  total?: number | null;
  tipoIndicacao?: string | null;
  valorIndicacao?: number | null;
  veiculo?: LeadOpportunityVeiculoSummary | null;
  termoAdesaoArquivoUrl?: string | null;
  pagamentoAdesaoArquivoUrl?: string | null;
  comprovanteIndicacaoArquivoUrl?: string | null;
  comprovanteVistoriaArquivoUrl?: string | null;
  dataPagamentoAdesaoPrevista?: string | null;
  /** Consultor dono da oportunidade — consultores só excluem as próprias. */
  responsavelId?: string | null;
}

export interface LeadOpportunityVeiculoSummary {
  descricao?: string | null;
  placa?: string | null;
  /** Carro zero: chassi no lugar da placa. */
  chassi?: string | null;
  fipe?: number | null;
  rastreador?: number | null;
  valorVistoria?: number | null;
  dataChegada?: string | null;
}

export interface LeadDetail {
  id: string;
  nomeOuRazaoSocial: string;
  tipoPessoa: TipoPessoa;
  documento?: string | null;
  telefone?: string | null;
  telefone2?: string | null;
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
  utmCampaign?: string | null;
  utmTerm?: string | null;
  metaClickId?: string | null;
  metaFormId?: string | null;
  metaLeadId?: string | null;
  indicadoPorLeadId?: string | null;
  indicadoPorLeadNome?: string | null;
  tipoIndicacao?: string | null;
  criadoManualmente: boolean;
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
  /** Valor da adesão informado ao mover para "Cotação". */
  valorAdesao?: number | null;
  /** Card de outro veículo do mesmo cliente: o card original. */
  veiculoAdicionalDeLeadId?: string | null;
  veiculoAdicionalDeLeadNome?: string | null;
  /** CPF/CNPJ do card original — a venda do veículo adicional usa o mesmo documento. */
  veiculoAdicionalDeDocumento?: string | null;
}

export interface LeadCreateRequest {
  nomeOuRazaoSocial: string;
  tipoPessoa: TipoPessoa;
  documento?: string | null;
  telefone?: string | null;
  telefone2?: string | null;
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
  /** Modelo do veículo que não atendemos (coluna "Não fazemos"). */
  veiculoNaoAtendido?: string | null;
  gclid?: string | null;
  utmMedium?: string | null;
  utmSource?: string | null;
  utmCampaign?: string | null;
  utmTerm?: string | null;
  metaClickId?: string | null;
  metaFormId?: string | null;
  metaLeadId?: string | null;
  indicadoPorLeadId?: string | null;
  tipoIndicacao?: string | null;
  responsavelId?: string | null;
  etapaId?: string | null;
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

export interface LeadLixeira {
  id: string;
  nomeOuRazaoSocial: string;
  placa?: string | null;
  telefone?: string | null;
  responsavelNome?: string | null;
  etapa?: string | null;
  tipoIndicacao?: string | null;
  excluidoEm?: string | null;
  excluidoPor?: string | null;
}

export interface LeadKanbanCard {
  leadId: string;
  nomeOuRazaoSocial: string;
  telefone?: string | null;
  telefone2?: string | null;
  email?: string | null;
  estado?: string | null;
  origem?: string | null;
  campanha?: string | null;
  placa?: string | null;
  temSeguro?: boolean | null;
  utilidadeVeiculo?: string | null;
  tipoIndicacao?: string | null;
  migracao?: boolean;
  indicacao?: boolean | null;
  criadoManualmente: boolean;
  responsavelId?: string | null;
  responsavelNome?: string | null;
  tags: string[];
  criadoEm: string;
  ultimoContatoEm?: string | null;
  semContato: boolean;
  arquivado: boolean;
  rowVersion: number;
  /** "O que?" — produto de interesse (AGV, AGV ELÉTRICO, AGV TRUCK...). */
  oQue?: string | null;
  /** Valor da adesão informado ao mover para "Cotação". */
  valorAdesao?: number | null;
  /** Foto do consultor responsável. */
  responsavelFotoUrl?: string | null;
  /** Card de outro veículo de um cliente que já tem card. */
  veiculoAdicional?: boolean;
  /** Motivo da perda (cartões em "Perdido") e a explicação do consultor. */
  motivoPerda?: string | null;
  motivoPerdaObservacao?: string | null;
  /** Modelo do veículo informado pelo consultor (cartões em "Não fazemos"). */
  veiculoNaoAtendido?: string | null;
}

/** Opção do filtro por motivo da coluna "Perdido" (id vazio = sem motivo informado). */
export interface LeadKanbanMotivoPerda {
  id: string;
  descricao: string;
  quantidade: number;
}

export interface LeadKanbanColumn {
  etapa: LeadStage;
  /** Só as páginas já carregadas (os mais recentes); o restante vem por "Ver mais". */
  cartoes: LeadKanbanCard[];
  /** Só na coluna "Perdido": motivos dos cartões dela, com os filtros de cima. */
  motivosPerda?: LeadKanbanMotivoPerda[] | null;
  /** Quantidade de leads da coluna inteira. */
  total: number;
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
  mensalidadeComCupom?: number | null;
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
  comprovanteIndicacaoArquivoUrl?: string | null;
  comprovanteVistoriaArquivoUrl?: string | null;
  /** Data combinada para pagar a adesão, quando não foi paga no dia da venda (dispensa o comprovante). */
  dataPagamentoAdesaoPrevista?: string | null;
}

/** Venda do usuário com o pagamento da adesão marcado para hoje (ou atrasado) e ainda sem comprovante. */
export interface LembreteAdesao {
  opportunityId: string;
  leadId: string;
  leadNome: string;
  dataPagamentoAdesaoPrevista: string;
  pagamentoAdesao?: number | null;
}

export interface Veiculo {
  id: string;
  descricao?: string | null;
  placa?: string | null;
  /** Carro zero: chassi no lugar da placa. */
  chassi?: string | null;
  fipe?: number | null;
  rastreador?: number | null;
  valorVistoria?: number | null;
  vistoriadorId?: string | null;
  vistoriadorNome?: string | null;
  dataChegada?: string | null;
}

export interface VeiculoUpsertRequest {
  descricao?: string | null;
  placa?: string | null;
  /** Carro zero: chassi no lugar da placa. */
  chassi?: string | null;
  fipe?: number | null;
  rastreador?: number | null;
  valorVistoria?: number | null;
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
  mensalidadeComCupom?: number | null;
  pagamentoAdesao?: number | null;
  porcentagem?: number | null;
  termoAdesaoAceito: boolean;
  migracao: boolean;
  veiculo?: VeiculoUpsertRequest | null;
  cpf?: string | null;
  estado?: string | null;
  indicacao?: boolean | null;
  tipoIndicacao?: string | null;
  valorIndicacao?: number | null;
  total?: number | null;
}

export interface OpportunityUpdateRequest {
  titulo: string;
  responsavelId: string;
  produtoOuServico?: string | null;
  valorEstimado: number;
  probabilidadeFechamento?: number | null;
  dataPrevistaFechamento?: string | null;
  concorrente?: string | null;
  observacoes?: string | null;
  dataAdesao?: string | null;
  ativoEm?: string | null;
  mensalidade?: number | null;
  mensalidadeComDesconto?: number | null;
  mensalidadeComCupom?: number | null;
  pagamentoAdesao?: number | null;
  porcentagem?: number | null;
  termoAdesaoAceito: boolean;
  migracao: boolean;
  veiculo?: VeiculoUpsertRequest | null;
  rowVersion: number;
  cpf?: string | null;
  estado?: string | null;
  indicacao?: boolean | null;
  tipoIndicacao?: string | null;
  valorIndicacao?: number | null;
  total?: number | null;
  dataEfetivaFechamento?: string | null;
  dataPagamentoAdesaoPrevista?: string | null;
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
  mensalidadeComCupom?: number | null;
  pagamentoAdesao?: number | null;
  porcentagem?: number | null;
  migracao?: boolean;
  veiculo?: VeiculoUpsertRequest | null;
  dataPagamentoAdesaoPrevista?: string | null;
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
  /** Leads do período vindos do tráfego pago que ainda estão em "Sem etapa". */
  novosLeadsTrafegoSemEtapa?: number;
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
  adesao?: number;
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
  /** Coluna do quadro de leads em que o lead está. */
  etapaNome?: string | null;
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
  /** Resumo do quadro de leads: leads do período em cada coluna. */
  funilLeads?: EtapaLeadResumo[];
  /** Últimos 12 meses (mês atual por último). */
  resumoMensal?: ResumoMensal[];
  leadsParadosTotal?: number;
}

export interface EtapaLeadResumo {
  etapa: string;
  cor?: string | null;
  quantidade: number;
}

export interface ResumoMensal {
  mes: string;
  leads: number;
  perdidos: number;
  vendas: number;
  valorGanho: number;
  adesao: number;
  conversao: number;
}

// --- Marketing / tráfego pago ---

export interface MarketingIndicadores {
  totalLeads: number;
  leadsSemEtapa: number;
  leadsGanhos: number;
  leadsPerdidos: number;
  /** Vendas sobre os já decididos (vendas + perdidos). */
  taxaConversao: number;
  leadsSemContato: number;
  leadsEmAndamento: number;
  leadsNaoFazemos: number;
  leadsSemResponsavel: number;
  /** Vendas sobre o total de leads do período. */
  taxaConversaoGeral: number;
  valorAdesao: number;
  mensalidadeMedia: number;
  tempoMedioPrimeiroContatoHoras?: number | null;
  totalLeadsPeriodoAnterior: number;
  leadsGanhosPeriodoAnterior: number;
  mediaLeadsPorDia: number;
}

export interface MarketingGrupo {
  nome: string;
  totalLeads: number;
  ganhos: number;
  perdidos: number;
  taxaConversao: number;
  semEtapa: number;
}

export interface MarketingOrigem {
  origem: string;
  totalLeads: number;
  ganhos: number;
  taxaConversao: number;
}

export interface MarketingCampanha {
  campanha: string;
  origem?: string | null;
  totalLeads: number;
  ganhos: number;
  taxaConversao: number;
  ultimoLeadEm: string;
  perdidos: number;
  semEtapa: number;
  valorAdesao: number;
}

export interface MarketingConsultor {
  id?: string | null;
  nome: string;
  totalLeads: number;
  semEtapa: number;
  emAndamento: number;
  ganhos: number;
  perdidos: number;
  taxaConversao: number;
  semContato: number;
  tempoMedioPrimeiroContatoHoras?: number | null;
}

/** Leads e vendas de um consultor num mês ("2026-09"). */
export interface MarketingConsultorMes {
  id?: string | null;
  nome: string;
  mes: string;
  leads: number;
  ganhos: number;
}

export interface MarketingEvolucao {
  data: string;
  quantidade: number;
  ganhos?: number;
}

export interface MarketingSerie {
  nome: string;
  valores: number[];
}

export interface MarketingFunil {
  etapa: string;
  quantidade: number;
  cor?: string | null;
}

/** diaSemana: 0 = domingo; hora no horário de Brasília. */
export interface MarketingHorario {
  diaSemana: number;
  hora: number;
  quantidade: number;
}

export interface MarketingMotivoPerda {
  motivo: string;
  quantidade: number;
}

/** Números de uma regional no Tráfego pago (MG132, MG134...). */
export interface MarketingRegional {
  regional: string;
  totalLeads: number;
  semEtapa: number;
  emAndamento: number;
  ganhos: number;
  perdidos: number;
  naoFazemos: number;
  taxaConversao: number;
  semContato: number;
  semResponsavel: number;
  participacaoPercentual: number;
}

export interface MarketingOpcoes {
  oQue: string[];
  origens: string[];
  campanhas: string[];
  canais: string[];
  consultores: { id: string; nome: string }[];
  estados: string[];
  etapas: string[];
  regionais?: string[];
}

export interface MarketingLeadItem {
  id: string;
  nomeOuRazaoSocial: string;
  telefone?: string | null;
  origem?: string | null;
  campanha?: string | null;
  utmSource?: string | null;
  utmMedium?: string | null;
  etapaNome?: string | null;
  responsavelNome?: string | null;
  criadoEm: string;
  oQue?: string | null;
  estado?: string | null;
  canal?: string | null;
}

export interface MarketingDashboard {
  indicadores: MarketingIndicadores;
  porOrigem: MarketingOrigem[];
  porCampanha: MarketingCampanha[];
  evolucao: MarketingEvolucao[];
  origensDisponiveis: string[];
  leads: MarketingLeadItem[];
  evolucaoPorOQue: MarketingSerie[];
  porOQue: MarketingGrupo[];
  porCanal: MarketingGrupo[];
  porEstado: MarketingGrupo[];
  porConsultor: MarketingConsultor[];
  funil: MarketingFunil[];
  porHorario: MarketingHorario[];
  motivosPerda: MarketingMotivoPerda[];
  opcoes: MarketingOpcoes;
  periodoInicio: string;
  periodoFim: string;
  porConsultorMensal: MarketingConsultorMes[];
  /** Total de leads do período — a lista vem paginada por /marketing/leads. */
  totalLeadsLista: number;
  /** Leads por regional, com todos os filtros menos o de regional (para comparar lado a lado). */
  porRegional?: MarketingRegional[];
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
  /** Teto de leads do tráfego pago por dia. Nulo = sem limite. */
  limiteDiarioLeads?: number | null;
  leadsRecebidosHoje?: number;
  /** Usuário ativo (consegue entrar no CRM). */
  ativo?: boolean;
  /** Entra no rodízio da distribuição automática de leads. */
  recebeLeads?: boolean;
  /** Faixa de horário (Brasília, "HH:mm") em que recebe leads; nulo = o dia todo. */
  horarioInicioLeads?: string | null;
  horarioFimLeads?: string | null;
  /** Dias em que recebe leads (0 = domingo … 6 = sábado); nulo = todos. */
  diasSemanaLeads?: number[] | null;
  /** Tipos de lead ("O que?") que o consultor recebe; vazio/nulo = qualquer tipo. */
  recebeSomenteOQue?: string[] | null;
  /** Leads de tráfego pago que chegaram para o vendedor no mês anterior. */
  leadsTrafegoMesAnterior?: number;
  /** Regional do cadastro do consultor (filtro da Gestão comercial). */
  regionalNome?: string | null;
  /** Leads de tráfego pago (Notion + sistema novo) que chegaram no mês. */
  leadsTrafegoNoMes?: number;
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
  /** Leads de tráfego pago (Notion + sistema novo) que chegaram no mês. */
  leadsTrafegoNoMes?: number;
}

export interface RankingComercial {
  vendedorId: string;
  vendedorNome: string;
  posicao: number;
  valorGanho: number;
  vendasGanhas: number;
  taxaConversao: number;
  /** Soma do pagamento de adesão das vendas do período. */
  valorAdesao?: number;
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

export interface PrimeiroContatoVendedor {
  vendedorId: string;
  vendedorNome: string;
  horas: number;
  leads: number;
}

export interface GestaoComercialResumo {
  tempoMedioPrimeiroContatoHoras: number;
  /** Quantos leads entraram na média (os que já tiveram contato no período). */
  leadsComPrimeiroContato?: number;
  primeiroContatoPorVendedor?: PrimeiroContatoVendedor[] | null;
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
  /** Distribuição automática: só recebe leads com estes "O que?". Nulo = qualquer lead. */
  recebeSomenteOQue?: string[] | null;
  limiteDiarioLeads?: number | null;
  /** Regionais ocultas para este administrador (ele não vê os dados delas). */
  regionaisOcultasIds?: string[] | null;
  regionaisOcultasNomes?: string[] | null;
  /** Administrador que pega leads / atua nas vendas. Falso = só administra a plataforma (some de tudo que remete a vendas). */
  atuaNasVendas?: boolean;
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
  recebeSomenteOQue?: string[] | null;
  limiteDiarioLeads?: number | null;
  regionaisOcultasIds?: string[] | null;
  atuaNasVendas?: boolean | null;
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
  recebeSomenteOQue?: string[] | null;
  limiteDiarioLeads?: number | null;
  regionaisOcultasIds?: string[] | null;
  /** Verdadeiro quando a lista acima foi mexida (lista vazia + verdadeiro = mostra todas de novo). */
  alterarRegionaisOcultas?: boolean;
  atuaNasVendas?: boolean | null;
}

export interface GrupoMembro {
  id: string;
  nomeCompleto: string;
  fotoUrl?: string | null;
  ativo?: boolean;
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

export interface RelatorioTotais {
  leads: number;
  leadsPerdidos: number;
  vendas: number;
  taxaConversao: number;
  adesao: number;
  mensalidade: number;
  ticketMensalidade: number;
  rastreador: number;
  vistoria: number;
  indicacao: number;
  vendasIndicacao: number;
}

export interface RelatorioMes {
  mes: string;
  leads: number;
  leadsPerdidos: number;
  vendas: number;
  adesao: number;
  mensalidade: number;
  rastreador: number;
  vistoria: number;
  indicacao: number;
}

export interface RelatorioSemana {
  inicio: string;
  leads: number;
  leadsPerdidos: number;
  vendas: number;
  adesao: number;
}

export interface RelatorioOrigem {
  origem: string;
  leads: number;
  vendas: number;
  adesao: number;
}

export interface RelatorioVendedor {
  vendedorId: string | null;
  vendedor: string;
  leads: number;
  vendas: number;
  adesao: number;
  mensalidade: number;
  taxaConversao: number;
}

export interface RelatorioEstado {
  estado: string;
  leads: number;
  vendas: number;
}

export interface RelatorioFaixaFipe {
  faixa: string;
  vendas: number;
}

export interface RelatorioProduto {
  produto: string;
  leads: number;
  vendas: number;
  adesao: number;
}

export interface RelatorioMarketingMes {
  mes: string;
  leadsGerados: number;
  vendas: number;
  facebookAds: number;
  googleAds: number;
  ferramentas: number;
  backlinks: number;
  totalGastos: number;
  faturamento: number;
  metaFaturamento: number;
  custoPorLead: number | null;
  roas: number | null;
  taxaConversao: number | null;
}

export interface RelatorioComercial {
  dataInicio: string;
  dataFim: string;
  totais: RelatorioTotais;
  porMes: RelatorioMes[];
  porSemana: RelatorioSemana[];
  porOrigem: RelatorioOrigem[];
  porVendedor: RelatorioVendedor[];
  porEstado: RelatorioEstado[];
  faixasFipe: RelatorioFaixaFipe[];
  porProduto: RelatorioProduto[];
  marketingNotion: RelatorioMarketingMes[];
  porEtapa: RelatorioEtapa[];
}

export interface RelatorioEtapa {
  etapaId: string | null;
  etapa: string;
  leads: number;
}

export interface AlertaDistribuicao {
  bloqueada: boolean;
  leadsSemResponsavel: number;
  leadsBloqueadosPorLimite: number;
  consultores: number;
  noLimiteDiario: number;
  noLimiteMensal: number;
  /** Se um gestor mandou continuar a distribuição ignorando os limites e o horário, até quando (fim do dia). */
  continuarAteEm?: string | null;
  /** Leads parados por limite e/ou horário (cada lead conta uma vez). */
  leadsBloqueados: number;
  /** Leads parados porque os consultores aptos estão fora do dia/horário de recebimento. */
  leadsBloqueadosPorHorario: number;
  consultoresForaDoHorario: number;
}

/** Grupo de conversa que o usuário pode abrir no chat. */
export interface DiscordChatCanal {
  chave: string;
  nome: string;
  /** "grupo" ou "direta" (conversa 1:1: o nome é o da outra pessoa). */
  tipo: "grupo" | "direta";
  fotoUrl?: string | null;
  /** Só nas conversas 1:1: a outra pessoa está com o CRM aberto agora. */
  online?: boolean;
}

/** Pessoa com quem dá para iniciar uma conversa 1:1. */
export interface DiscordChatContato {
  id: string;
  nome: string;
  fotoUrl?: string | null;
  regional?: string | null;
  online?: boolean;
}

export interface DiscordChatAnexo {
  nome: string;
  url: string;
  imagem: boolean;
}

export interface DiscordChatMensagem {
  id: string;
  autorNome: string;
  autorFotoUrl?: string | null;
  conteudo: string;
  criadaEm: string;
  anexos: DiscordChatAnexo[];
  /** Publicada pelo CRM (a tela alinha à direita as do próprio usuário). */
  doCrm: boolean;
  editada?: boolean;
}

export interface DiscordChatMensagens {
  mensagens: DiscordChatMensagem[];
  /** Pode haver mensagens mais antigas. */
  temMais: boolean;
  /** O Discord entregou as mensagens sem texto: falta ligar "Message Content Intent" no portal do desenvolvedor. */
  conteudoOculto: boolean;
}

/** Mensagens não lidas do chat: total (o número do menu) e por conversa. 50 significa "50 ou mais". */
export interface DiscordChatNaoLidas {
  total: number;
  porConversa: Record<string, number>;
}

/** Chamada de voz de uma conversa: o CRM não embute a chamada, abre o canal de voz no Discord. */
export interface DiscordChatChamada {
  url: string;
  /** O CRM avisou a conversa de que a pessoa está numa chamada (não repete se clicar de novo logo em seguida). */
  avisou: boolean;
}

/** Quem está com o CRM aberto agora numa conversa (sem a própria pessoa). */
export interface DiscordChatOnline {
  pessoas: { id: string; nome: string; fotoUrl?: string | null }[];
}

/** Avisos automáticos do CRM nos canais das regionais no Discord (tudo desligado até o administrador ligar). */
export interface DiscordAvisosCanais {
  venda: boolean;
  metaBatida: boolean;
  leadsParados: boolean;
}
