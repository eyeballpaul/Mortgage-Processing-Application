using Microsoft.AspNetCore.Http;

namespace PD.Mortgages.Web.Services;

public interface IDocumentUploadValidator
{
    Task<DocumentUploadValidationResult> ValidateAsync(
        IFormFile? document,
        int existingDocumentCount,
        long existingBytes,
        CancellationToken cancellationToken = default);
}
