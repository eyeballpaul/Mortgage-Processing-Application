using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PD.Mortgages.CustomerSessions;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Web.Configuration;

namespace PD.Mortgages.Tests;

public sealed class CustomerSessionStorageTests
{
    [Fact]
    public void SessionDatabaseMigrationMatchesItsModelAndContainsOnlyTheCache()
    {
        var options = new DbContextOptionsBuilder<CustomerSessionDbContext>()
            .UseSqlServer("Server=localhost;Database=customer-sessions;User Id=sa;Password=NotUsed;TrustServerCertificate=True")
            .Options;
        using var context = new CustomerSessionDbContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Single(context.Database.GetMigrations());
        var script = context.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE [dbo].[MortgageSessionCache]", script);
        Assert.DoesNotContain("MortgageApplications", script);
        Assert.DoesNotContain("MortgageApplicationAuditEntries", script);
    }

    [Fact]
    public void MortgageDatabaseMigrationMatchesItsModelAndExcludesTheSessionCache()
    {
        var options = new DbContextOptionsBuilder<MortgageDbContext>()
            .UseSqlServer("Server=localhost;Database=mortgages;User Id=sa;Password=NotUsed;TrustServerCertificate=True")
            .Options;
        using var context = new MortgageDbContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Single(context.Database.GetMigrations());
        var script = context.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE [MortgageApplications]", script);
        Assert.Contains("CREATE TABLE [MortgageApplicationAuditEntries]", script);
        Assert.DoesNotContain("MortgageSessionCache", script);
    }

    [Fact]
    public void SqlSessionProviderRequiresItsOwnConnectionString()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Session:Provider"] = "SqlServer",
            ["ConnectionStrings:mortgages"] = "mortgage database",
            ["ProductService:BaseUrl"] = "https://products.example/"
        }).Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(new ProductionEnvironment(), configuration));
        Assert.Contains("ConnectionStrings:customersessions", exception.Message);
    }

    [Fact]
    public void ProductionCannotFallBackToInMemorySessions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Session:Provider"] = "Memory",
            ["DataProtection:BlobUri"] = "https://example.blob.core.windows.net/keys/keys.xml"
        }).Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(new ProductionEnvironment(), configuration));
        Assert.Contains("Session:Provider must be Redis or SqlServer", exception.Message);
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
