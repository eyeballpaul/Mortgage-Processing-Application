extern alias MortgageApi;
extern alias NotificationWorker;
extern alias OutboxPublisher;

using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Azure.Messaging.ServiceBus;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Types.Enums;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Infrastructure.Storage;
using PD.Mortgages.DatabaseBootstrap;
using SubmissionMessage = MortgageApi::PD.Mortgages.Events.SubmissionMessage;
using SubmissionOutboxPublisher = OutboxPublisher::PD.Mortgages.OutboxPublisher.SubmissionOutboxPublisher;
using NotificationServiceBus = OutboxPublisher::PD.Mortgages.Events.NotificationServiceBus;
using NotificationsDbContext = NotificationWorker::PD.Notifications.Worker.NotificationsDbContext;
using WorkerMessage = NotificationWorker::PD.Mortgages.Events.SubmissionMessage;
using WorkerStore = NotificationWorker::PD.Notifications.Worker.SimulatedNotificationStore;
using Worker = NotificationWorker::PD.Notifications.Worker.SubmissionNotificationWorker;

namespace PD.Mortgages.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MORTGAGE_TEST_SQL_CONNECTION")))
            Skip = "Set MORTGAGE_TEST_SQL_CONNECTION to a disposable SQL Server to run SQL integration tests.";
    }
}

public sealed class ServiceBusEmulatorFactAttribute : FactAttribute
{
    public ServiceBusEmulatorFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MORTGAGE_TEST_SQL_CONNECTION")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MORTGAGE_TEST_SERVICEBUS_CONNECTION")))
            Skip = "Set disposable SQL and local Service Bus emulator connections to run this integration test.";
    }
}

