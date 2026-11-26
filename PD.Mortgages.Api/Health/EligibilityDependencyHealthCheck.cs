using Microsoft.Extensions.Diagnostics.HealthChecks;
using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Api.Health;

public sealed class EligibilityDependencyHealthCheck(IEligibilityClient eligibility) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await eligibility.AssessAsync(300_000, 30_000, 100_000, false, cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
