using Microsoft.Extensions.Diagnostics.HealthChecks;
using PD.Products.Application.Services;

namespace PD.Products.Api.Health;

public sealed class ProductCatalogHealthCheck(IProductService productService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var products = await productService.GetProductsAsync(cancellationToken);
            return products.Count > 0
                ? HealthCheckResult.Healthy("Product catalogue is available.")
                : HealthCheckResult.Unhealthy("Product catalogue is empty.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Product catalogue is unavailable.", exception);
        }
    }
}
