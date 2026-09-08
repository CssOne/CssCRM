using CssVision.Web.Api.Contracts.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Api;

/// <summary>
/// Traduz exceções de negócio do CRM em respostas HTTP padronizadas, sem expor detalhes internos
/// (stack trace, mensagens de infraestrutura) ao cliente.
/// </summary>
public sealed class CrmExceptionHandler(ILogger<CrmExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, error) = exception switch
        {
            CrmForbiddenException ex => (StatusCodes.Status403Forbidden, new ApiError(ex.Message, "forbidden")),
            CrmNotFoundException ex => (StatusCodes.Status404NotFound, new ApiError(ex.Message, "not_found")),
            CrmConcurrencyException ex => (StatusCodes.Status409Conflict, new ApiError(ex.Message, "concurrency")),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                new ApiError("Este registro foi alterado por outro usuário. Recarregue e tente novamente.", "concurrency")),
            CrmBusinessException ex => (StatusCodes.Status400BadRequest, new ApiError(ex.Message, ex.Codigo, ex.Detalhes)),
            _ => (0, (ApiError?)null)
        };

        if (error is null) return false;

        if (status >= 500)
        {
            logger.LogError(exception, "Erro não tratado no CRM");
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(error, ct);
        return true;
    }
}
