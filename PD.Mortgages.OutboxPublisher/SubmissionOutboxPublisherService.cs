using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PD.Mortgages.OutboxPublisher;

internal sealed class SubmissionOutboxPublisherService(
    SubmissionOutboxPublisher publisher,
    ServiceBusClient client,
    IConfiguration configuration,
    ILogger<SubmissionOutboxPublisherService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var sender = client.CreateSender(configuration["Notifications:QueueName"]!);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publishedCount = await publisher.PublishBatchAsync(sender, stoppingToken);
                if (publishedCount > 0)
                    logger.LogInformation("Published {Count} submission outbox events.", publishedCount);
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
}
