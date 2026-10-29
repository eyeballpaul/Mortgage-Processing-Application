using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Infrastructure.Products;

public sealed class MockProductCatalogClient : IProductCatalogClient
{
    private static readonly IReadOnlyList<MortgageProductDto> Products =
    [
        new("fixed-2", "2 year fixed", 4.29m, 999m, 5.9m, 2, 90),
        new("fixed-5", "5 year fixed", 4.09m, 999m, 5.5m, 5, 90),
        new("fixed-5-95", "5 year fixed · 95% LTV", 5.19m, 999m, 6.4m, 5, 95),
        new("tracker-2", "2 year tracker", 4.64m, 0m, 5.8m, 2, 85)
    ];

    public Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Products);
    }

    public Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(
        decimal propertyValue,
        decimal deposit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (propertyValue <= 0 || deposit < 0 || deposit >= propertyValue)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyValue), "Enter a valid property value and deposit.");
        }

        var loanToValue = (propertyValue - deposit) / propertyValue * 100;
        var eligibleProducts = Products
            .Where(product => loanToValue <= product.MaximumLoanToValue)
            .ToList();

        return Task.FromResult<IReadOnlyList<MortgageProductDto>>(eligibleProducts);
    }

    public Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Products.SingleOrDefault(product => product.Id == id));
    }
}
