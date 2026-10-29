using System.Collections.Concurrent;

namespace PD.Products.Api.Middleware;

public sealed class SimpleRateLimitMiddleware(RequestDelegate next)
{
    private const int PermitLimit = 60;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, WindowCounter> counters = new();

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTimeOffset.UtcNow;
        var counter = counters.AddOrUpdate(
            key,
            _ => new WindowCounter(now, 1),
            (_, current) => current.IsExpired(now, Window)
                ? new WindowCounter(now, 1)
                : current with { Count = current.Count + 1 });

        if (counter.Count > PermitLimit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = ((int)Window.TotalSeconds).ToString();
            return;
        }

        await next(context);
    }

    private sealed record WindowCounter(DateTimeOffset StartedAt, int Count)
    {
        public bool IsExpired(DateTimeOffset now, TimeSpan window) => now - StartedAt >= window;
    }
}
