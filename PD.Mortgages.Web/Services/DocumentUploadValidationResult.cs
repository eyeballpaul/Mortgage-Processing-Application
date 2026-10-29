namespace PD.Mortgages.Web.Services;

public sealed record DocumentUploadValidationResult(
    string? ErrorMessage,
    string? SafeFileName,
    byte[]? Contents)
{
    public bool IsValid => ErrorMessage is null && SafeFileName is not null && Contents is not null;

    public static DocumentUploadValidationResult Invalid(string message) => new(message, null, null);
}
