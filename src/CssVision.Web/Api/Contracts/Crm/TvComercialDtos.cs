namespace CssVision.Web.Api.Contracts.Crm;

public record TvPeriodoDto(int Mes, int Ano);

public record TvResumoDto(int VendasHoje, int VendasNoMes, decimal ValorHoje, decimal ValorNoMes, decimal? PercentualMetaGeral);

/// <summary>Posição de um consultor nos rankings de vendas e de adesão (o valor é a adesão paga).</summary>
public record TvRankingDto(
    int Posicao, Guid ConsultorId, string Nome, string? FotoUrl, string Regional,
    int QuantidadeVendas, decimal ValorVendido, int? QuantidadeMeta, decimal? PercentualMeta);

public record TvConversaoDto(
    int Posicao, Guid ConsultorId, string Nome, string? FotoUrl, string Regional,
    int LeadsAtendidos, int VendasFechadas, int LeadsPerdidos, decimal TaxaConversao);

public record TvRegionalDto(
    int Posicao, Guid RegionalId, string Nome, int QuantidadeVendas, decimal ValorTotal,
    decimal PercentualParticipacao, int? QuantidadeMeta, decimal? PercentualMeta);

public record TvVendaDto(
    Guid VendaId, string Consultor, string? FotoUrl, string Regional, string? Cliente, string? NumeroContrato,
    string? Origem, decimal Valor, DateTimeOffset DataVenda, DateTimeOffset AtualizadaEm);

public record TvEvolucaoDto(
    DateOnly Data, int QuantidadeVendasDia, decimal ValorVendidoDia, int QuantidadeAcumulada, decimal ValorAcumulado);

/// <summary>Tudo o que o painel da TV (/tv/comercial) mostra, direto do banco do CRM.</summary>
public record TvComercialDto(
    TvPeriodoDto Periodo,
    TvResumoDto Resumo,
    IReadOnlyList<TvRankingDto> RankingConsultores,
    IReadOnlyList<TvRankingDto> RankingValorAdesao,
    IReadOnlyList<TvConversaoDto> RankingConversao,
    IReadOnlyList<TvRegionalDto> RankingRegionais,
    IReadOnlyList<TvEvolucaoDto> EvolucaoMensal,
    IReadOnlyList<TvVendaDto> UltimasVendas,
    DateTimeOffset AtualizadoEm,
    /// <summary>Indicadores administrativos do Notion; nulo se o Notion não respondeu.</summary>
    TvAdministrativoDto? Administrativo = null,
    /// <summary>Quantas das vendas do painel vêm só do Notion (base MG134), já sem as que o CRM também tem.</summary>
    int VendasSoNoNotion = 0);

public record TvAdministrativoRegistroDto(string Pessoa, string? FotoUrl, string? Cliente, string? Placa, string? TipoEvento, string Data);

public record TvAdministrativoIndicadorDto(string Id, string Rotulo, string Acao, int Total, int Hoje, TvAdministrativoRegistroDto? Ultimo);

/// <summary>Reintegrações, eventos finalizados e rastreadores do mês, direto da base operacional do Notion.</summary>
public record TvAdministrativoDto(IReadOnlyList<TvAdministrativoIndicadorDto> Indicadores);
