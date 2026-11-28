using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PD.Mortgages.Events;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.OutboxPublisher;

internal sealed class SubmissionOutboxPublisher(
    IServiceScopeFactory scopes,
    ILogger<SubmissionOutboxPublisher> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public async Task<int> PublishBatchAsync(ServiceBusSender sender, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MortgageDbContext>();
        var now = DateTime.UtcNow;
        var candidates = await db.SubmissionOutbox.AsNoTracking()
            .Where(item => item.PublishedAtUtc == null && (item.LeaseUntilUtc == null || item.LeaseUntilUtc < now))
            .OrderBy(item => item.OccurredAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var publishedCount = 0;
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
            else
                publishedCount++;
        }

        return publishedCount;
    }
}
