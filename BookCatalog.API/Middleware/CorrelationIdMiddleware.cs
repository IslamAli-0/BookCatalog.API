using System.Diagnostics;

namespace BookCatalog.API.Middleware;

/// <summary>
/// Reads the incoming <c>X-Correlation-ID</c> header (or generates a new one),
/// stores it in <see cref="HttpContext.Items"/>,
/// and echoes it back in the response as <c>X-Correlation-ID</c> and
/// <c>traceparent</c>-compatible <c>trace-id</c> headers.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    internal const string CorrelationIdHeader = "X-Correlation-ID";
    internal const string TraceIdHeader = "trace-id";

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Read caller-supplied ID or mint a new one
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N");

        // 2. Store on the context so controllers / services can access it
        context.Items[CorrelationIdHeader] = correlationId;

        // 3. Echo it back on the response (set before writing the body)
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            // Also expose the W3C trace-id so client APMs can correlate
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            context.Response.Headers[TraceIdHeader] = traceId;
            return Task.CompletedTask;
        });

        // 4. Enrich Serilog log context with the correlation ID so every log
        //    emitted during this request automatically carries it.
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            logger.LogDebug(
                "Correlation ID {CorrelationId} assigned to {Method} {Path}",
                correlationId, context.Request.Method, context.Request.Path);

            await next(context);
        }
    }
}
