using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PD.Shared;

public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    private static readonly string ServiceName = typeof(RequestTelemetryMiddleware).Assembly.GetName().Name ?? "PD.Service";
    private static readonly Meter Meter = new(ServiceName);
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>(ServiceName switch
    {
        "PD.Mortgages.Web" => "mortgage_web_requests_total",
        "PD.Products.Api" => "product_api_requests_total",
        "PD.Mortgages.Api" => "mortgage_api_requests_total",
        _ => "staff_web_requests_total"
    });

    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        var suppliedId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        var correlationId = suppliedId is { Length: > 0 and <= 64 } && suppliedId.All(char.IsLetterOrDigit)
            ? suppliedId
            : activity?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            if (!context.Request.Path.StartsWithSegments("/health"))
            {
                var route = context.GetEndpoint()?.DisplayName ?? "unmatched";
                RequestCounter.Add(1, new KeyValuePair<string, object?>("route", route));
                logger.LogInformation(
                    "HTTP {Method} {Route} returned {StatusCode} in {ElapsedMs} ms; TraceId {TraceId}, SpanId {SpanId}, CorrelationId {CorrelationId}",
                    context.Request.Method,
                    route,
                    failed ? 500 : context.Response.StatusCode,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString(),
                    correlationId);
            }
        }
    }
}
