using Microsoft.Extensions.Diagnostics.HealthChecks;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Staff.Web.Health;

public sealed class ApplicationHealthCheck(IMortgageApplicationRepository applicationRepository) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await applicationRepository.GetByIdAsync(Guid.Empty, cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
