using Azure.Messaging.ServiceBus;

namespace PD.Mortgages.Api.Services;

internal sealed class SubmissionNotificationWorker(
    IServiceScopeFactory scopes,
    ServiceBusClient client,
    IConfiguration configuration,
    ILogger<SubmissionNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor(configuration["Notifications:QueueName"]!, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1
        });
        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus notification processing failed at {ErrorSource}.", args.ErrorSource);
            return Task.CompletedTask;
        };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.StartProcessingAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not start the notification worker; it will be retried.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        await processor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        if (!SubmissionMessage.TryRead(args.Message.Body, args.Message.MessageId, out var message))
        {
            logger.LogWarning("Dead-lettering an invalid submission notification message.");
            await args.DeadLetterMessageAsync(args.Message, "InvalidSubmissionEvent", "Unsupported or invalid event.", args.CancellationToken);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<SimulatedNotificationStore>();
        bool inserted;
        try
        {
            inserted = await store.RecordAsync(message!, args.CancellationToken);
        }
        catch (InvalidDataException)
        {
            logger.LogError("Submission event {EventId} conflicts with an existing simulated notification.", message!.EventId);
            await args.DeadLetterMessageAsync(args.Message, "ConflictingSubmissionEvent", "Event ID already belongs to another application.", args.CancellationToken);
            return;
        }
        if (inserted)
        {
            logger.LogInformation("Simulated notification recorded for submission event {EventId}; no email was sent.", message!.EventId);
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }
}
