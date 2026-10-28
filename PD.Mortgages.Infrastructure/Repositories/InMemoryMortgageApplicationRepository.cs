using System.Collections.Concurrent;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class InMemoryMortgageApplicationRepository : IMortgageApplicationRepository
{
    private readonly ConcurrentDictionary<Guid, MortgageApplication> _applications = new();

    public Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _applications.TryGetValue(id, out var application);
        return Task.FromResult(application);
    }

    public Task AddAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_applications.TryAdd(application.Id, application))
        {
            throw new InvalidOperationException($"Mortgage application '{application.Id}' already exists.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_applications.ContainsKey(application.Id))
        {
            throw new KeyNotFoundException($"Mortgage application '{application.Id}' was not found.");
        }

        _applications[application.Id] = application;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _applications.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
