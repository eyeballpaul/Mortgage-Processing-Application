using PD.Mortgages.Domain.Entities;

namespace PD.Mortgages.Domain.Repositories;

public interface IMortgageProductRepository
{
    Task<IReadOnlyList<MortgageProduct>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<MortgageProduct?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}
