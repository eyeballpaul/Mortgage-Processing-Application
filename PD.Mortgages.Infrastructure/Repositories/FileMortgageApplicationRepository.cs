using System.Diagnostics;
using System.Text.Json;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class FileMortgageApplicationRepository : IMortgageApplicationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _rootPath;
    private readonly string _lockPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileMortgageApplicationRepository(MortgagePersistenceOptions options)
    {
        _rootPath = Path.GetFullPath(Path.Combine(options.RootPath, "applications"));
        _lockPath = Path.Combine(_rootPath, ".repository.lock");
        Directory.CreateDirectory(_rootPath);
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
