namespace PD.Mortgages.Application.Services;

public sealed class DocumentUploadValidator : IDocumentUploadValidator
{
    private static readonly HashSet<string> PermittedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg"
    };

    public async Task<DocumentUploadValidationResult> ValidateAsync(
        string? fileName,
        long fileLength,
        Stream? contents,
        int existingDocumentCount,
        long existingBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileLength <= 0 || contents is null)
        {
            return DocumentUploadValidationResult.Invalid("Choose a file to upload.");
        }

        if (fileLength > DocumentUploadLimits.MaximumFileBytes)
        {
            return DocumentUploadValidationResult.Invalid("Files must be 5 MB or smaller.");
        }

        if (existingDocumentCount >= DocumentUploadLimits.MaximumDocumentCount ||
            existingBytes + fileLength > DocumentUploadLimits.MaximumApplicationBytes)
        {
            return DocumentUploadValidationResult.Invalid("This application has reached its document limit (10 files or 25 MB total).");
        }

        var extension = Path.GetExtension(fileName);
        if (!PermittedExtensions.Contains(extension))
        {
            return DocumentUploadValidationResult.Invalid("Upload a PDF, PNG or JPEG file.");
        }

        await using var memoryStream = new MemoryStream();
        await contents.CopyToAsync(memoryStream, cancellationToken);
        if (memoryStream.Length != fileLength)
        {
            return DocumentUploadValidationResult.Invalid("The uploaded file could not be read completely.");
        }

        var documentContents = memoryStream.ToArray();
        if (!HasAllowedSignature(extension, documentContents))
        {
            return DocumentUploadValidationResult.Invalid("The file content does not match its file type. Please choose a valid PDF, PNG or JPEG.");
        }

        var safeFileName = Path.GetFileName(fileName.Replace('\\', '/'));
        return new DocumentUploadValidationResult(null, safeFileName, documentContents);
    }

    private static bool HasAllowedSignature(string extension, byte[] contents)
    {
        var header = contents.AsSpan();
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => header.StartsWith("%PDF-"u8),
            ".png" => header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            _ => false
        };
    }
}
