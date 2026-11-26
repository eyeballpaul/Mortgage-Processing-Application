using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Azure.Identity;
using Azure.Storage.Blobs;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;

namespace PD.Mortgages.Infrastructure.Persistence;

public static class MortgagePersistenceRegistration
{
    public static void AddMortgagePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MortgagePersistenceOptions.SectionName)
            .Get<MortgagePersistenceOptions>() ?? new MortgagePersistenceOptions();
        services.AddSingleton(options);
        RegisterDocumentStorage(services, configuration, options);

        if (string.Equals(options.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("mortgages")
                ?? throw new InvalidOperationException("ConnectionStrings:mortgages is required when Persistence:Provider is SqlServer.");
            services.AddDbContext<MortgageDbContext>(dbOptions => dbOptions.UseSqlServer(connectionString));
            services.AddScoped<IMortgageApplicationRepository, SqlMortgageApplicationRepository>();
            services.AddScoped<IMortgageApplicationAuditRepository, SqlMortgageApplicationAuditRepository>();
            return;
        }

        if (string.Equals(options.Provider, "File", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMortgageApplicationRepository, FileMortgageApplicationRepository>();
            services.AddSingleton<IMortgageApplicationAuditRepository, FileMortgageApplicationAuditRepository>();
            return;
        }

        if (string.Equals(options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMortgageApplicationRepository, InMemoryMortgageApplicationRepository>();
            services.AddSingleton<IMortgageApplicationAuditRepository, InMemoryMortgageApplicationAuditRepository>();
            return;
        }

        throw new InvalidOperationException($"Persistence provider '{options.Provider}' is not supported.");
    }

    public static async Task MigrateMortgageDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<MortgagePersistenceOptions>();
        if (!options.RunMigrationsOnStartup ||
            !string.Equals(options.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MortgageDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
    }

    private static void RegisterDocumentStorage(
        IServiceCollection services,
        IConfiguration configuration,
        MortgagePersistenceOptions persistenceOptions)
    {
        var options = configuration.GetSection(DocumentStorageOptions.SectionName)
            .Get<DocumentStorageOptions>() ?? new DocumentStorageOptions();
        services.AddSingleton(options);

        if (string.Equals(options.Provider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(options.BlobServiceUri, UriKind.Absolute, out var serviceUri) ||
                serviceUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("DocumentStorage:BlobServiceUri must be an absolute HTTPS URI when AzureBlob storage is selected.");
            }

            if (string.IsNullOrWhiteSpace(options.ContainerName))
            {
                throw new InvalidOperationException("DocumentStorage:ContainerName is required when AzureBlob storage is selected.");
            }

            services.AddSingleton(new BlobServiceClient(serviceUri, new DefaultAzureCredential()));
            services.AddSingleton<IMortgageDocumentStorage>(provider =>
            {
                var client = provider.GetRequiredService<BlobServiceClient>();
                return new AzureBlobMortgageDocumentStorage(client.GetBlobContainerClient(options.ContainerName));
            });
            return;
        }

        if (!string.Equals(options.Provider, "File", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Document storage provider '{options.Provider}' is not supported.");
        }

        if (string.Equals(persistenceOptions.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMortgageDocumentStorage, InMemoryMortgageDocumentStorage>();
        }
        else
        {
            services.AddSingleton<IMortgageDocumentStorage>(_ => new FileMortgageDocumentStorage(persistenceOptions.RootPath));
        }
    }
}
