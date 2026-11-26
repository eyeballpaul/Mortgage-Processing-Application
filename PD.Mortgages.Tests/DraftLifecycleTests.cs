extern alias MortgageApi;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
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
            await repository.AddAsync(draft);
            File.SetLastWriteTimeUtc(Path.Combine(root, "applications", $"{draft.Id:N}.json"), DateTime.UtcNow.AddDays(-31));
            await using var services = new ServiceCollection()
                .AddSingleton<IMortgageDraftLifecycle>(repository)
                .AddSingleton<IMortgageDocumentStorage, PD.Mortgages.Infrastructure.Storage.InMemoryMortgageDocumentStorage>()
                .BuildServiceProvider();
            var maintenance = new MortgageApi::PD.Mortgages.Api.Services.DraftMaintenanceService(
                services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<MortgageApi::PD.Mortgages.Api.Services.DraftMaintenanceService>.Instance);

            await maintenance.ProcessAsync(CancellationToken.None);

            Assert.Null(await repository.GetByIdAsync(draft.Id));
            var entry = Assert.Single((await audit.GetPageAsync(draft.Id, 1, 25)).Events);
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
        await repository.AddAsync(draft);
        Assert.True(await repository.TryDeleteDraftAsync(draft.Id, draft.Version,
            new MortgageApplicationAuditEntry(DateTime.UtcNow, "session:test", "Deleted"), null));
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
            Assert.Contains(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100));
            await maintenance.ProcessAsync(CancellationToken.None);
            Assert.Equal(2, storage.Attempts);
            Assert.DoesNotContain(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100));
            await repository.MarkDocumentsCleanedAsync(draft.Id, DateTime.UtcNow.AddDays(-2));
            await maintenance.ProcessAsync(CancellationToken.None);
            Assert.Equal(3, storage.Attempts);
        }
    }

    [Fact]
    public async Task DeletionRejectsStaleVersionsAndRetainsAuditAndCleanupWork()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var draft = new MortgageApplication();
        await repository.AddAsync(draft);
        var changed = (await repository.GetByIdAsync(draft.Id))!;
        changed.UpdateQualification(300_000, 30_000);
        await repository.UpdateAsync(changed);
        var audit = new MortgageApplicationAuditEntry(DateTime.UtcNow, "session:test", "Draft deleted");

        Assert.False(await repository.TryDeleteDraftAsync(draft.Id, draft.Version, audit, null));
        Assert.NotNull(await repository.GetByIdAsync(draft.Id));
        Assert.True(await repository.TryDeleteDraftAsync(draft.Id, changed.Version, audit, null));
        Assert.Null(await repository.GetByIdAsync(draft.Id));
        Assert.Equal("Draft deleted", Assert.Single((await repository.AuditRepository.GetPageAsync(draft.Id, 1, 25)).Events).Action);
        Assert.Contains(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100));

        await repository.MarkDocumentsCleanedAsync(draft.Id, DateTime.UtcNow);
        Assert.DoesNotContain(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100));
        Assert.Contains(draft.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(1), 100));
    }

    [Fact]
    public async Task ExpirationRechecksLastActivityBeforeDeleting()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var draft = new MortgageApplication();
        await repository.AddAsync(draft);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        var cutoff = DateTime.UtcNow;
        var candidate = Assert.Single(await repository.FindExpiredDraftsAsync(cutoff, 100));

        Assert.True(await repository.TouchActivityAsync(draft.Id));
        Assert.False(await repository.TryDeleteDraftAsync(candidate.Id, candidate.Version,
            new MortgageApplicationAuditEntry(DateTime.UtcNow, "system", "Expired"),
            cutoff));
        Assert.NotNull(await repository.GetByIdAsync(draft.Id));
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
        await repository.AddAsync(application);

        Assert.Empty(await repository.FindExpiredDraftsAsync(DateTime.UtcNow.AddDays(1), 100));
        Assert.False(await repository.TryDeleteDraftAsync(application.Id, application.Version,
            new MortgageApplicationAuditEntry(DateTime.UtcNow, "system", "Expired"),
            DateTime.UtcNow.AddDays(1)));
        Assert.NotNull(await repository.GetByIdAsync(application.Id));
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
            await repository.AddAsync(draft);
            Assert.True(await repository.TryDeleteDraftAsync(draft.Id, draft.Version,
                new MortgageApplicationAuditEntry(DateTime.UtcNow, "session:test", "Draft deleted"), null));

            var restarted = new FileMortgageApplicationRepository(options, audit);
            Assert.Null(await restarted.GetByIdAsync(draft.Id));
            Assert.Contains(draft.Id, await restarted.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100));
            Assert.Equal("Draft deleted", Assert.Single((await audit.GetPageAsync(draft.Id, 1, 25)).Events).Action);
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
