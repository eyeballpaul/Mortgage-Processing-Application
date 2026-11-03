using System.Net;
using Microsoft.Extensions.Logging;

namespace PD.Mortgages.Infrastructure.Products;

public sealed class ProductServiceResilienceHandler(
    ILogger<ProductServiceResilienceHandler> logger) : DelegatingHandler
{
    private const int MaximumRetries = 2;
    private const int FailureThreshold = 3;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromSeconds(30);
    private int consecutiveFailures;
    private long circuitOpenUntilTicks;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsCircuitOpen())
        {
            logger.LogWarning("Product service circuit is open; short-circuiting {Method} {Uri}", request.Method, request.RequestUri);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                ReasonPhrase = "Product service circuit is open"
            };
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt >= MaximumRetries)
                {
                    if (IsTransient(response.StatusCode))
                    {
                        RecordFailure();
                    }
                    else
                    {
                        ResetCircuit();
                    }

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
            catch (HttpRequestException exception)
            {
                RecordFailure();
                logger.LogError(exception, "Product service request failed after retries for {Method} {Uri}", request.Method, request.RequestUri);
                throw;
            }
        }
    }

    private bool IsCircuitOpen() => DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref circuitOpenUntilTicks);

    private void RecordFailure()
    {
        if (Interlocked.Increment(ref consecutiveFailures) >= FailureThreshold)
        {
            Interlocked.Exchange(ref circuitOpenUntilTicks, DateTimeOffset.UtcNow.Add(CircuitOpenDuration).UtcTicks);
        }
    }

    private void ResetCircuit()
    {
        Interlocked.Exchange(ref consecutiveFailures, 0);
        Interlocked.Exchange(ref circuitOpenUntilTicks, 0);
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
