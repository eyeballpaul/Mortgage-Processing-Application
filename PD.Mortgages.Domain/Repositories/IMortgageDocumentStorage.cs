namespace PD.Mortgages.Domain.Repositories;

public interface IMortgageDocumentStorage
{
    Task SaveAsync(Guid applicationId, Guid documentId, byte[] contents, CancellationToken cancellationToken = default);
    Task<byte[]?> GetAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default);
    Task DeleteAllAsync(Guid applicationId, CancellationToken cancellationToken = default);
}
