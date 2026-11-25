using Microsoft.EntityFrameworkCore;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Infrastructure.Storage;

public sealed class SqlMortgageDocumentStorage(MortgageDbContext dbContext) : IMortgageDocumentStorage
{
    public async Task SaveAsync(
        Guid applicationId,
        Guid documentId,
        byte[] contents,
        CancellationToken cancellationToken = default)
    {
        dbContext.DocumentContents.Add(new MortgageDocumentContentRecord
        {
            ApplicationId = applicationId,
            DocumentId = documentId,
            Contents = contents.ToArray()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<byte[]?> GetAsync(
        Guid applicationId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.DocumentContents.AsNoTracking()
            .SingleOrDefaultAsync(
                stored => stored.ApplicationId == applicationId && stored.DocumentId == documentId,
                cancellationToken);
        return document?.Contents.ToArray();
    }

    public async Task DeleteAllAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var documents = await dbContext.DocumentContents
            .Where(document => document.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
        dbContext.DocumentContents.RemoveRange(documents);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid applicationId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.DocumentContents
            .SingleOrDefaultAsync(
                stored => stored.ApplicationId == applicationId && stored.DocumentId == documentId,
                cancellationToken);
        if (document is null)
        {
            return;
        }

        dbContext.DocumentContents.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
