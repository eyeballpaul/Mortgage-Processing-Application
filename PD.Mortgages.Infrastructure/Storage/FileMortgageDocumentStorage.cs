using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Infrastructure.Storage;

public sealed class FileMortgageDocumentStorage : IMortgageDocumentStorage
{
    private readonly string _rootPath;

    public FileMortgageDocumentStorage(string rootPath)
    {
        _rootPath = Path.GetFullPath(Path.Combine(rootPath, "documents"));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(Guid applicationId, Guid documentId, byte[] contents, CancellationToken cancellationToken = default)
    {
        var applicationPath = GetApplicationPath(applicationId);
        Directory.CreateDirectory(applicationPath);
        var path = GetDocumentPath(applicationId, documentId);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, contents, cancellationToken);
            File.Move(temporaryPath, path, false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<byte[]?> GetAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var path = GetDocumentPath(applicationId, documentId);
        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, cancellationToken)
            : null;
    }

    public Task DeleteAllAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetApplicationPath(applicationId);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetDocumentPath(applicationId, documentId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetApplicationPath(Guid applicationId) => Path.Combine(_rootPath, applicationId.ToString("N"));

    private string GetDocumentPath(Guid applicationId, Guid documentId) =>
        Path.Combine(GetApplicationPath(applicationId), $"{documentId:N}.bin");
}
