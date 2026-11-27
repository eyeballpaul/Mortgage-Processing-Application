using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Types.Enums;

namespace PD.Mortgages.Domain.Repositories;

public interface IMortgageApplicationRepository
{
    Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MortgageApplicationSearchResult> SearchAsync(
        string? search,
        ApplicationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task AddAsync(MortgageApplication application, CancellationToken cancellationToken = default);
    Task AddAsync(MortgageApplication application, MortgageApplicationAuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task UpdateAsync(MortgageApplication application, CancellationToken cancellationToken = default);
    Task UpdateAsync(MortgageApplication application, MortgageApplicationAuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
