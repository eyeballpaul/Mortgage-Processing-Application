using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        if (string.Equals(options.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("mortgages")
                ?? throw new InvalidOperationException("ConnectionStrings:mortgages is required when Persistence:Provider is SqlServer.");
            services.AddDbContext<MortgageDbContext>(dbOptions => dbOptions.UseSqlServer(connectionString));
            services.AddScoped<IMortgageApplicationRepository, SqlMortgageApplicationRepository>();
            services.AddScoped<IMortgageApplicationAuditRepository, SqlMortgageApplicationAuditRepository>();
            services.AddSingleton<IMortgageDocumentStorage>(_ => new FileMortgageDocumentStorage(options.RootPath));
            return;
        }

        if (string.Equals(options.Provider, "File", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMortgageApplicationRepository, FileMortgageApplicationRepository>();
            services.AddSingleton<IMortgageApplicationAuditRepository, FileMortgageApplicationAuditRepository>();
            services.AddSingleton<IMortgageDocumentStorage>(_ => new FileMortgageDocumentStorage(options.RootPath));
            return;
        }

        if (string.Equals(options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMortgageApplicationRepository, InMemoryMortgageApplicationRepository>();
            services.AddSingleton<IMortgageApplicationAuditRepository, InMemoryMortgageApplicationAuditRepository>();
            services.AddSingleton<IMortgageDocumentStorage, InMemoryMortgageDocumentStorage>();
            return;
        }

        throw new InvalidOperationException($"Persistence provider '{options.Provider}' is not supported.");
    }

    public static async Task MigrateMortgageDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<MortgagePersistenceOptions>();
        if (!string.Equals(options.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MortgageDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
    }
}
