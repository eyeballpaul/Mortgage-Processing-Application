using System.Collections.Concurrent;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class InMemoryMortgageApplicationAuditRepository : IMortgageApplicationAuditRepository
{
    private readonly ConcurrentDictionary<Guid, List<MortgageApplicationAuditEntry>> _events = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public async Task AppendAsync(Guid applicationId, MortgageApplicationAuditEntry entry, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(applicationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            _events.GetOrAdd(applicationId, _ => []).Add(entry);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendLegacyAsync(Guid applicationId, IReadOnlyCollection<MortgageApplicationAuditEntry> entries, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(applicationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var events = _events.GetOrAdd(applicationId, _ => []);
            foreach (var entry in entries)
            {
                if (!events.Contains(entry))
                {
                    events.Add(entry);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<MortgageApplicationAuditPage> GetPageAsync(Guid applicationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pageSize = Math.Clamp(pageSize, 1, 100);
        var events = _events.TryGetValue(applicationId, out var storedEvents)
            ? storedEvents.OrderByDescending(entry => entry.RecordedAtUtc).ToList()
            : [];
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
