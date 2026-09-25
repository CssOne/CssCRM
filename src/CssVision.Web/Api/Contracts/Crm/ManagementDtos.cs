namespace CssVision.Web.Api.Contracts.Crm;

public record VendedorResumoDto(
    Guid Id,
    string Nome,
    int LeadsAtivos,
    int OportunidadesAbertas,
    int? LimiteMensalLeads,
    int LeadsRecebidosNoMes,
    int? LimiteDiarioLeads = null,
    int LeadsRecebidosHoje = 0,
    bool Ativo = true,
    bool RecebeLeads = true);

public record AtualizarLimiteMensalRequest(int? Limite);

public record AtualizarLimiteDiarioRequest(int? Limite);

public record AtualizarRecebeLeadsRequest(bool RecebeLeads);

/// <summary>Ficha de desempenho de um consultor (papel Comercial) para a tela de gestão de consultores.</summary>
public record ConsultorDesempenhoDto(
    Guid Id,
    string Nome,
    string Email,
    string? Telefone,
    string? RegionalNome,
    bool Ativo,
    int LeadsAtivos,
    int OportunidadesAbertas,
    decimal ValorPipeline,
    int VendasGanhas,
    decimal ValorGanho,
    decimal TaxaConversao,
    int? LimiteMensalLeads,
    int LeadsRecebidosNoMes,
    decimal MetaValor,
    decimal RealizadoValor,
    decimal PercentualMeta);

public record RankingComercialDto(
    Guid VendedorId,
    string VendedorNome,
    int Posicao,
    decimal ValorGanho,
    int VendasGanhas,
    decimal TaxaConversao);

public record TempoMedioEtapaDto(string Etapa, double DiasMedios);

public record MotivoPerdaResumoDto(string Motivo, int Quantidade, decimal ValorPerdido);

public record OportunidadeParadaDto(
    Guid OpportunityId,
    string LeadNome,
    string EtapaNome,
    string ResponsavelNome,
    int DiasSemMovimentacao,
    decimal ValorEstimado);

public record RedistribuicaoHistoricoDto(
    Guid LeadId,
    string LeadNome,
    string? ResponsavelAnteriorNome,
    string ResponsavelNovoNome,
    string? AlteradoPorNome,
    string? Motivo,
    DateTimeOffset AlteradoEm);

public record GestaoComercialResumoDto(
    double TempoMedioPrimeiroContatoHoras,
    IReadOnlyList<TempoMedioEtapaDto> TempoMedioPorEtapa,
    IReadOnlyList<OportunidadeParadaDto> OportunidadesSemMovimentacao,
    IReadOnlyList<RankingComercialDto> Ranking,
    IReadOnlyList<MotivoPerdaResumoDto> MotivosPerda);
