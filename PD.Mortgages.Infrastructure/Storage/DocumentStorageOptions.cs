namespace PD.Mortgages.Infrastructure.Storage;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    public string Provider { get; set; } = "File";
    public string? BlobServiceUri { get; set; }
    public string ContainerName { get; set; } = "mortgage-documents";
}
