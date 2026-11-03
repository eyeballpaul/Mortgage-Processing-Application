using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;

namespace PD.Mortgages.Tests;

public sealed class DurableStorageTests
{
    [Fact]
    public async Task FileRepositoryRoundTripsApplicationStateAndOwner()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var options = new MortgagePersistenceOptions { RootPath = root };
            var repository = new FileMortgageApplicationRepository(options);
            var application = new MortgageApplication();
            application.AssignOwner("customer-123");
            application.UpdateQualification(300_000m, 30_000m);
            application.AddDocument(Guid.NewGuid(), "identity.pdf", 128);

            await repository.AddAsync(application);
            var restored = await repository.GetByIdAsync(application.Id);

            Assert.NotNull(restored);
            Assert.Equal("customer-123", restored.OwnerId);
            Assert.Equal(300_000m, restored.PropertyValue);
            Assert.Equal("identity.pdf", Assert.Single(restored.Documents).FileName);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task FileDocumentStorageSupportsSingleDocumentDeletion()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var storage = new FileMortgageDocumentStorage(root);
            var applicationId = Guid.NewGuid();
            var firstDocumentId = Guid.NewGuid();
            var secondDocumentId = Guid.NewGuid();

            await storage.SaveAsync(applicationId, firstDocumentId, [1, 2, 3]);
            await storage.SaveAsync(applicationId, secondDocumentId, [4, 5, 6]);
            await storage.DeleteAsync(applicationId, firstDocumentId);

            Assert.Null(await storage.GetAsync(applicationId, firstDocumentId));
            Assert.Equal([4, 5, 6], await storage.GetAsync(applicationId, secondDocumentId));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pd-mortgages-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
