extern alias MortgageApi;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Types.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Repositories;

namespace PD.Mortgages.Tests;

public sealed class DraftLifecycleTests
{
    [Fact]
    public async Task MaintenanceExpiresInactiveDraftAndRecordsSystemAudit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pd-lifecycle-test-{Guid.NewGuid():N}");
        try
        {
            var options = new MortgagePersistenceOptions { RootPath = root };
            var audit = new FileMortgageApplicationAuditRepository(options);
            var repository = new FileMortgageApplicationRepository(options, audit);
            var draft = new MortgageApplication();
            await repository.AddAsync(draft, TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(Path.Combine(root, "applications", $"{draft.Id:N}.json"), DateTime.UtcNow.AddDays(-31));
            await using var services = new ServiceCollection()
                .AddSingleton<IMortgageDraftLifecycle>(repository)
                .AddSingleton<IMortgageDocumentStorage, PD.Mortgages.Infrastructure.Storage.InMemoryMortgageDocumentStorage>()
                .BuildServiceProvider();
            var maintenance = new MortgageApi::PD.Mortgages.Api.Services.DraftMaintenanceService(
                services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<MortgageApi::PD.Mortgages.Api.Services.DraftMaintenanceService>.Instance);

            await maintenance.ProcessAsync(CancellationToken.None);

            Assert.Null(await repository.GetByIdAsync(draft.Id, TestContext.Current.CancellationToken));
            var entry = Assert.Single((await audit.GetPageAsync(draft.Id, 1, 25, TestContext.Current.CancellationToken)).Events);
            Assert.Equal("Inactive draft expired", entry.Action);
            Assert.Equal("system:draft-retention", entry.ActorId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MaintenanceRetriesFailedCleanupAndRechecksDeletedDrafts()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var draft = new MortgageApplication();
        await repository.AddAsync(draft, TestContext.Current.CancellationToken);
        Assert.True(await repository.TryDeleteDraftAsync(draft.Id, draft.Version, new MortgageApplicationAuditEntry(DateTime.UtcNow, "session:test", "Deleted"), null, TestContext.Current.CancellationToken));
        var storage = new FailingOnceStorage();
        var services = new ServiceCollection()
            .AddSingleton<IMortgageDraftLifecycle>(repository)
            .AddSingleton<IMortgageDocumentStorage>(storage)
            .BuildServiceProvider();
        await using (services)
        {
            var maintenance = new MortgageApi::PD.Mortgages.Api.Services.DraftMaintenanceService(
                services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<MortgageApi::PD.Mortgages.Api.Services.DraftMaintenanceService>.Instance);
            await maintenance.ProcessAsync(CancellationToken.None);
            Assert.Contains(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100, TestContext.Current.CancellationToken));
            await maintenance.ProcessAsync(CancellationToken.None);
            Assert.Equal(2, storage.Attempts);
            Assert.DoesNotContain(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100, TestContext.Current.CancellationToken));
            await repository.MarkDocumentsCleanedAsync(draft.Id, DateTime.UtcNow.AddDays(-2), TestContext.Current.CancellationToken);
            await maintenance.ProcessAsync(CancellationToken.None);
            Assert.Equal(3, storage.Attempts);
        }
    }

    [Fact]
    public async Task DeletionRejectsStaleVersionsAndRetainsAuditAndCleanupWork()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var draft = new MortgageApplication();
        await repository.AddAsync(draft, TestContext.Current.CancellationToken);
        var changed = (await repository.GetByIdAsync(draft.Id, TestContext.Current.CancellationToken))!;
        changed.UpdateQualification(300_000, 30_000);
        await repository.UpdateAsync(changed, TestContext.Current.CancellationToken);
        var audit = new MortgageApplicationAuditEntry(DateTime.UtcNow, "session:test", "Draft deleted");

