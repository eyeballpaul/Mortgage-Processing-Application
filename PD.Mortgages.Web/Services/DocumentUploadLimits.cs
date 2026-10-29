namespace PD.Mortgages.Web.Services;

public static class DocumentUploadLimits
{
    public const long MaximumFileBytes = 5 * 1024 * 1024;
    public const int MaximumDocumentCount = 10;
    public const long MaximumApplicationBytes = 25 * 1024 * 1024;
}
