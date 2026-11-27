using System.Diagnostics;
using System.Text.Json;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Types.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class FileMortgageApplicationRepository : IMortgageApplicationRepository, IMortgageDraftLifecycle
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _rootPath;
    private readonly string _lockPath;
    private readonly string _deletedPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IMortgageApplicationAuditRepository? _auditRepository;

    public FileMortgageApplicationRepository(
        MortgagePersistenceOptions options,
        IMortgageApplicationAuditRepository? auditRepository = null)
    {
        _rootPath = Path.GetFullPath(Path.Combine(options.RootPath, "applications"));
        _lockPath = Path.Combine(_rootPath, ".repository.lock");
        _deletedPath = Path.Combine(_rootPath, "deleted");
        _auditRepository = auditRepository;
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_deletedPath);
    }

    public async Task<MortgageApplicationSearchResult> SearchAsync(
        string? search,
        ApplicationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var normalizedSearch = search?.Trim();
            var applications = new List<MortgageApplication>();
            foreach (var path in Directory.EnumerateFiles(_rootPath, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(path);
                var snapshot = await JsonSerializer.DeserializeAsync<MortgageApplicationSnapshot>(stream, JsonOptions, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                var application = snapshot.ToDomain();
                if ((status is null || application.Status == status) &&
                    (string.IsNullOrEmpty(normalizedSearch) ||
                     application.Id.ToString("D").Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                     (application.CreatedByEmployeeId?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                     $"{application.FirstName} {application.LastName}".Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                     (application.Email?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false)))
                {
                    applications.Add(application);
                }
            }

            applications = applications
                .OrderByDescending(application => application.UpdatedAtUtc)
                .ThenByDescending(application => application.Id)
                .ToList();
            var totalCount = applications.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);
            return new MortgageApplicationSearchResult(
                applications.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                totalCount,
                page,
                pageSize);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var path = GetPath(id);
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<MortgageApplicationSnapshot>(stream, JsonOptions, cancellationToken);
            return snapshot?.ToDomain();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TouchActivityAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var path = GetPath(id);
            if (!File.Exists(path))
                return false;
            MortgageApplicationSnapshot? snapshot;
            await using (var stream = File.OpenRead(path))
                snapshot = await JsonSerializer.DeserializeAsync<MortgageApplicationSnapshot>(stream, JsonOptions, cancellationToken);
            if (snapshot is null || snapshot.Status != ApplicationStatus.InProgress)
                return false;
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var path = GetPath(application.Id);
            if (File.Exists(path))
            {
                throw new InvalidOperationException($"Mortgage application '{application.Id}' already exists.");
            }

            await WriteAsync(path, MortgageApplicationSnapshot.FromDomain(application), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(
        MortgageApplication application,
        MortgageApplicationAuditEntry auditEntry,
        CancellationToken cancellationToken = default)
    {
        if (_auditRepository is null)
        {
            throw new InvalidOperationException("An audit repository is required for audited application writes.");
        }

        await AddAsync(application, cancellationToken);
        try
        {
            await _auditRepository.AppendAsync(application.Id, auditEntry, cancellationToken);
        }
        catch (Exception auditException)
        {
            try
            {
                await DeleteAsync(application.Id, CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The application was saved but its audit event failed and the new application could not be removed.",
                    auditException,
                    rollbackException);
            }

            throw;
        }
    }

    public async Task UpdateAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var path = GetPath(application.Id);
            if (!File.Exists(path))
            {
                throw new KeyNotFoundException($"Mortgage application '{application.Id}' was not found.");
            }

            MortgageApplicationSnapshot current;
            await using (var stream = File.OpenRead(path))
            {
                current = await JsonSerializer.DeserializeAsync<MortgageApplicationSnapshot>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException($"Mortgage application '{application.Id}' could not be read.");
            }

            if (current.Version + 1 != application.Version)
            {
                throw new InvalidOperationException("This application was changed elsewhere. Reload it before saving your changes.");
            }

            await WriteAsync(path, MortgageApplicationSnapshot.FromDomain(application), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(
        MortgageApplication application,
        MortgageApplicationAuditEntry auditEntry,
        CancellationToken cancellationToken = default)
    {
        if (_auditRepository is null)
        {
            throw new InvalidOperationException("An audit repository is required for audited application writes.");
        }

        var previous = await GetByIdAsync(application.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mortgage application '{application.Id}' was not found.");
        await UpdateAsync(application, cancellationToken);
        try
        {
            await _auditRepository.AppendAsync(application.Id, auditEntry, cancellationToken);
        }
        catch (Exception auditException)
        {
            try
            {
                await RestoreAsync(previous, application.Version, CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The application was saved but its audit event failed and the application rollback also failed.",
                    auditException,
                    rollbackException);
            }

            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var path = GetPath(id);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryDeleteDraftAsync(Guid id, long version, MortgageApplicationAuditEntry auditEntry,
                DateTime? inactiveBeforeUtc, CancellationToken cancellationToken = default)
    {
        if (_auditRepository is null)
        {
            throw new InvalidOperationException("An audit repository is required for application deletion.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var path = GetPath(id);
            if (!File.Exists(path) ||
                (inactiveBeforeUtc is not null && File.GetLastWriteTimeUtc(path) >= inactiveBeforeUtc.Value))
            {
                return false;
            }

            MortgageApplicationSnapshot snapshot;
            await using (var stream = File.OpenRead(path))
            {
                snapshot = await JsonSerializer.DeserializeAsync<MortgageApplicationSnapshot>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException($"Mortgage application '{id}' could not be read.");
            }
            if (snapshot.Version != version || snapshot.Status != ApplicationStatus.InProgress)
            {
                return false;
            }

            await _auditRepository.AppendAsync(id, auditEntry, cancellationToken);
            var tombstone = GetDeletedPath(id);
            File.Move(path, tombstone);
            await File.WriteAllTextAsync(tombstone, "", cancellationToken);
            File.SetLastWriteTimeUtc(tombstone, DateTime.UnixEpoch);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ExpiredMortgageDraft>> FindExpiredDraftsAsync(
        DateTime inactiveBeforeUtc, int limit, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var drafts = new List<ExpiredMortgageDraft>();
            foreach (var path in Directory.EnumerateFiles(_rootPath, "*.json").OrderBy(File.GetLastWriteTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.GetLastWriteTimeUtc(path) >= inactiveBeforeUtc) continue;
                await using var stream = File.OpenRead(path);
                var snapshot = await JsonSerializer.DeserializeAsync<MortgageApplicationSnapshot>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException($"Mortgage application file '{path}' could not be read.");
                if (snapshot.Status == ApplicationStatus.InProgress)
                {
                    drafts.Add(new ExpiredMortgageDraft(snapshot.Id, snapshot.Version));
                    if (drafts.Count == limit) break;
                }
            }
            return drafts;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<Guid>> FindDocumentsDueForCleanupAsync(
        DateTime cleanedBeforeUtc, int limit, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            return Directory.EnumerateFiles(_deletedPath, "*.deleted")
                .Where(path => File.GetLastWriteTimeUtc(path) < cleanedBeforeUtc)
                .OrderBy(File.GetLastWriteTimeUtc).Take(limit)
                .Select(path => Guid.ParseExact(Path.GetFileNameWithoutExtension(path), "N")).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkDocumentsCleanedAsync(Guid id, DateTime cleanedAtUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var path = GetDeletedPath(id);
            if (File.Exists(path))
            {
                await File.WriteAllTextAsync(path, "", cancellationToken);
                File.SetLastWriteTimeUtc(path, cleanedAtUtc);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileStream> AcquireRepositoryLockAsync(CancellationToken cancellationToken)
    {
        var wait = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (wait.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    private string GetPath(Guid id) => Path.Combine(_rootPath, $"{id:N}.json");
    private string GetDeletedPath(Guid id) => Path.Combine(_deletedPath, $"{id:N}.deleted");

    private async Task RestoreAsync(
        MortgageApplication application,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var repositoryLock = await AcquireRepositoryLockAsync(cancellationToken);
            var path = GetPath(application.Id);
            await using var stream = File.OpenRead(path);
            var current = await JsonSerializer.DeserializeAsync<MortgageApplicationSnapshot>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException($"Mortgage application '{application.Id}' could not be read.");
            if (current.Version != expectedVersion)
            {
                throw new InvalidOperationException("The application changed before its failed audit write could be rolled back.");
            }

            await WriteAsync(path, MortgageApplicationSnapshot.FromDomain(application), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task WriteAsync(string path, MortgageApplicationSnapshot snapshot, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
