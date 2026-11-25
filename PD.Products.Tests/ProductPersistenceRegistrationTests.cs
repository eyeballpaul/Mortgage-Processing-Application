using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PD.Products.Domain.Repositories;
using PD.Products.Infrastructure.Persistence;
using PD.Products.Infrastructure.Repositories;

namespace PD.Products.Tests;

public sealed class ProductPersistenceRegistrationTests
{
    [Fact]
    public void MissingProviderDefaultsToInMemoryRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var services = new ServiceCollection();
        services.AddProductPersistence(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryMortgageProductRepository>(provider.GetRequiredService<IMortgageProductRepository>());
    }

    [Fact]
    public void SqlServerProviderRegistersSqlRepositoryWithoutConnecting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:products"] = "Server=localhost;Database=products;User Id=sa;Password=DesignTimeOnly;TrustServerCertificate=True"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddProductPersistence(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<SqlMortgageProductRepository>(provider.GetRequiredService<IMortgageProductRepository>());
    }
}
