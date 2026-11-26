using Microsoft.Extensions.Diagnostics.HealthChecks;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Api.Health;

public sealed class ApplicationHealthCheck(IMortgageApplicationRepository repository) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await repository.GetByIdAsync(Guid.Empty, cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
