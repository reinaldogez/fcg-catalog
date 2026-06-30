using System.Diagnostics;
using System.Text.Json;
using Fcg.Catalog.Domain.Exceptions;

namespace Fcg.Catalog.Api.Middleware;

// Traduz exceções de domínio para application/problem+json (RFC 7807). A ordem de catch
// vai do mais específico (409) ao mais geral (500); DomainException, base das demais, vem
// por último entre as de domínio para não capturá-las antes.
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainConflictException ex)
        {
            await EscreverProblemaAsync(
                context,
                StatusCodes.Status409Conflict,
                "Conflito",
                ex.Message
            );
        }
        catch (DomainAuthException ex)
        {
            await EscreverProblemaAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Não autenticado",
                ex.Message
            );
        }
        catch (AccessDeniedException ex)
        {
            await EscreverProblemaAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Acesso negado",
                ex.Message
            );
        }
        catch (DomainException ex)
        {
            await EscreverProblemaAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Requisição inválida",
                ex.Message
            );
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Cliente desistiu antes da resposta: sem corpo, status 499 (convenção "client closed request").
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Erro não tratado ao processar {Method} {Path}",
                context.Request.Method,
                context.Request.Path
            );
            await EscreverProblemaAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Erro interno",
                "Ocorreu um erro inesperado."
            );
        }
    }

    private static async Task EscreverProblemaAsync(
        HttpContext context,
        int status,
        string titulo,
        string detalhe
    )
    {
        if (context.Response.HasStarted)
            return;

        string traceId = Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        string corpo = JsonSerializer.Serialize(
            new
            {
                type = $"https://httpstatuses.io/{status}",
                title = titulo,
                status,
                detail = detalhe,
                traceId,
            },
            s_json
        );

        await context.Response.WriteAsync(corpo);
    }
}
