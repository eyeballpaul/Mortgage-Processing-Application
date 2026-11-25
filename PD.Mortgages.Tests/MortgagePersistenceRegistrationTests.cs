using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Storage;

namespace PD.Mortgages.Tests;

public sealed class MortgagePersistenceRegistrationTests
{
    [Fact]
    public void SqlPersistenceUsesFileStorageForDocuments()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "pd-mortgages-tests", Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["Persistence:RootPath"] = rootPath,
                ["ConnectionStrings:mortgages"] = "Server=localhost;Database=Mortgages;User Id=sa;Password=DesignTimeOnly;TrustServerCertificate=True"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddMortgagePersistence(configuration);

        try
        {
            using var provider = services.BuildServiceProvider();
            Assert.IsType<FileMortgageDocumentStorage>(provider.GetRequiredService<IMortgageDocumentStorage>());
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }
        }
    }
}
