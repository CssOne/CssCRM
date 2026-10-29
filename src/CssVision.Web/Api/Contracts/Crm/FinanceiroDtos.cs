namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>
/// Venda no Controle financeiro. Quando <see cref="DoCard"/> é verdadeiro ela veio do quadro de leads (cliente, data e adesão
/// são do card); <see cref="Versao"/> é o carimbo de edição devolvido ao salvar — impede sobrescrever uma alteração mais nova.
/// </summary>
public record FinanceiroVendaDto(
    Guid? Id, Guid? OportunidadeId, bool DoCard, DateOnly DataVenda, string Cliente, string Produto,
    decimal ValorAdesao, decimal ValorBonus, decimal Premiacoes,
    DateOnly? DataPrevistaAdesao, string StatusAdesao, DateOnly? DataPrevistaBonus, string StatusBonus, string? Observacoes,
    uint? Versao = null,
    string? Consultor = null,
    Guid? ConsultorId = null,
    bool BonusAutomatico = false,
    decimal? AdesaoOriginal = null,
    /// <summary>Bônus do contrato: o previsto (pelo contrato) e o que o consultor informou ter realmente recebido (nulo = ainda não informado).</summary>
    decimal? BonusPrevisto = null,
    decimal? BonusRecebido = null,
    /// <summary>Taxa de instalação do rastreador (do card): a única coisa descontada da adesão antes da bonificação do CLT.</summary>
    decimal? TaxaRastreador = null);

public record FinanceiroCustoDto(
    Guid? Id, bool DoCard, DateOnly Data, string Categoria, string? Descricao, decimal Valor,
    DateOnly? Vencimento, string Status, DateOnly? DataPagamento, bool ComprovanteSalvo,
    uint? Versao = null,
    string? Consultor = null,
    Guid? ConsultorId = null,
    bool Fixo = false);

public record FinanceiroMetaDto(
    int Mes, decimal MetaAdesao, decimal MetaBonus, decimal MetaRecorrencia, int MetaVendas, string? Observacoes, bool DoCrm = false);

public record FinanceiroConfigDto(decimal PercentualReserva, decimal CaixaInicial);

/// <summary>
/// De quem são os números: <c>propria</c> (o consultor logado, pode editar), <c>consultor</c> (um consultor escolhido por quem gere) ou
/// <c>equipe</c> (todos os consultores que quem consulta enxerga, somados). As duas últimas são só leitura.
/// </summary>
public record FinanceiroVisaoDto(string Tipo, Guid? ConsultorId, string? ConsultorNome, bool SomenteLeitura, int Consultores);

public record FinanceiroAnoDto(
    int Ano,
    FinanceiroConfigDto Config,
    IReadOnlyList<FinanceiroVendaDto> Vendas,
    IReadOnlyList<FinanceiroCustoDto> Custos,
    IReadOnlyList<FinanceiroMetaDto> Metas,
    FinanceiroVisaoDto? Visao = null,
    IReadOnlyList<FinanceiroRendaDto>? Rendas = null,
    string? TipoContrato = null);

/// <summary>Renda fixa do mês definida pelo contrato: salário (CLT) ou recorrência (PJ). Conta como receita já recebida no mês.</summary>
public record FinanceiroRendaDto(int Mes, string Tipo, decimal Valor, string? Consultor = null, Guid? ConsultorId = null);

// ---- Contrato do consultor (PJ/CLT), valores definidos pela gestão e histórico ----

/// <summary><c>Origem</c>: <c>Individual</c> (contrato definido para o consultor) ou <c>Grupo</c> (herdado do grupo em que ele está, em <c>Grupo</c>).</summary>
public record FinanceiroPerfilDto(string? Tipo, string Transporte, DateOnly? InicioContrato, string? ModeloPj = null, string? Origem = null, string? Grupo = null);

public record FinanceiroPerfilRequest(string? Tipo, string? Transporte, DateOnly? InicioContrato, string? ModeloPj = null);

/// <summary>Valor de um parâmetro (ex.: bônus por placa) com vigência a partir de um mês. <c>VigenteDesde</c> é qualquer dia do mês: vale o mês inteiro.</summary>
public record FinanceiroValorRequest(string Parametro, DateOnly VigenteDesde, decimal Valor, string? Observacao);

public record FinanceiroValorDto(Guid Id, string Parametro, DateOnly VigenteDesde, decimal Valor, string? Observacao, string? DefinidoPor, DateTimeOffset DefinidoEm);

