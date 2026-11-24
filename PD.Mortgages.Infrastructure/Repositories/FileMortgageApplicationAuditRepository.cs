using System.Diagnostics;
using System.Text.Json;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class FileMortgageApplicationAuditRepository : IMortgageApplicationAuditRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootPath;

    public FileMortgageApplicationAuditRepository(MortgagePersistenceOptions options)
    {
        _rootPath = Path.GetFullPath(Path.Combine(options.RootPath, "audit-trails"));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task AppendAsync(Guid applicationId, MortgageApplicationAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var repositoryLock = await AcquireLockAsync(applicationId, cancellationToken);
        await AppendWithoutLockAsync(applicationId, [entry], cancellationToken);
    }

    public async Task AppendLegacyAsync(Guid applicationId, IReadOnlyCollection<MortgageApplicationAuditEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await using var repositoryLock = await AcquireLockAsync(applicationId, cancellationToken);
        var migrationMarkerPath = GetLegacyMigrationMarkerPath(applicationId);
        if (File.Exists(migrationMarkerPath))
        {
            return;
        }

        var path = GetPath(applicationId);
        var existing = new HashSet<MortgageApplicationAuditEntry>();
        if (File.Exists(path))
        {
            await using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(readStream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var entry = JsonSerializer.Deserialize<MortgageApplicationAuditEntry>(line, JsonOptions)
                    ?? throw new InvalidDataException($"Audit history for application '{applicationId}' contains an invalid entry.");
                existing.Add(entry);
            }
        }

        var missingEntries = entries.Where(entry => existing.Add(entry)).ToList();
        await AppendWithoutLockAsync(applicationId, missingEntries, cancellationToken);
        await File.WriteAllTextAsync(migrationMarkerPath, string.Empty, cancellationToken);
    }

    public async Task<MortgageApplicationAuditPage> GetPageAsync(Guid applicationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var repositoryLock = await AcquireLockAsync(applicationId, cancellationToken);
        var path = GetPath(applicationId);
        var totalCount = await CountEntriesAsync(path, cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var startIndex = Math.Max(0, totalCount - page * pageSize);
        var endIndex = totalCount - (page - 1) * pageSize;
        var selectedEvents = new List<MortgageApplicationAuditEntry>(Math.Min(pageSize, totalCount));

        if (endIndex > startIndex)
        {
            await using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(readStream);
            var index = 0;
            while (index < endIndex && await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (index >= startIndex)
                {
                    var entry = JsonSerializer.Deserialize<MortgageApplicationAuditEntry>(line, JsonOptions)
                        ?? throw new InvalidDataException($"Audit history for application '{applicationId}' contains an invalid entry.");
                    selectedEvents.Add(entry);
                }

                index++;
            }
        }

        selectedEvents.Reverse();
        return new MortgageApplicationAuditPage(selectedEvents, totalCount, page, pageSize);
    }

    private async Task AppendWithoutLockAsync(Guid applicationId, IReadOnlyCollection<MortgageApplicationAuditEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await using var stream = new FileStream(GetPath(applicationId), FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(entry, JsonOptions).AsMemory(), cancellationToken);
        }
    }

    private static async Task<int> CountEntriesAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        var count = 0;
        while (await reader.ReadLineAsync(cancellationToken) is not null)
        {
            count++;
        }

        return count;
    }

    private async Task<FileStream> AcquireLockAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(_rootPath, $"{applicationId:N}.lock");
        var wait = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (wait.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    private string GetPath(Guid applicationId) => Path.Combine(_rootPath, $"{applicationId:N}.jsonl");

    private string GetLegacyMigrationMarkerPath(Guid applicationId) => Path.Combine(_rootPath, $"{applicationId:N}.legacy-migrated");
}
