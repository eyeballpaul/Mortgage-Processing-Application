using Microsoft.Extensions.Diagnostics.HealthChecks;
using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Api.Health;

public sealed class ProductDependencyHealthCheck(IProductCatalogClient catalog) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var products = await catalog.GetProductsAsync(cancellationToken);
        return products.Count > 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The product catalog is empty.");
    }
}