public sealed class SubmissionNotificationTests
{
    [Fact]
    public async Task LocalEmulatorRequiresDevelopmentAndLoopback()
    {
        const string localConnection = "Endpoint=sb://localhost:5672;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=a2V5;UseDevelopmentEmulator=true;";
        static IConfiguration Settings(string connection) => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Notifications:UseLocalEmulator"] = "true",
                ["Notifications:QueueName"] = "test-submission-notifications",
                ["ConnectionStrings:notification-bus"] = connection
            }).Build();

        await using var client = NotificationServiceBus.Create(Settings(localConnection), new TestEnvironment("Development"));
        Assert.Equal("localhost", client.FullyQualifiedNamespace);
        Assert.Throws<InvalidOperationException>(() => NotificationServiceBus.Create(Settings(localConnection), new TestEnvironment("Production")));
        Assert.Throws<InvalidOperationException>(() => NotificationServiceBus.Create(
            Settings(localConnection.Replace("localhost", "example.com")), new TestEnvironment("Development")));
        Assert.Throws<InvalidOperationException>(() => NotificationServiceBus.Create(
            Settings(localConnection.Replace("UseDevelopmentEmulator=true;", "")), new TestEnvironment("Development")));
    }

    [Fact]
    public async Task AzureNotificationsUseTheConfiguredNamespace()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:ServiceBusNamespace"] = "example.servicebus.windows.net",
            ["Notifications:QueueName"] = "submission-notifications"
        }).Build();
        await using var client = NotificationServiceBus.Create(config, new TestEnvironment("Production"));
        Assert.Equal("example.servicebus.windows.net", client.FullyQualifiedNamespace);
    }

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

    [Fact]
    public async Task WorkerDeadLettersInvalidMessagesWithoutCompletingThem()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var worker = new Worker(services.GetRequiredService<IServiceScopeFactory>(),
            null!, new ConfigurationBuilder().Build(), NullLogger<Worker>.Instance);
        var args = new TestMessageArgs(BinaryData.FromString("{"), Guid.NewGuid().ToString("D"));

        await worker.ProcessMessageAsync(args);

        Assert.Equal("InvalidSubmissionEvent", args.DeadLetterReason);
        Assert.False(args.Completed);
    }

    [Fact]
    public async Task WorkerDoesNotCompleteWhenProcessingFails()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var worker = new Worker(services.GetRequiredService<IServiceScopeFactory>(),
            null!, new ConfigurationBuilder().Build(), NullLogger<Worker>.Instance);
        var message = new WorkerMessage(Guid.NewGuid(), Guid.NewGuid(), WorkerMessage.Type, 1, DateTime.UtcNow);
        var args = new TestMessageArgs(BinaryData.FromString(message.Serialize()), message.EventId.ToString("D"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ProcessMessageAsync(args));
        Assert.False(args.Completed);
        Assert.Null(args.DeadLetterReason);
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
    public async Task SeparateNotificationDatabaseDeduplicatesAndRejectsConflictingEvents()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("MORTGAGE_TEST_SQL_CONNECTION"))
        {
            InitialCatalog = $"notifications_worker_test_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<NotificationsDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var first = new NotificationsDbContext(options);
        try
        {
            await first.Database.MigrateAsync();
            await using var second = new NotificationsDbContext(options);
            var message = new WorkerMessage(Guid.NewGuid(), Guid.NewGuid(), WorkerMessage.Type, 1, DateTime.UtcNow);
            var results = await Task.WhenAll(
                new WorkerStore(first).RecordAsync(message, CancellationToken.None),
                new WorkerStore(second).RecordAsync(message, CancellationToken.None));

            Assert.Single(results, recorded => recorded);
            await using var verify = new NotificationsDbContext(options);
            Assert.Equal(message.ApplicationId, (await verify.SimulatedNotifications.SingleAsync()).ApplicationId);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new WorkerStore(verify).RecordAsync(message with { ApplicationId = Guid.NewGuid() }, CancellationToken.None));

            await using var services = new ServiceCollection()
                .AddDbContext<NotificationsDbContext>(db => db.UseSqlServer(builder.ConnectionString))
                .AddScoped<WorkerStore>()
                .BuildServiceProvider();
            var worker = new Worker(services.GetRequiredService<IServiceScopeFactory>(),
                null!, new ConfigurationBuilder().Build(), NullLogger<Worker>.Instance);
            var duplicate = new TestMessageArgs(BinaryData.FromString(message.Serialize()), message.EventId.ToString("D"));
            await worker.ProcessMessageAsync(duplicate);
            Assert.True(duplicate.Completed);
            Assert.Null(duplicate.DeadLetterReason);
            var conflicting = message with { ApplicationId = Guid.NewGuid() };
            var conflictArgs = new TestMessageArgs(BinaryData.FromString(conflicting.Serialize()), message.EventId.ToString("D"));
            await worker.ProcessMessageAsync(conflictArgs);
            Assert.Equal("ConflictingSubmissionEvent", conflictArgs.DeadLetterReason);
            Assert.False(conflictArgs.Completed);
        }
        finally
        {
            await first.Database.EnsureDeletedAsync();
        }
    }

    [SqlServerFact]
    public async Task HistoricalNotificationsAreCopiedIdempotentlyAndConflictsStopBootstrap()
    {
        await using var source = await TestDatabase.CreateAsync();
        var targetBuilder = new SqlConnectionStringBuilder(source.ConnectionString)
        {
            InitialCatalog = $"notifications_history_test_{Guid.NewGuid():N}"
        };
        var targetOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseSqlServer(targetBuilder.ConnectionString).Options;
        await using var target = new NotificationsDbContext(targetOptions);
        try
        {
            await target.Database.MigrateAsync();
            var eventId = Guid.NewGuid();
            var applicationId = Guid.NewGuid();
            var recordedAt = DateTime.UtcNow;
            await using (var old = source.Open())
            {
                old.SimulatedNotifications.Add(new SimulatedNotificationRecord
                {
                    EventId = eventId,
                    ApplicationId = applicationId,
                    RecordedAtUtc = recordedAt
                });
                await old.SaveChangesAsync();
            }

            async Task CopyAsync()
            {
                await using var from = new SqlConnection(source.ConnectionString);
                await using var to = new SqlConnection(targetBuilder.ConnectionString);
                await HistoricalNotificationCopy.CopyAsync(from, to);
            }

            await CopyAsync();
            await CopyAsync();
            var copied = await target.SimulatedNotifications.SingleAsync();
            Assert.Equal(eventId, copied.EventId);
            Assert.Equal(applicationId, copied.ApplicationId);
            Assert.Equal(recordedAt, copied.RecordedAtUtc);

            copied.ApplicationId = Guid.NewGuid();
            await target.SaveChangesAsync();
            await Assert.ThrowsAsync<SqlException>(CopyAsync);
        }
        finally
        {
            await target.Database.EnsureDeletedAsync();
        }
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
            NullLogger<SubmissionOutboxPublisher>.Instance);
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

    [ServiceBusEmulatorFact]
    public async Task PublisherAndWorkerProcessAnEventThroughTheLocalEmulator()
    {
        await using var database = await TestDatabase.CreateAsync();
        var targetBuilder = new SqlConnectionStringBuilder(database.ConnectionString)
        {
            InitialCatalog = $"notifications_emulator_test_{Guid.NewGuid():N}"
        };
        var targetOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseSqlServer(targetBuilder.ConnectionString).Options;
        await using var target = new NotificationsDbContext(targetOptions);
        try
        {
            await target.Database.MigrateAsync();
            var eventId = Guid.NewGuid();
            var applicationId = Guid.NewGuid();
            await using (var seed = database.Open())
            {
                seed.SubmissionOutbox.Add(new SubmissionOutboxRecord
                {
                    Id = eventId, ApplicationId = applicationId, OccurredAtUtc = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:UseLocalEmulator"] = "true",
                ["Notifications:QueueName"] = "test-submission-notifications",
                ["ConnectionStrings:notification-bus"] = Environment.GetEnvironmentVariable("MORTGAGE_TEST_SERVICEBUS_CONNECTION")
            }).Build();
            await using var client = NotificationServiceBus.Create(config, new TestEnvironment("Development"));
            await using var services = new ServiceCollection()
                .AddDbContext<MortgageDbContext>(options => options.UseSqlServer(database.ConnectionString))
                .AddDbContext<NotificationsDbContext>(options => options.UseSqlServer(targetBuilder.ConnectionString))
                .AddScoped<WorkerStore>()
                .BuildServiceProvider();
            var worker = new Worker(services.GetRequiredService<IServiceScopeFactory>(), client, config, NullLogger<Worker>.Instance);
            await worker.StartAsync(CancellationToken.None);
            try
            {
                var publisher = new SubmissionOutboxPublisher(services.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<SubmissionOutboxPublisher>.Instance);
                await using var sender = client.CreateSender("test-submission-notifications");
                await publisher.PublishBatchAsync(sender, CancellationToken.None);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                while (!await target.SimulatedNotifications.AsNoTracking().AnyAsync(row =>
                    row.EventId == eventId && row.ApplicationId == applicationId, timeout.Token))
                    await Task.Delay(500, timeout.Token);

                await using var check = database.Open();
                Assert.NotNull((await check.SubmissionOutbox.AsNoTracking().SingleAsync()).PublishedAtUtc);
            }
            finally
            {
                await worker.StopAsync(CancellationToken.None);
                worker.Dispose();
            }
        }
        finally
        {
            await target.Database.EnsureDeletedAsync();
        }
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

    private sealed class TestMessageArgs(BinaryData body, string messageId)
            : ProcessMessageEventArgs(ServiceBusModelFactory.ServiceBusReceivedMessage(body: body, messageId: messageId),
                new TestReceiver(), CancellationToken.None)
    {
        public bool Completed { get; private set; }
        public string? DeadLetterReason { get; private set; }

        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        {
            Completed = true;
            return Task.CompletedTask;
        }

        public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message, string deadLetterReason,
            string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default)
        {
            DeadLetterReason = deadLetterReason;
            return Task.CompletedTask;
        }
    }

    private sealed class TestReceiver : ServiceBusReceiver;

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
