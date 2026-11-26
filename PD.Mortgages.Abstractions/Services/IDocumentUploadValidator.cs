namespace PD.Mortgages.Application.Services;

public interface IDocumentUploadValidator
{
    Task<DocumentUploadValidationResult> ValidateAsync(
        string? fileName,
        long fileLength,
        Stream? contents,
        int existingDocumentCount,
        long existingBytes,
        CancellationToken cancellationToken = default);
}
