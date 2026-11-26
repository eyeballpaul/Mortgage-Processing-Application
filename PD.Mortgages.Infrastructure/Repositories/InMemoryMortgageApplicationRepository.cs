using System.Collections.Concurrent;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class InMemoryMortgageApplicationRepository : IMortgageApplicationRepository, IMortgageDraftLifecycle
{
    private readonly ConcurrentDictionary<Guid, MortgageApplication> _applications = new();
    private readonly Dictionary<Guid, DateTime> _activity = new();
    private readonly Dictionary<Guid, DateTime?> _deleted = new();
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IMortgageApplicationAuditRepository _auditRepository;

    public InMemoryMortgageApplicationRepository(IMortgageApplicationAuditRepository? auditRepository = null)
    {
        _auditRepository = auditRepository ?? new InMemoryMortgageApplicationAuditRepository();
    }

    public IMortgageApplicationAuditRepository AuditRepository => _auditRepository;

    public Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_applications.TryGetValue(id, out var application))
            {
                return Task.FromResult<MortgageApplication?>(null);
            }
            return Task.FromResult<MortgageApplication?>(Copy(application));
        }
    }

    public Task<bool> TouchActivityAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_applications.TryGetValue(id, out var application) || application.Status != ApplicationStatus.InProgress)
                return Task.FromResult(false);
            _activity[id] = DateTime.UtcNow;
            return Task.FromResult(true);
        }
    }

    public Task<MortgageApplicationSearchResult> SearchAsync(
        string? search,
        ApplicationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedSearch = search?.Trim();
        List<MortgageApplication> matches;
        lock (_sync)
        {
            matches = _applications.Values
            .Where(application => status is null || application.Status == status)
            .Where(application => string.IsNullOrEmpty(normalizedSearch) ||
                application.Id.ToString("D").Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                (application.CreatedByEmployeeId?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                $"{application.FirstName} {application.LastName}".Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                (application.Email?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(application => application.UpdatedAtUtc)
            .ThenByDescending(application => application.Id)
            .Select(Copy)
            .ToList();
        }
        var totalCount = matches.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var result = new MortgageApplicationSearchResult(
            matches.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            totalCount,
            page,
            pageSize);
        return Task.FromResult(result);
    }

    public async Task AddAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            AddCore(application);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task AddAsync(
        MortgageApplication application,
        MortgageApplicationAuditEntry auditEntry,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        var added = false;
        try
        {
            AddCore(application);
            added = true;
            await _auditRepository.AppendAsync(application.Id, auditEntry, cancellationToken);
        }
        catch
        {
            if (added)
            {
                lock (_sync)
                {
                    _applications.TryRemove(application.Id, out _);
                    _activity.Remove(application.Id);
                }
            }

            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task UpdateAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            UpdateCore(application);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task UpdateAsync(
        MortgageApplication application,
        MortgageApplicationAuditEntry auditEntry,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        MortgageApplication? current = null;
        var updated = false;
        try
        {
            current = await GetByIdAsync(application.Id, cancellationToken)
                ?? throw new KeyNotFoundException($"Mortgage application '{application.Id}' was not found.");
            UpdateCore(application);
            updated = true;
            await _auditRepository.AppendAsync(application.Id, auditEntry, cancellationToken);
        }
        catch
        {
            if (updated && current is not null)
            {
                lock (_sync)
                {
                    _applications[application.Id] = current;
                }
            }

            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            lock (_sync)
            {
                _applications.TryRemove(id, out _);
                _activity.Remove(id);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<bool> TryDeleteDraftAsync(Guid id, long version, MortgageApplicationAuditEntry auditEntry,
                DateTime? inactiveBeforeUtc, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        MortgageApplication? removed = null;
        DateTime activity = default;
        try
        {
            lock (_sync)
            {
                if (!_applications.TryGetValue(id, out var application) || application.Version != version ||
                    application.Status != ApplicationStatus.InProgress ||
                    (inactiveBeforeUtc is not null && _activity[id] >= inactiveBeforeUtc.Value))
                {
                    return false;
                }
                removed = Copy(application);
                activity = _activity[id];
                _applications.TryRemove(id, out _);
                _activity.Remove(id);
                _deleted[id] = null;
            }
            await _auditRepository.AppendAsync(id, auditEntry, cancellationToken);
            return true;
        }
        catch
        {
            if (removed is not null)
            {
                lock (_sync)
                {
                    _applications[id] = removed;
                    _activity[id] = activity;
                    _deleted.Remove(id);
                }
            }
            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<IReadOnlyList<ExpiredMortgageDraft>> FindExpiredDraftsAsync(
        DateTime inactiveBeforeUtc, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<ExpiredMortgageDraft>>(_applications.Values
                .Where(application => application.Status == ApplicationStatus.InProgress && _activity[application.Id] < inactiveBeforeUtc)
                .OrderBy(application => _activity[application.Id]).Take(limit)
                .Select(application => new ExpiredMortgageDraft(application.Id, application.Version)).ToList());
        }
    }

    public Task<IReadOnlyList<Guid>> FindDocumentsDueForCleanupAsync(
        DateTime cleanedBeforeUtc, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(_deleted
                .Where(entry => entry.Value is null || entry.Value < cleanedBeforeUtc)
                .OrderBy(entry => entry.Value).Take(limit).Select(entry => entry.Key).ToList());
        }
    }

    public Task MarkDocumentsCleanedAsync(Guid id, DateTime cleanedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_deleted.ContainsKey(id)) _deleted[id] = cleanedAtUtc;
        }
        return Task.CompletedTask;
    }

    private void AddCore(MortgageApplication application)
    {
        lock (_sync)
        {
            if (!_applications.TryAdd(application.Id, Copy(application)))
            {
                throw new InvalidOperationException($"Mortgage application '{application.Id}' already exists.");
            }
            _activity[application.Id] = DateTime.UtcNow;
        }
    }

    private void UpdateCore(MortgageApplication application)
    {
        lock (_sync)
        {
            if (!_applications.TryGetValue(application.Id, out var current))
            {
                throw new KeyNotFoundException($"Mortgage application '{application.Id}' was not found.");
            }

            if (current.Version + 1 != application.Version)
            {
                throw new InvalidOperationException("This application was changed elsewhere. Reload it before saving your changes.");
            }

            _applications[application.Id] = Copy(application);
            _activity[application.Id] = DateTime.UtcNow;
        }
    }

    private static MortgageApplication Copy(MortgageApplication application)
    {
        var snapshot = MortgageApplicationSnapshot.FromDomain(application) with
        {
            AuditTrail = application.AuditTrail.ToList()
        };
        return snapshot.ToDomain();
    }
}
