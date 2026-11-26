using System.Text.Json;
using System.Text.Json.Nodes;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;

namespace PD.Mortgages.Tests;

public sealed class DurableStorageTests
{
    [Fact]
    public void OlderSnapshotsKeepHistoricalConsentRecordsWhenResaved()
    {
        var application = new MortgageApplication();
        application.RecordConsents(true, "terms-1", "customer-1");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.SerializeToNode(MortgageApplicationSnapshot.FromDomain(application), options)!.AsObject();
        json["removedConsent"] = true;
        json["consentHistory"]!.AsArray().Add(JsonSerializer.SerializeToNode(
            new CustomerConsent((ConsentPurpose)1, true, "terms-1", DateTime.UnixEpoch, "customer-1"), options));

        var snapshot = json.Deserialize<MortgageApplicationSnapshot>(options)!;
        var restored = snapshot.ToDomain();
        restored.RecordConsents(false, "terms-2", "customer-1");
        var resaved = JsonSerializer.SerializeToNode(MortgageApplicationSnapshot.FromDomain(restored), options)!.AsObject();

        Assert.True(restored.ConsentsRecorded);
        Assert.False(restored.CreditCheckConsent);
        Assert.Equal(3, restored.ConsentHistory.Count);
        Assert.Equal(1, resaved["consentHistory"]![1]!["purpose"]!.GetValue<int>());
        Assert.Equal("customer-1", resaved["consentHistory"]![1]!["actorId"]!.GetValue<string>());
        Assert.False(resaved.ContainsKey("removedConsent"));
    }

    [Fact]
    public async Task FileRepositoryRoundTripsApplicationStateAndOwner()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var options = new MortgagePersistenceOptions { RootPath = root };
            var repository = new FileMortgageApplicationRepository(options);
            var auditRepository = new FileMortgageApplicationAuditRepository(options);
            var application = MortgageApplication.CreateForEmployee("staff-42");
            application.AssignOwner("customer-123");
            application.UpdateQualification(300_000m, 30_000m);
            application.RecordConsents(true, "terms-1", "staff-42");
            application.AddDocument(Guid.NewGuid(), "identity.pdf", 128);
            application.RecordAudit("staff-42", "Application created");

            await repository.AddAsync(application);
            await auditRepository.AppendAsync(application.Id, Assert.Single(application.AuditTrail));
            var restored = await repository.GetByIdAsync(application.Id);
            var auditPage = await auditRepository.GetPageAsync(application.Id, 1, 25);
            var listed = await repository.SearchAsync(null, null, 1, 25);

            Assert.NotNull(restored);
            Assert.Equal("customer-123", restored.OwnerId);
            Assert.Equal("staff-42", restored.CreatedByEmployeeId);
            Assert.Equal(300_000m, restored.PropertyValue);
            Assert.Single(restored.ConsentHistory);
            Assert.All(restored.ConsentHistory, consent => Assert.Equal("terms-1", consent.TermsVersion));
            Assert.Equal("identity.pdf", Assert.Single(restored.Documents).FileName);
            Assert.Empty(restored.AuditTrail);
            Assert.Equal("Application created", Assert.Single(auditPage.Events).Action);
            Assert.Equal(1, listed.TotalCount);
            Assert.Equal(application.Id, Assert.Single(listed.Applications).Id);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task FileAuditRepositoryPagesNewestFirstAndMigratesLegacyEventsOnlyOnce()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var repository = new FileMortgageApplicationAuditRepository(new MortgagePersistenceOptions { RootPath = root });
            var applicationId = Guid.NewGuid();
            var events = Enumerable.Range(0, 31)
                .Select(index => new MortgageApplicationAuditEntry(DateTime.UtcNow.AddSeconds(index), "staff-42", $"Event {index}"))
                .ToList();

            await repository.AppendLegacyAsync(applicationId, events);
            await repository.AppendLegacyAsync(applicationId, events);
            var firstPage = await repository.GetPageAsync(applicationId, 1, 25);
            var secondPage = await repository.GetPageAsync(applicationId, 2, 25);

            Assert.Equal(31, firstPage.TotalCount);
            Assert.Equal(2, firstPage.TotalPages);
            Assert.Equal("Event 30", firstPage.Events[0].Action);
            Assert.Equal("Event 6", firstPage.Events[^1].Action);
            Assert.Equal(6, secondPage.Events.Count);
            Assert.Equal("Event 5", secondPage.Events[0].Action);
            Assert.Equal("Event 0", secondPage.Events[^1].Action);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task FileRepositoryRejectsAnUpdateBasedOnAStaleSnapshot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var repository = new FileMortgageApplicationRepository(new MortgagePersistenceOptions { RootPath = root });
            var application = new MortgageApplication();
            await repository.AddAsync(application);
            var firstCopy = await repository.GetByIdAsync(application.Id);
            var staleCopy = await repository.GetByIdAsync(application.Id);

            Assert.NotNull(firstCopy);
            Assert.NotNull(staleCopy);
            firstCopy.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
            await repository.UpdateAsync(firstCopy);
            staleCopy.UpdateQualification(300_000m, 30_000m);

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(staleCopy));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task InMemoryRepositoryRejectsAnUpdateBasedOnAStaleSnapshot()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var application = new MortgageApplication();
        await repository.AddAsync(application);
        var firstCopy = await repository.GetByIdAsync(application.Id);
        var staleCopy = await repository.GetByIdAsync(application.Id);

        Assert.NotNull(firstCopy);
        Assert.NotNull(staleCopy);
        firstCopy.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
        await repository.UpdateAsync(firstCopy);
        staleCopy.UpdateQualification(300_000m, 30_000m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(staleCopy));
        var persisted = await repository.GetByIdAsync(application.Id);
        Assert.Equal("Jane", persisted!.FirstName);
        Assert.Null(persisted.PropertyValue);
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
