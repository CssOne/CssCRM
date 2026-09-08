namespace CssVision.Web.Api.Contracts.Crm;

public record VendedorResumoDto(Guid Id, string Nome, int LeadsAtivos, int OportunidadesAbertas);

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
