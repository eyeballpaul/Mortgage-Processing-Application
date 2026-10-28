using PD.Mortgages.Domain.Entities;

namespace PD.Mortgages.Domain.Repositories;

public interface IMortgageApplicationRepository
{
    Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(MortgageApplication application, CancellationToken cancellationToken = default);
    Task UpdateAsync(MortgageApplication application, CancellationToken cancellationToken = default);
}
