extern alias MortgageApi;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Azure.Messaging.ServiceBus;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Infrastructure.Storage;
using SimulatedNotificationStore = MortgageApi::PD.Mortgages.Api.Services.SimulatedNotificationStore;
using SubmissionMessage = MortgageApi::PD.Mortgages.Api.Services.SubmissionMessage;
using SubmissionOutboxPublisher = MortgageApi::PD.Mortgages.Api.Services.SubmissionOutboxPublisher;

namespace PD.Mortgages.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MORTGAGE_TEST_SQL_CONNECTION")))
            Skip = "Set MORTGAGE_TEST_SQL_CONNECTION to a disposable SQL Server to run SQL integration tests.";
    }
}

public sealed class SubmissionNotificationTests
{
    [Fact]
    public void EventEnvelopeAcceptsOnlyMatchingSupportedMessages()
    {
        var message = new SubmissionMessage(Guid.NewGuid(), Guid.NewGuid(), SubmissionMessage.Type, 1, DateTime.UtcNow);
        var body = BinaryData.FromString(message.Serialize());
        Assert.True(SubmissionMessage.TryRead(body, message.EventId.ToString("D"), out var parsed));
        Assert.Equal(message, parsed);
        Assert.False(SubmissionMessage.TryRead(body, Guid.NewGuid().ToString("D"), out _));
        Assert.False(SubmissionMessage.TryRead(BinaryData.FromString("{"), message.EventId.ToString("D"), out _));
        Assert.False(SubmissionMessage.TryRead(BinaryData.FromString((message with { Version = 2 }).Serialize()), message.EventId.ToString("D"), out _));
        Assert.False(SubmissionMessage.TryRead(BinaryData.FromString(
            (message with { OccurredAtUtc = DateTime.SpecifyKind(message.OccurredAtUtc, DateTimeKind.Unspecified) }).Serialize()),
            message.EventId.ToString("D"), out _));
    }

