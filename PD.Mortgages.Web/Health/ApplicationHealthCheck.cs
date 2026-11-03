using Microsoft.Extensions.Diagnostics.HealthChecks;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Web.Health;

public sealed class ApplicationHealthCheck(
    IMortgageApplicationRepository applicationRepository) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await applicationRepository.GetByIdAsync(Guid.Empty, cancellationToken);
            return HealthCheckResult.Healthy("Mortgage application storage is available.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Mortgage application storage is unavailable.", exception);
        }
    }
}
