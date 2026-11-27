using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PD.Notifications.Worker;

var builder = Host.CreateApplicationBuilder(args);
if (builder.Configuration.GetValue<bool>("Notifications:Enabled"))
{
    var connectionString = builder.Configuration.GetConnectionString("notifications")
        ?? throw new InvalidOperationException("ConnectionStrings:notifications is required.");
    var serviceBusNamespace = builder.Configuration["Notifications:ServiceBusNamespace"];
    var queueName = builder.Configuration["Notifications:QueueName"];
    if (string.IsNullOrWhiteSpace(serviceBusNamespace) || string.IsNullOrWhiteSpace(queueName))
        throw new InvalidOperationException("Notifications:ServiceBusNamespace and Notifications:QueueName are required.");

    builder.Services.AddDbContext<NotificationsDbContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddScoped<SimulatedNotificationStore>();
    builder.Services.AddSingleton(new ServiceBusClient(serviceBusNamespace, new DefaultAzureCredential()));
    builder.Services.AddHostedService<SubmissionNotificationWorker>();
}

using var host = builder.Build();
if (!builder.Configuration.GetValue<bool>("Notifications:Enabled"))
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Notifications")
        .LogInformation("Notifications are disabled.");
await host.RunAsync();
