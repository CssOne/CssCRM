namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Id nulo indica que o consultor ainda não tem meta cadastrada para o mês.</summary>
public record SalesGoalDto(
    Guid? Id,
    Guid VendedorId,
    string VendedorNome,
    DateOnly MesReferencia,
    int? MetaQuantidadeVendas,
    decimal? MetaValor,
    decimal RealizadoValor,
    int RealizadoQuantidade);

public record SalesGoalUpsertRequest(Guid VendedorId, DateOnly MesReferencia, int MetaQuantidadeVendas, decimal? MetaValor);

/// <summary>Id nulo indica que a regional ainda não tem meta geral cadastrada para o mês.</summary>
public record RegionalGoalDto(
    Guid? Id,
    Guid RegionalId,
    string RegionalNome,
    DateOnly MesReferencia,
    int? MetaQuantidadeVendas,
    decimal? MetaValor,
    decimal RealizadoValor,
    int RealizadoQuantidade);

public record RegionalGoalUpsertRequest(Guid RegionalId, DateOnly MesReferencia, int MetaQuantidadeVendas, decimal? MetaValor);
