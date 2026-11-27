using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Api.Services;

internal sealed class SubmissionOutboxPublisher(
    IServiceScopeFactory scopes,
    ServiceBusClient client,
    IConfiguration configuration,
    ILogger<SubmissionOutboxPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var sender = client.CreateSender(configuration["Notifications:QueueName"]!);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishBatchAsync(sender, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Submission outbox publishing failed; pending events will be retried.");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal async Task PublishBatchAsync(ServiceBusSender sender, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MortgageDbContext>();
        var now = DateTime.UtcNow;
        var candidates = await db.SubmissionOutbox.AsNoTracking()
            .Where(item => item.PublishedAtUtc == null && (item.LeaseUntilUtc == null || item.LeaseUntilUtc < now))
            .OrderBy(item => item.OccurredAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var item in candidates)
        {
            now = DateTime.UtcNow;
            var leaseId = Guid.NewGuid();
            var claimed = await db.SubmissionOutbox
                .Where(entry => entry.Id == item.Id && entry.PublishedAtUtc == null &&
                    (entry.LeaseUntilUtc == null || entry.LeaseUntilUtc < now))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(entry => entry.LeaseId, leaseId)
                    .SetProperty(entry => entry.LeaseUntilUtc, now + LeaseDuration), cancellationToken);
            if (claimed == 0) continue;

            var payload = new SubmissionMessage(item.Id, item.ApplicationId, SubmissionMessage.Type, 1,
                DateTime.SpecifyKind(item.OccurredAtUtc, DateTimeKind.Utc));
            await sender.SendMessageAsync(new ServiceBusMessage(payload.Serialize())
            {
                MessageId = item.Id.ToString("D"),
                ContentType = "application/json",
                Subject = SubmissionMessage.Type
            }, cancellationToken);

            var publishedAt = DateTime.UtcNow;
            var marked = await db.SubmissionOutbox.Where(entry => entry.Id == item.Id && entry.LeaseId == leaseId && entry.PublishedAtUtc == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(entry => entry.PublishedAtUtc, publishedAt)
                    .SetProperty(entry => entry.LeaseId, (Guid?)null)
                    .SetProperty(entry => entry.LeaseUntilUtc, (DateTime?)null), cancellationToken);
            if (marked == 0)
                logger.LogWarning("Outbox lease expired for event {EventId}; duplicate delivery is possible.", item.Id);
        }
    }
}
