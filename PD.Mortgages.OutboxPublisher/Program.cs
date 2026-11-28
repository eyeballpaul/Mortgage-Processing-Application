using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PD.Mortgages.Events;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.OutboxPublisher;

var builder = Host.CreateApplicationBuilder(args);
var notificationsEnabled = builder.Configuration.GetValue<bool>("Notifications:Enabled");
var runOnce = builder.Configuration.GetValue<bool>("OutboxPublisher:RunOnce");

if (notificationsEnabled)
{
    var connectionString = builder.Configuration.GetConnectionString("mortgages")
        ?? throw new InvalidOperationException("ConnectionStrings:mortgages is required.");
    builder.Services.AddDbContext<MortgageDbContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddSingleton(NotificationServiceBus.Create(builder.Configuration, builder.Environment));
    builder.Services.AddSingleton<SubmissionOutboxPublisher>();
    if (!runOnce)
        builder.Services.AddHostedService<SubmissionOutboxPublisherService>();
}

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OutboxPublisher");

if (!notificationsEnabled)
{
    logger.LogInformation("Outbox publishing is disabled.");
    if (runOnce)
        return 0;
}
else if (runOnce)
{
    try
    {
        var client = host.Services.GetRequiredService<ServiceBusClient>();
        await using var sender = client.CreateSender(builder.Configuration["Notifications:QueueName"]!);
        var publisher = host.Services.GetRequiredService<SubmissionOutboxPublisher>();
        var publishedCount = await publisher.PublishBatchAsync(sender, CancellationToken.None);
        logger.LogInformation("Published {Count} submission outbox events.", publishedCount);
        return 0;
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "One-shot outbox publishing failed.");
        return 1;
    }
}

await host.RunAsync();
return 0;
