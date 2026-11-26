using PD.Mortgages.Domain.Entities;

namespace PD.Mortgages.Domain.Repositories;

public sealed record ExpiredMortgageDraft(Guid Id, long Version);

public interface IMortgageDraftLifecycle
{
    Task<bool> TouchActivityAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> TryDeleteDraftAsync(Guid id, long version, MortgageApplicationAuditEntry auditEntry,
        DateTime? inactiveBeforeUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpiredMortgageDraft>> FindExpiredDraftsAsync(
        DateTime inactiveBeforeUtc, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> FindDocumentsDueForCleanupAsync(
        DateTime cleanedBeforeUtc, int limit, CancellationToken cancellationToken = default);
    Task MarkDocumentsCleanedAsync(Guid id, DateTime cleanedAtUtc, CancellationToken cancellationToken = default);
}