/// <summary>O que vale para o consultor no mês pedido: o valor vigente, desde quando e se é o padrão do sistema (ainda não definido pela gestão).</summary>
public record FinanceiroParametroAtualDto(
    string Parametro, decimal? Valor, DateOnly? Desde, bool Padrao,
    int? CarenciaMeses = null, bool CarenciaPropria = false, int? CarenciaPadrao = null, DateOnly? InicioCobranca = null,
    int? LeadsNoMes = null, decimal? CustoRealNoMes = null,
    /// <summary>De onde vem o valor: <c>Individual</c> (do consultor), <c>Grupo</c> (herdado, nome em <c>Grupo</c>) ou <c>Padrao</c> (padrão geral/do sistema).</summary>
    string? Origem = null, string? Grupo = null);

/// <summary>Prazo do consultor para começar a pagar um custo fixo. <c>CarenciaMeses</c> nulo = volta a seguir o padrão.</summary>
public record FinanceiroPrazoRequest(string Parametro, int? CarenciaMeses);

/// <summary>Valor e carência padrão de um custo fixo para todos os consultores PJ. Campo nulo = usa o padrão do sistema.</summary>
public record FinanceiroPadraoRequest(string Parametro, decimal? Valor, int? CarenciaMeses);

public record FinanceiroPadraoDto(string Parametro, decimal Valor, int CarenciaMeses, bool ValorDefinido, bool CarenciaDefinida, string? DefinidoPor);

public record FinanceiroPadroesDto(bool PodeEditar, IReadOnlyList<FinanceiroPadraoDto> Padroes);

/// <summary>Aviso de que um custo fixo do consultor PJ começa a ser cobrado em breve (a gestão pode estender o prazo ou deixar começar).</summary>
public record FinanceiroAlertaCobrancaDto(
    Guid ConsultorId, string ConsultorNome, string Parametro, string Rotulo, decimal Valor, DateOnly InicioCobranca, int DiasRestantes, int CarenciaMeses, bool PodeEditar);

public record FinanceiroCustoFixoDto(string Categoria, string Descricao, decimal Valor, string? Chave = null);

public record FinanceiroMesContratoDto(
    int Mes, decimal Salario, decimal Recorrencia, decimal BonusPorPlaca, decimal PercentualAdesao, IReadOnlyList<FinanceiroCustoFixoDto> Custos,
    decimal AdesoesDoMes = 0, decimal Comissao = 0, int VendasDoMes = 0, decimal FaturadoLiquido = 0,
    decimal Alimentacao = 0, decimal Passagem = 0);

public record FinanceiroContratoDto(
    Guid ConsultorId,
    string? ConsultorNome,
    FinanceiroPerfilDto Perfil,
    bool PodeEditar,
    IReadOnlyList<FinanceiroParametroAtualDto> Atuais,
    IReadOnlyList<FinanceiroValorDto> Historico,
    IReadOnlyList<FinanceiroMesContratoDto> Meses,
    IReadOnlyList<FinanceiroGrupoHistoricoDto>? Grupos = null);

/// <summary>Um período do consultor em um grupo (ou fora de qualquer grupo, quando <c>GrupoId</c> é nulo). <c>Ate</c> nulo = é o atual.</summary>
public record FinanceiroGrupoHistoricoDto(Guid? GrupoId, string? Grupo, string? Regional, DateOnly Desde, DateOnly? Ate);

// ---- Contrato do grupo: o que vale para todo consultor do grupo, com vigência mensal ----

public record GrupoContratoResumoDto(Guid GrupoId, string Grupo, Guid RegionalId, string Regional, bool Ativo, int Consultores, string? Tipo, string? ModeloPj);

public record GrupoContratoPerfilDto(DateOnly VigenteDesde, string? Tipo, string? ModeloPj, string Transporte, string? DefinidoPor);

public record GrupoContratoValorDto(Guid Id, string Parametro, DateOnly VigenteDesde, decimal Valor, string? Observacao, string? DefinidoPor);

public record GrupoContratoPrazoDto(string Parametro, int? CarenciaMeses, int CarenciaPadrao);

public record GrupoContratoConsultorDto(Guid ConsultorId, string Nome, DateOnly Desde, string? Tipo);

public record GrupoContratoDto(
    Guid GrupoId, string Grupo, Guid RegionalId, string Regional, bool PodeEditar,
    GrupoContratoPerfilDto? Perfil,
    IReadOnlyList<GrupoContratoPerfilDto> HistoricoPerfil,
    IReadOnlyList<FinanceiroParametroAtualDto> Atuais,
    IReadOnlyList<GrupoContratoValorDto> Historico,
    IReadOnlyList<GrupoContratoPrazoDto> Prazos,
    IReadOnlyList<GrupoContratoConsultorDto> Consultores);

/// <summary>Contrato do grupo a partir de um mês: tipo (PJ/CLT), modelo do PJ e transporte. Tipo vazio = o grupo deixa de ter contrato a partir do mês.</summary>
public record GrupoPerfilRequest(DateOnly VigenteDesde, string? Tipo, string? ModeloPj, string? Transporte);

