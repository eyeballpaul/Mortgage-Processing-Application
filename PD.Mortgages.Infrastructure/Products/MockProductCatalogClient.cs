using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Infrastructure.Products;

public sealed class MockProductCatalogClient(LocalProductCatalogFallback fallback) : IProductCatalogClient
{
    public Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        fallback.GetProductsAsync(cancellationToken);

    public Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(
        decimal propertyValue,
        decimal deposit,
        CancellationToken cancellationToken = default) =>
        fallback.GetEligibleProductsAsync(propertyValue, deposit, cancellationToken);

    public Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        return fallback.GetProductAsync(id, cancellationToken);
    }

    public async Task<bool> IsProductEligibleAsync(string id, decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default) =>
        (await fallback.GetEligibleProductsAsync(propertyValue, deposit, cancellationToken)).Any(product => product.Id == id);
}
