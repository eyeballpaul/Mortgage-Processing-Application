using PD.Products.Domain.Entities;

namespace PD.Products.Domain.Repositories;

public interface IMortgageProductRepository
{
    Task<IReadOnlyList<MortgageProduct>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<MortgageProduct?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}
