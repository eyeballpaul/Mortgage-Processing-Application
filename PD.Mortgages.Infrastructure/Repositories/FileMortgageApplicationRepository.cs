using System.Text.Json;
using PD.Mortgages.Domain.Entities;
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
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileMortgageApplicationRepository(MortgagePersistenceOptions options)
    {
        _rootPath = Path.GetFullPath(Path.Combine(options.RootPath, "applications"));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = GetPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<MortgageApplicationSnapshot>(stream, JsonOptions, cancellationToken);
        return snapshot?.ToDomain();
    }

    public async Task AddAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
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
            var path = GetPath(application.Id);
            if (!File.Exists(path))
            {
                throw new KeyNotFoundException($"Mortgage application '{application.Id}' was not found.");
            }

            await WriteAsync(path, MortgageApplicationSnapshot.FromDomain(application), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
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
