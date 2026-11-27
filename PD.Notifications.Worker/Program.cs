using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PD.Notifications.Worker;
using PD.Mortgages.Events;

var builder = Host.CreateApplicationBuilder(args);
if (builder.Configuration.GetValue<bool>("Notifications:Enabled"))
{
    var connectionString = builder.Configuration.GetConnectionString("notifications")
        ?? throw new InvalidOperationException("ConnectionStrings:notifications is required.");
    builder.Services.AddDbContext<NotificationsDbContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddScoped<SimulatedNotificationStore>();
    builder.Services.AddSingleton(NotificationServiceBus.Create(builder.Configuration, builder.Environment));
    builder.Services.AddHostedService<SubmissionNotificationWorker>();
}

using var host = builder.Build();
if (builder.Configuration.GetValue<bool>("Notifications:UseLocalEmulator") &&
    builder.Configuration.GetValue<bool>("Notifications:Enabled"))
{
    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}
if (!builder.Configuration.GetValue<bool>("Notifications:Enabled"))
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Notifications")
        .LogInformation("Notifications are disabled.");
await host.RunAsync();
