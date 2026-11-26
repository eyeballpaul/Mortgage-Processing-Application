using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PD.Mortgages.Infrastructure.Persistence;

public static class AzureBlobDataProtectionRegistration
{
    public static void AddAzureBlobDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var blobUri = configuration["DataProtection:BlobUri"];
        if (string.IsNullOrWhiteSpace(blobUri))
        {
            return;
        }

        if (!Uri.TryCreate(blobUri, UriKind.Absolute, out var keyBlobUri) ||
            keyBlobUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("DataProtection:BlobUri must be an absolute HTTPS URI.");
        }

        services.AddDataProtection()
            .PersistKeysToAzureBlobStorage(new BlobClient(keyBlobUri, new DefaultAzureCredential()));
    }
}
