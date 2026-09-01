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
        if (exception is OperationCanceledException &&
            (httpContext.RequestAborted.IsCancellationRequested || cancellationToken.IsCancellationRequested))
        {
            logger.LogInformation("Request cancelled/aborted: {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);

            // Prevent an accidental empty 200 OK when the exception is marked handled
            {
                const int ClientClosedRequestStatusCode = 499; // Non-standard, but commonly used to represent a client-aborted request.
                httpContext.Response.Clear();
                httpContext.Response.StatusCode = ClientClosedRequestStatusCode;
            }

            return true;
        }

        // Log full details with request context for correlation
        logger.LogError(exception, "Unhandled exception [{TraceId}] on {Method} {Path}: {Message}",
            httpContext.TraceIdentifier, httpContext.Request.Method, httpContext.Request.Path, exception.Message);

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        int statusCode = StatusCodes.Status500InternalServerError;
        string title = "Server Error";
        string detail = "An unexpected error occurred while processing your request. Please try again later.";

        if (exception is ArgumentException argEx)
        {
            statusCode = StatusCodes.Status400BadRequest;
            title = "Validation Error";
            detail = argEx.Message;
        }
        else if (exception is KeyNotFoundException notFoundEx)
        {
            statusCode = StatusCodes.Status404NotFound;
            title = "Not Found";
            detail = notFoundEx.Message;
        }
        else if (exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            statusCode = StatusCodes.Status409Conflict;
            title = "Conflict";
            detail = "The resource was modified by another request. Please retry.";
        }
        else if (exception is InvalidOperationException invEx)
        {
            statusCode = StatusCodes.Status409Conflict;
            title = "Conflict";
            detail = invEx.Message;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = statusCode;

        // Use IProblemDetailsService so AddProblemDetails() formatting pipeline is honored
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            }
        });
    }
}
