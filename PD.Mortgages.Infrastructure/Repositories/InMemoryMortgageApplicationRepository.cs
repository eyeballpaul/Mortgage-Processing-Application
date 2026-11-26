using System.Collections.Concurrent;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class InMemoryMortgageApplicationRepository : IMortgageApplicationRepository
{
    private readonly ConcurrentDictionary<Guid, MortgageApplication> _applications = new();
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
            return Task.FromResult(_applications.TryGetValue(id, out var application) ? Copy(application) : null);
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
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void AddCore(MortgageApplication application)
    {
        lock (_sync)
        {
            if (!_applications.TryAdd(application.Id, Copy(application)))
            {
                throw new InvalidOperationException($"Mortgage application '{application.Id}' already exists.");
            }
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
