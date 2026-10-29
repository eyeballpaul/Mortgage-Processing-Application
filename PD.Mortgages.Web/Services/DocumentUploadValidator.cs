using Microsoft.AspNetCore.Http;

namespace PD.Mortgages.Web.Services;

public sealed class DocumentUploadValidator : IDocumentUploadValidator
{
    private static readonly HashSet<string> PermittedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg"
    };

    public async Task<DocumentUploadValidationResult> ValidateAsync(
        IFormFile? document,
        int existingDocumentCount,
        long existingBytes,
        CancellationToken cancellationToken = default)
    {
        if (document is null || document.Length == 0)
        {
            return DocumentUploadValidationResult.Invalid("Choose a file to upload.");
        }

        if (document.Length > DocumentUploadLimits.MaximumFileBytes)
        {
            return DocumentUploadValidationResult.Invalid("Files must be 5 MB or smaller.");
        }

        if (existingDocumentCount >= DocumentUploadLimits.MaximumDocumentCount ||
            existingBytes + document.Length > DocumentUploadLimits.MaximumApplicationBytes)
        {
            return DocumentUploadValidationResult.Invalid("This application has reached its document limit (10 files or 25 MB total).");
        }

        var extension = Path.GetExtension(document.FileName);
        if (!PermittedExtensions.Contains(extension))
        {
            return DocumentUploadValidationResult.Invalid("Upload a PDF, PNG or JPEG file.");
        }

        await using var memoryStream = new MemoryStream();
        await document.CopyToAsync(memoryStream, cancellationToken);
        var contents = memoryStream.ToArray();
        if (!HasAllowedSignature(extension, contents))
        {
            return DocumentUploadValidationResult.Invalid("The file content does not match its file type. Please choose a valid PDF, PNG or JPEG.");
        }

        return new DocumentUploadValidationResult(null, Path.GetFileName(document.FileName), contents);
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
