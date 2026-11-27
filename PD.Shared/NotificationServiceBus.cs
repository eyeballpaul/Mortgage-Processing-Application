using Azure.Identity;
using Azure.Messaging.ServiceBus;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PD.Mortgages.Events;

internal static class NotificationServiceBus
{
    public static ServiceBusClient Create(IConfiguration configuration, IHostEnvironment environment)
    {
        var queueName = configuration["Notifications:QueueName"];
        if (string.IsNullOrWhiteSpace(queueName))
            throw new InvalidOperationException("Notifications:QueueName is required.");

        if (configuration.GetValue<bool>("Notifications:UseLocalEmulator"))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("The Service Bus emulator is only permitted in Development.");

            var connectionString = configuration.GetConnectionString("notification-bus");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:notification-bus is required for the local emulator.");

            var properties = ServiceBusConnectionStringProperties.Parse(connectionString);
            var settings = new DbConnectionStringBuilder { ConnectionString = connectionString };
            if (!properties.Endpoint.IsLoopback ||
                !settings.TryGetValue("UseDevelopmentEmulator", out var emulator) ||
                !string.Equals(emulator.ToString(), "true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The local Service Bus emulator must use a loopback endpoint and UseDevelopmentEmulator=true.");

            return new ServiceBusClient(connectionString);
        }

        var serviceBusNamespace = configuration["Notifications:ServiceBusNamespace"];
        if (string.IsNullOrWhiteSpace(serviceBusNamespace))
            throw new InvalidOperationException("Notifications:ServiceBusNamespace is required.");

        return new ServiceBusClient(serviceBusNamespace, new DefaultAzureCredential());
    }
}
