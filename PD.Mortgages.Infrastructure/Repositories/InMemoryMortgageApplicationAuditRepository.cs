using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class InMemoryMortgageApplicationAuditRepository : IMortgageApplicationAuditRepository
{
    private readonly Dictionary<Guid, List<MortgageApplicationAuditEntry>> _events = [];
    private readonly Lock _sync = new();

    public Task AppendAsync(Guid applicationId, MortgageApplicationAuditEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_events.TryGetValue(applicationId, out var events))
            {
                events = [];
                _events.Add(applicationId, events);
            }

            events.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task AppendLegacyAsync(Guid applicationId, IReadOnlyCollection<MortgageApplicationAuditEntry> entries, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_events.TryGetValue(applicationId, out var events))
            {
                events = [];
                _events.Add(applicationId, events);
            }

            foreach (var entry in entries)
            {
                if (!events.Contains(entry))
                {
                    events.Add(entry);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<MortgageApplicationAuditPage> GetPageAsync(Guid applicationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pageSize = Math.Clamp(pageSize, 1, 100);
        List<MortgageApplicationAuditEntry> events;
        lock (_sync)
        {
            events = _events.TryGetValue(applicationId, out var storedEvents)
                ? storedEvents.OrderByDescending(entry => entry.RecordedAtUtc).ToList()
                : [];
        }
        var totalCount = events.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var result = new MortgageApplicationAuditPage(
            events.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            totalCount,
            page,
            pageSize);
        return Task.FromResult(result);
    }

}