public record GrupoValorRequest(string Parametro, DateOnly VigenteDesde, decimal Valor, string? Observacao);

/// <summary>Carência do grupo para começar a cobrar um custo fixo. Nulo = volta a seguir o padrão geral.</summary>
public record GrupoPrazoRequest(string Parametro, int? CarenciaMeses);

// ---- Setor Financeiro: o que a CSS cobra de cada consultor, mês a mês ----

/// <summary>Cobrança de um mês: notebook (parcela), custo de lead ou tráfego pago. <c>Pago</c> falso = em aberto.</summary>
public record CobrancaDto(string Item, string Rotulo, string Descricao, DateOnly Competencia, decimal Valor, bool Pago, DateOnly? PagoEm, string? Observacao, bool Atrasada, Guid? CustoId = null, DateOnly? Vencimento = null,
    bool InformadoPeloConsultor = false, DateOnly? DataInformada = null, string? ComprovanteUrl = null, DateOnly? LembreteEm = null, string? MotivoRecusa = null, string? ObservacaoConsultor = null,
    /// <summary>Quando o valor foi ajustado à mão: o que o contrato calculava (o <c>Valor</c> é o combinado).</summary>
    decimal? ValorOriginal = null);

/// <summary>
/// Ajusta uma cobrança de um mês: <c>Valor</c> troca o calculado pelo contrato, <c>Remover</c> tira a cobrança do mês. Sem nenhum dos dois, volta ao que o contrato calcula
/// (restaura a removida e desfaz o valor ajustado).
/// </summary>
public record CobrancaAjusteRequest(string Item, DateOnly Competencia, Guid? CustoId, decimal? Valor, bool Remover = false);

public record CobrancaPagamentoRequest(string Item, DateOnly Competencia, bool Pago, DateOnly? PagoEm, string? Observacao, Guid? CustoId = null);

/// <summary>Custo que o financeiro lança para o consultor, pago em parcelas mensais (notebook, celular...).</summary>
public record CustoConsultorRequest(string Tipo, string? Descricao, decimal ValorTotal, int Parcelas, DateOnly PrimeiraParcela, bool Forcar = false);

public record CustoConsultorDto(
    Guid Id, string Tipo, string Rotulo, string? Descricao, decimal ValorTotal, int Parcelas, decimal ValorParcela, DateOnly PrimeiraParcela,
    int ParcelasPagas, int ParcelaAtual, string? CriadoPor, DateTimeOffset CriadoEm);

public record SetorFinanceiroConsultorDto(
    Guid ConsultorId, string ConsultorNome, string? TipoContrato, bool PodeEditar,
    decimal EmAberto, decimal Pago, int CobrancasEmAberto, int CobrancasAtrasadas,
    IReadOnlyList<CobrancaDto> Cobrancas,
    decimal CustoLeadMes = 0, int LeadsMes = 0, decimal CustoLeadTotal = 0,
    IReadOnlyList<CustoConsultorDto>? Custos = null,
    /// <summary>Cobranças removidas de algum mês (com o valor que o contrato calculava): dá para restaurar.</summary>
    IReadOnlyList<CobrancaDto>? Removidas = null);

public record SetorFinanceiroResumoDto(
    Guid ConsultorId, string ConsultorNome, string? TipoContrato, string? ModeloPj,
    decimal EmAberto, int CobrancasEmAberto, int CobrancasAtrasadas, int AvisosAbertos, DateOnly? ProximaCobranca,
    decimal CustoLeadMes = 0, int LeadsMes = 0, bool TemCobrancas = false, int AguardandoConferencia = 0);

/// <summary>O consultor avisa que pagou uma cobrança (com a data e, se quiser, o comprovante): o financeiro confere e confirma.</summary>
public record InformarPagamentoRequest(string Item, DateOnly Competencia, Guid? CustoId, DateOnly DataPagamento, string? Observacao);

/// <summary>"Pagar depois": o consultor escolhe um dia para ser lembrado (nulo cancela o lembrete).</summary>
public record LembreteCobrancaRequest(string Item, DateOnly Competencia, Guid? CustoId, DateOnly? Data);

/// <summary>O financeiro não confirmou o pagamento informado e devolve a cobrança ao consultor com o motivo.</summary>
public record RecusarPagamentoRequest(string Item, DateOnly Competencia, Guid? CustoId, string Motivo);

/// <summary>Consultor que aparece nas escolhas do Controle financeiro (administradores não entram: só quem tem o papel de consultor).</summary>
public record ConsultorFinanceiroDto(Guid Id, string Nome, bool Ativo);
