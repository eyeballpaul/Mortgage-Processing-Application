using System.Net;
using Microsoft.Extensions.Logging;

namespace PD.Mortgages.Infrastructure.Products;

public sealed class ProductServiceResilienceHandler(
    ILogger<ProductServiceResilienceHandler> logger) : DelegatingHandler
{
    private const int MaximumRetries = 2;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt >= MaximumRetries)
                {
                    return response;
                }

                response.Dispose();
                await DelayAsync(attempt, request, cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt < MaximumRetries)
            {
                logger.LogWarning(
                    exception,
                    "Transient product service request failure for {Method} {Uri}; retry {RetryNumber} of {MaximumRetries}",
                    request.Method,
                    request.RequestUri,
                    attempt + 1,
                    MaximumRetries);
                await DelayAsync(attempt, request, cancellationToken);
            }
        }
    }

    private async Task DelayAsync(int attempt, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100 + Random.Shared.Next(0, 100));
        logger.LogWarning(
            "Transient product service response for {Method} {Uri}; retry {RetryNumber} of {MaximumRetries} after {DelayMilliseconds} ms",
            request.Method,
            request.RequestUri,
            attempt + 1,
            MaximumRetries,
            delay.TotalMilliseconds);
        await Task.Delay(delay, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;
}
