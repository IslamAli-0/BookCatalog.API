using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BookCatalog.API.Handlers;

public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Client disconnected -- not an application error
        if (exception is OperationCanceledException)
        {
            logger.LogInformation("Request cancelled by client: {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
            return true;
        }

        // Log full details with request context for correlation
        logger.LogError(exception, "Unhandled exception on {Method} {Path}: {Message}",
            httpContext.Request.Method, httpContext.Request.Path, exception.Message);

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // Use IProblemDetailsService so AddProblemDetails() formatting pipeline is honored
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Server Error",
                Detail = "An unexpected error occurred while processing your request. Please try again later.",
                Instance = httpContext.Request.Path,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            }
        });
    }
}
