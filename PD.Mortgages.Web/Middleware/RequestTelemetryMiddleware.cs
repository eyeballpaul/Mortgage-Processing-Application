using System.Diagnostics.Metrics;

namespace PD.Mortgages.Web.Middleware;

public sealed class RequestTelemetryMiddleware(
    RequestDelegate next,
    ILogger<RequestTelemetryMiddleware> logger)
{
    private static readonly Meter Meter = new("PD.Mortgages.Web");
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>("mortgage_web_requests_total");

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        try
        {
            await next(context);
        }
        finally
        {
            RequestCounter.Add(1, new KeyValuePair<string, object?>("route", context.Request.Path.ToString()));
            logger.LogInformation(
                "Mortgage web request {Method} {Path} returned {StatusCode}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode);
        }
    }
}
