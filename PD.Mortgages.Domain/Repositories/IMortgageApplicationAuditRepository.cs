using PD.Mortgages.Domain.Entities;

namespace PD.Mortgages.Domain.Repositories;

public interface IMortgageApplicationAuditRepository
{
    Task AppendAsync(Guid applicationId, MortgageApplicationAuditEntry entry, CancellationToken cancellationToken = default);
    Task AppendLegacyAsync(Guid applicationId, IReadOnlyCollection<MortgageApplicationAuditEntry> entries, CancellationToken cancellationToken = default);
    Task<MortgageApplicationAuditPage> GetPageAsync(Guid applicationId, int page, int pageSize, CancellationToken cancellationToken = default);
}
