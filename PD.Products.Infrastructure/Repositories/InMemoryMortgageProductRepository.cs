using PD.Products.Domain.Entities;
using PD.Products.Domain.Repositories;
using PD.Products.Infrastructure.Persistence;

namespace PD.Products.Infrastructure.Repositories;

public sealed class InMemoryMortgageProductRepository : IMortgageProductRepository
{
    public Task<IReadOnlyList<MortgageProduct>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MortgageProductSeedData.Products);
    }

    public Task<MortgageProduct?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MortgageProductSeedData.Products.SingleOrDefault(product => product.Id == id));
    }
}
