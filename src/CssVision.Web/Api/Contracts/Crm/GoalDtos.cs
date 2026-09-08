namespace CssVision.Web.Api.Contracts.Crm;

public record SalesGoalDto(
    Guid Id,
    Guid VendedorId,
    string VendedorNome,
    DateOnly MesReferencia,
    decimal MetaValor,
    int? MetaQuantidadeVendas,
    decimal RealizadoValor,
    int RealizadoQuantidade);

public record SalesGoalUpsertRequest(Guid VendedorId, DateOnly MesReferencia, decimal MetaValor, int? MetaQuantidadeVendas);
