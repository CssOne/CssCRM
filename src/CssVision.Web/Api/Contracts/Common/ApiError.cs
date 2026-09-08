namespace CssVision.Web.Api.Contracts.Common;

/// <summary>Formato padrão de erro de negócio devolvido pela API do CRM.</summary>
public class ApiError(string mensagem, string? codigo = null, object? detalhes = null)
{
    public string Mensagem { get; } = mensagem;
    public string? Codigo { get; } = codigo;
    public object? Detalhes { get; } = detalhes;
}

/// <summary>Lançada por serviços de domínio para sinalizar uma regra de negócio violada.</summary>
public class CrmBusinessException(string mensagem, string? codigo = null, object? detalhes = null)
    : Exception(mensagem)
{
    public string? Codigo { get; } = codigo;
    public object? Detalhes { get; } = detalhes;
}
