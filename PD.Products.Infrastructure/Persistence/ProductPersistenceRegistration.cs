using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PD.Products.Domain.Repositories;
using PD.Products.Infrastructure.Repositories;

namespace PD.Products.Infrastructure.Persistence;

public static class ProductPersistenceRegistration
{
    public static void AddProductPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";

        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("products")
                ?? throw new InvalidOperationException("ConnectionStrings:products is required when Persistence:Provider is SqlServer.");
            services.AddDbContext<ProductsDbContext>(options => options.UseSqlServer(connectionString));
            services.AddScoped<IMortgageProductRepository, SqlMortgageProductRepository>();
            return;
        }

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMortgageProductRepository, InMemoryMortgageProductRepository>();
            return;
        }

        throw new InvalidOperationException($"Product persistence provider '{provider}' is not supported.");
    }

    public static async Task MigrateProductDatabaseAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var migrationsDisabled = bool.TryParse(configuration["Persistence:RunMigrationsOnStartup"], out var runMigrations)
            && !runMigrations;
        if (migrationsDisabled ||
            !string.Equals(configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ProductsDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
    }
}
