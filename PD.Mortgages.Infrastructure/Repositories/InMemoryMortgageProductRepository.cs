using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class InMemoryMortgageProductRepository : IMortgageProductRepository
{
    private static readonly IReadOnlyList<MortgageProduct> Products =
    [
        new("fixed-2", "2 year fixed", 4.29m, 999m, 5.9m, 2, 90),
        new("fixed-5", "5 year fixed", 4.09m, 999m, 5.5m, 5, 90),
        new("tracker-2", "2 year tracker", 4.64m, 0m, 5.8m, 2, 85)
    ];

    public Task<IReadOnlyList<MortgageProduct>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Products);
    }

    public Task<MortgageProduct?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Products.SingleOrDefault(product => product.Id == id));
    }
}
