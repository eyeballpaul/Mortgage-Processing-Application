using Microsoft.Extensions.Diagnostics.HealthChecks;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Web.Health;

public sealed class ApplicationHealthCheck(
    IMortgageApplicationRepository applicationRepository) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(HealthCheckResult.Healthy("Mortgage application storage is available."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Mortgage application storage is unavailable.", exception));
        }
    }
}
