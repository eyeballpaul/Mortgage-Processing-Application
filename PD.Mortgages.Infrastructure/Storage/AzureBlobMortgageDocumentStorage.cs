using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Infrastructure.Storage;

public sealed class AzureBlobMortgageDocumentStorage(BlobContainerClient containerClient) : IMortgageDocumentStorage
{
    public async Task SaveAsync(
        Guid applicationId,
        Guid documentId,
        byte[] contents,
        CancellationToken cancellationToken = default)
    {
        var blob = GetBlob(applicationId, documentId);
        await blob.UploadAsync(BinaryData.FromBytes(contents), overwrite: false, cancellationToken);
    }

    public async Task<byte[]?> GetAsync(
        Guid applicationId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await GetBlob(applicationId, documentId).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToArray();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(
        Guid applicationId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        await GetBlob(applicationId, documentId).DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);
    }

    public async Task DeleteAllAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var prefix = $"{applicationId:N}/";
        await foreach (var blob in containerClient.GetBlobsAsync(
                           traits: BlobTraits.None,
                           states: BlobStates.None,
                           prefix: prefix,
                           cancellationToken: cancellationToken))
        {
            await containerClient.DeleteBlobIfExistsAsync(
                blob.Name,
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken);
        }
    }

    private BlobClient GetBlob(Guid applicationId, Guid documentId) =>
        containerClient.GetBlobClient($"{applicationId:N}/{documentId:N}.bin");
}
