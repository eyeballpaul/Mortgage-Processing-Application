using System.Net;
using Microsoft.Extensions.Logging;

namespace PD.Mortgages.Infrastructure.Eligibility;

public sealed class EligibilityResilienceHandler : DelegatingHandler
{
    private readonly ILogger<EligibilityResilienceHandler> logger;
    private readonly TimeSpan requestTimeout;
    private int failures;
    private long openUntilTicks;

    public EligibilityResilienceHandler(ILogger<EligibilityResilienceHandler> logger)
        : this(logger, TimeSpan.FromSeconds(10)) { }

    internal EligibilityResilienceHandler(ILogger<EligibilityResilienceHandler> logger, TimeSpan requestTimeout)
    {
        this.logger = logger;
        this.requestTimeout = requestTimeout;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref openUntilTicks))
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout);
        try
        {
            var response = await base.SendAsync(request, timeout.Token);
            try
            {
                await response.Content.LoadIntoBufferAsync(65_536, timeout.Token);
            }
            catch
            {
                response.Dispose();
                throw;
            }
            if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                RecordFailure();
            else
            {
                Interlocked.Exchange(ref failures, 0);
                Interlocked.Exchange(ref openUntilTicks, 0);
            }
            return response;
        }
        catch (HttpRequestException)
        {
            RecordFailure();
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            RecordFailure();
            logger.LogWarning("Eligibility service request timed out.");
            throw;
        }
    }

    private void RecordFailure()
    {
        if (Interlocked.Increment(ref failures) >= 3)
            Interlocked.Exchange(ref openUntilTicks, DateTimeOffset.UtcNow.AddSeconds(30).UtcTicks);
    }
}