        Assert.False(await repository.TryDeleteDraftAsync(draft.Id, draft.Version, audit, null, TestContext.Current.CancellationToken));
        Assert.NotNull(await repository.GetByIdAsync(draft.Id, TestContext.Current.CancellationToken));
        Assert.True(await repository.TryDeleteDraftAsync(draft.Id, changed.Version, audit, null, TestContext.Current.CancellationToken));
        Assert.Null(await repository.GetByIdAsync(draft.Id, TestContext.Current.CancellationToken));
        Assert.Equal("Draft deleted", Assert.Single((await repository.AuditRepository.GetPageAsync(draft.Id, 1, 25, TestContext.Current.CancellationToken)).Events).Action);
        Assert.Contains(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100, TestContext.Current.CancellationToken));

        await repository.MarkDocumentsCleanedAsync(draft.Id, DateTime.UtcNow, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100, TestContext.Current.CancellationToken));
        Assert.Contains(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(1), 100, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExpirationRechecksLastActivityBeforeDeleting()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var draft = new MortgageApplication();
        await repository.AddAsync(draft, TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        var cutoff = DateTime.UtcNow;
        var candidate = Assert.Single(await repository.FindExpiredDraftsAsync(cutoff, 100, TestContext.Current.CancellationToken));

        Assert.True(await repository.TouchActivityAsync(draft.Id, TestContext.Current.CancellationToken));
        Assert.False(await repository.TryDeleteDraftAsync(candidate.Id, candidate.Version, new MortgageApplicationAuditEntry(DateTime.UtcNow, "system", "Expired"), cutoff, TestContext.Current.CancellationToken));
        Assert.NotNull(await repository.GetByIdAsync(draft.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubmittedApplicationsCannotBeExpiredOrDeletedAsDrafts()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var application = new MortgageApplication();
        application.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
        application.UpdateQualification(300_000, 30_000);
        application.UpdateFinances(100_000, 1_000, EmploymentType.Employed);
        application.RecordConsents(false, "terms-1", "session:test");
        TestEligibilityClient.Record(application);
        application.SelectProduct("fixed-2", new SelectedMortgageProductSnapshot("fixed-2", "Fixed", 4m, 999m, 5m, 2, 90, "original"));
        application.Submit();
        await repository.AddAsync(application, TestContext.Current.CancellationToken);

        Assert.Empty(await repository.FindExpiredDraftsAsync(DateTime.UtcNow.AddDays(1), 100, TestContext.Current.CancellationToken));
        Assert.False(await repository.TryDeleteDraftAsync(application.Id, application.Version, new MortgageApplicationAuditEntry(DateTime.UtcNow, "system", "Expired"), DateTime.UtcNow.AddDays(1), TestContext.Current.CancellationToken));
        Assert.NotNull(await repository.GetByIdAsync(application.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FileTombstoneSurvivesRepositoryRestartAndKeepsAudit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pd-lifecycle-test-{Guid.NewGuid():N}");
        try
        {
            var options = new MortgagePersistenceOptions { RootPath = root };
            var audit = new FileMortgageApplicationAuditRepository(options);
            var repository = new FileMortgageApplicationRepository(options, audit);
            var draft = new MortgageApplication();
            await repository.AddAsync(draft, TestContext.Current.CancellationToken);
            Assert.True(await repository.TryDeleteDraftAsync(draft.Id, draft.Version, new MortgageApplicationAuditEntry(DateTime.UtcNow, "session:test", "Draft deleted"), null, TestContext.Current.CancellationToken));

            var restarted = new FileMortgageApplicationRepository(options, audit);
            Assert.Null(await restarted.GetByIdAsync(draft.Id, TestContext.Current.CancellationToken));
            Assert.Contains(draft.Id, await restarted.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100, TestContext.Current.CancellationToken));
            Assert.Equal("Draft deleted", Assert.Single((await audit.GetPageAsync(draft.Id, 1, 25, TestContext.Current.CancellationToken)).Events).Action);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FailingOnceStorage : IMortgageDocumentStorage
    {
        public int Attempts { get; private set; }

        public Task SaveAsync(Guid applicationId, Guid documentId, byte[] contents, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> GetAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAllAsync(Guid applicationId, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts == 1) throw new IOException("Storage unavailable.");
            return Task.CompletedTask;
        }
    }
}