    [SqlServerFact]
    public async Task CustomerAndStaffSubmissionPathsCreateOneOutboxEventEach()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Open();
        var repository = new SqlMortgageApplicationRepository(db);
        foreach (var isStaff in new[] { false, true })
        {
            var application = isStaff ? MortgageApplication.CreateForEmployee("staff") : new MortgageApplication();
            if (!isStaff) application.AssignOwner("customer");
            application.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
            application.UpdateQualification(300_000, 30_000);
            application.UpdateFinances(100_000, 1_000, EmploymentType.Employed);
            application.RecordConsents(false, "terms", "customer");
            TestEligibilityClient.Record(application);
            application.SelectProduct("fixed-2", new SelectedMortgageProductSnapshot("fixed-2", "Fixed", 4, 999, 5, 2, 90, "1"));
            await repository.AddAsync(application);
            var service = new MortgageApplicationService(repository, new SqlMortgageApplicationAuditRepository(db),
                new MockProductCatalogClient(new LocalProductCatalogFallback()), new TestEligibilityClient(),
                new InMemoryMortgageDocumentStorage(), new TestActor(isStaff), Options.Create(new MortgageWorkflowOptions()),
                new DocumentUploadValidator(), repository, NullLogger<MortgageApplicationService>.Instance);

            if (isStaff)
                await service.SubmitAsync(application.Id, application.Version);
            else
            {
                var key = Guid.NewGuid();
                await service.SubmitCustomerAsync(application.Id, application.Version, key);
                await service.SubmitCustomerAsync(application.Id, application.Version, key);
            }
            Assert.Equal(1, await db.SubmissionOutbox.CountAsync(entry => entry.ApplicationId == application.Id));
            Assert.Equal(1, await db.AuditEntries.CountAsync(entry => entry.ApplicationId == application.Id && entry.Action == "Application submitted"));
        }
    }

    [SqlServerFact]
    public async Task SubmissionAuditAndOutboxCommitTogetherAndRejectStaleSubmissions()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Open();
        var repository = new SqlMortgageApplicationRepository(db);
        var application = new MortgageApplication();
        await repository.AddAsync(application);
        var submitted = Submitted(application);
        var audit = new MortgageApplicationAuditEntry(DateTime.UtcNow, "customer", "Application submitted");

        await repository.UpdateAsync(submitted, audit);

        await using var verify = database.Open();
        Assert.Equal(ApplicationStatus.Submitted, (await verify.Applications.SingleAsync()).Status);
        Assert.Equal(application.Id, (await verify.SubmissionOutbox.SingleAsync()).ApplicationId);
        Assert.Equal("Application submitted", (await verify.AuditEntries.SingleAsync()).Action);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlMortgageApplicationRepository(verify).UpdateAsync(submitted, audit));
        Assert.Equal(1, await verify.SubmissionOutbox.CountAsync());
    }

    [SqlServerFact]
    public async Task SubmissionFailureRollsBackApplicationAuditAndOutbox()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Open();
        var repository = new SqlMortgageApplicationRepository(db);
        var application = new MortgageApplication();
        await repository.AddAsync(application);
        var badAudit = new MortgageApplicationAuditEntry(DateTime.UtcNow, new string('x', 257), "Application submitted");

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.UpdateAsync(Submitted(application), badAudit));

        await using var verify = database.Open();
        Assert.Equal(ApplicationStatus.InProgress, (await verify.Applications.SingleAsync()).Status);
        Assert.Empty(await verify.SubmissionOutbox.ToListAsync());
        Assert.Empty(await verify.AuditEntries.ToListAsync());
    }

    [SqlServerFact]
    public async Task ConcurrentDuplicateDeliveriesRecordOnlyOneSimulatedNotification()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var first = database.Open();
        await using var second = database.Open();
        var message = new SubmissionMessage(Guid.NewGuid(), Guid.NewGuid(), SubmissionMessage.Type, 1, DateTime.UtcNow);
        var results = await Task.WhenAll(
            new SimulatedNotificationStore(first).RecordAsync(message, CancellationToken.None),
            new SimulatedNotificationStore(second).RecordAsync(message, CancellationToken.None));

        Assert.Single(results, recorded => recorded);
        await using var verify = database.Open();
        Assert.Equal(message.ApplicationId, (await verify.SimulatedNotifications.SingleAsync()).ApplicationId);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SimulatedNotificationStore(verify).RecordAsync(message with { ApplicationId = Guid.NewGuid() }, CancellationToken.None));
    }

    [SqlServerFact]
    public async Task PublisherClaimsOnceAndRetriesAfterAnExpiredLease()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var seed = database.Open();
        var eventId = Guid.NewGuid();
        seed.SubmissionOutbox.Add(new SubmissionOutboxRecord
        {
            Id = eventId,
            ApplicationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        await using var services = new ServiceCollection()
            .AddDbContext<MortgageDbContext>(options => options.UseSqlServer(database.ConnectionString))
            .BuildServiceProvider();
        var publisher = new SubmissionOutboxPublisher(services.GetRequiredService<IServiceScopeFactory>(),
            null!, new ConfigurationBuilder().Build(), NullLogger<SubmissionOutboxPublisher>.Instance);
        var failed = new TestSender { Fail = true };
        await Assert.ThrowsAsync<IOException>(() => publisher.PublishBatchAsync(failed, CancellationToken.None));

        await using var check = database.Open();
        var pending = await check.SubmissionOutbox.AsNoTracking().SingleAsync();
        Assert.Null(pending.PublishedAtUtc);
        Assert.NotNull(pending.LeaseId);
        await check.SubmissionOutbox.ExecuteUpdateAsync(update =>
            update.SetProperty(entry => entry.LeaseUntilUtc, DateTime.UtcNow.AddSeconds(-1)));
        var sender = new TestSender();
        await Task.WhenAll(publisher.PublishBatchAsync(sender, CancellationToken.None),
            publisher.PublishBatchAsync(sender, CancellationToken.None));

        Assert.Equal(1, sender.Count);
        Assert.Equal(eventId.ToString("D"), sender.LastMessageId);
        Assert.True(SubmissionMessage.TryRead(sender.LastBody!, sender.LastMessageId!, out _));
        Assert.NotNull((await check.SubmissionOutbox.AsNoTracking().SingleAsync()).PublishedAtUtc);
    }

    private static MortgageApplication Submitted(MortgageApplication application) =>
        (MortgageApplicationSnapshot.FromDomain(application) with
        {
            Status = ApplicationStatus.Submitted,
            Version = application.Version + 1,
            UpdatedAtUtc = DateTime.UtcNow
        }).ToDomain();

    private sealed class TestSender : ServiceBusSender
    {
        private int count;
        public bool Fail { get; init; }
        public int Count => count;
        public string? LastMessageId { get; private set; }
        public BinaryData? LastBody { get; private set; }

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Simulated send failure.");
            Interlocked.Increment(ref count);
            LastMessageId = message.MessageId;
            LastBody = message.Body;
            return Task.CompletedTask;
        }
    }

    private sealed class TestActor(bool isStaff) : IApplicationActor
    {
        public string SubjectId => isStaff ? "staff" : "customer";
        public bool IsAuthenticated => isStaff;
        public bool IsStaff => isStaff;
    }

    private sealed class TestDatabase(DbContextOptions<MortgageDbContext> options, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString => connectionString;

        public static async Task<TestDatabase> CreateAsync()
        {
            var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("MORTGAGE_TEST_SQL_CONNECTION"))
            {
                InitialCatalog = $"mortgage_notification_test_{Guid.NewGuid():N}"
            };
            var options = new DbContextOptionsBuilder<MortgageDbContext>().UseSqlServer(builder.ConnectionString).Options;
            var database = new TestDatabase(options, builder.ConnectionString);
            await using var context = database.Open();
            await context.Database.MigrateAsync();
            return database;
        }

        public MortgageDbContext Open() => new(options);

        public async ValueTask DisposeAsync()
        {
            await using var context = Open();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
