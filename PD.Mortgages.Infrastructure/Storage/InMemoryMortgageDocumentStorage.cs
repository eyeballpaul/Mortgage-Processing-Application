using System.Collections.Concurrent;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Infrastructure.Storage;

public sealed class InMemoryMortgageDocumentStorage : IMortgageDocumentStorage
{
    private readonly ConcurrentDictionary<(Guid ApplicationId, Guid DocumentId), byte[]> _documents = new();

    public Task SaveAsync(Guid applicationId, Guid documentId, byte[] contents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_documents.TryAdd((applicationId, documentId), contents.ToArray()))
        {
            throw new InvalidOperationException("The document could not be stored.");
        }

        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_documents.TryGetValue((applicationId, documentId), out var contents) ? contents.ToArray() : null);
    }
}
