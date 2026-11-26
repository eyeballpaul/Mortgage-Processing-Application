using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PD.Mortgages.Web.Health;

public sealed class CustomerSessionHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var key = $"pd-mortgages:readiness:{Guid.NewGuid():N}";
        await cache.SetAsync(key, [1], new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        }, cancellationToken);
        var value = await cache.GetAsync(key, cancellationToken);
        await cache.RemoveAsync(key, cancellationToken);
        return value is [1]
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The customer session store did not return the written value.");
    }
}
