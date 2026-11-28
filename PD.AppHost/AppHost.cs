using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("mortgages-sql")
    .WithDataVolume("mortgages-sql-data");
var mortgagesDatabase = sqlServer.AddDatabase("mortgages");
var customerSessionsDatabase = sqlServer.AddDatabase("customersessions", databaseName: "customer-sessions");
var productsDatabase = sqlServer.AddDatabase("products");
var notificationsDatabase = sqlServer.AddDatabase("notifications");
var documentStorageRootPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PD.Mortgages");
var developmentApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var productApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var eligibilityApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var apiClientId = builder.Configuration["MortgageApi:ApiClientId"] ?? Guid.Empty.ToString();
var tenantId = builder.Configuration["MortgageApi:TenantId"] ?? Guid.Empty.ToString();
var staffClientId = builder.Configuration["MortgageApi:StaffClientId"] ?? Guid.Empty.ToString();

var notificationsWorker = builder.AddDotnetProject("notifications-worker", "../PD.Notifications.Worker/PD.Notifications.Worker.csproj")
    .WithReference(notificationsDatabase)
    .WaitFor(notificationsDatabase);
var outboxPublisher = builder.AddDotnetProject("outbox-publisher", "../PD.Mortgages.OutboxPublisher/PD.Mortgages.OutboxPublisher.csproj")
    .WithReference(mortgagesDatabase)
    .WaitFor(mortgagesDatabase);

var productsApi = builder.AddDotnetProject("products-api", "../PD.Products.Api/PD.Products.Api.csproj")
    .WithReference(productsDatabase)
    .WithEnvironment("ProductService__ApiKey", productApiKey)
    .WithEnvironment("Persistence__Provider", "SqlServer")
    .WaitFor(productsDatabase)
    .WithHttpHealthCheck("/health/ready");

var eligibilityApi = builder.AddDotnetProject("eligibility-api", "../PD.Eligibility.Api/PD.Eligibility.Api.csproj")
    .WithEnvironment("EligibilityService__ApiKey", eligibilityApiKey)
    .WithHttpHealthCheck("/health/ready");

var mortgageApi = builder.AddDotnetProject("mortgages-api", "../PD.Mortgages.Api/PD.Mortgages.Api.csproj")
    .WithHttpHealthCheck("/health/ready")
    .WithReference(mortgagesDatabase)
    .WithEnvironment("ProductService__UseMock", "false")
    .WithEnvironment("ProductService__ApiKey", productApiKey)
    .WithEnvironment("ProductService__BaseUrl", productsApi.GetEndpoint("https"))
    .WithEnvironment("EligibilityService__BaseUrl", eligibilityApi.GetEndpoint("http"))
    .WithEnvironment("EligibilityService__ApiKey", eligibilityApiKey)
    .WithEnvironment("Persistence__Provider", "SqlServer")
    .WithEnvironment("Persistence__RootPath", documentStorageRootPath)
    .WithEnvironment("MortgageApi__DevelopmentKey", developmentApiKey)
    .WithEnvironment("MortgageApi__StaffClientId", staffClientId)
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", apiClientId)
    .WaitFor(mortgagesDatabase)
    .WaitFor(productsApi)
    .WaitFor(eligibilityApi);

var useNotificationEmulator = builder.Configuration.GetValue<bool?>("LocalNotifications:UseEmulator")
    ?? builder.Environment.IsDevelopment();
if (useNotificationEmulator)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("The local Service Bus emulator is only available in Development.");
    if (!builder.Configuration.GetValue<bool>("LocalNotifications:AcceptEmulatorLicense"))
        throw new InvalidOperationException("Review the Service Bus emulator and SQL Server licenses, then set LocalNotifications:AcceptEmulatorLicense to true, or set LocalNotifications:UseEmulator to false.");

    Console.WriteLine("Local notifications are enabled; pending local outbox events will be processed by the emulator.");
    var serviceBus = builder.AddAzureServiceBus("notification-bus").RunAsEmulator();
    serviceBus.AddServiceBusQueue("submission-notifications");

    notificationsWorker
        .WithReference(serviceBus)
        .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
        .WithEnvironment("Notifications__Enabled", "true")
        .WithEnvironment("Notifications__UseLocalEmulator", "true")
        .WithEnvironment("Notifications__QueueName", "submission-notifications")
        .WaitFor(serviceBus);
    outboxPublisher
        .WithReference(serviceBus)
        .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
        .WithEnvironment("Notifications__Enabled", "true")
        .WithEnvironment("Notifications__UseLocalEmulator", "true")
        .WithEnvironment("Notifications__QueueName", "submission-notifications")
        .WaitFor(serviceBus);
}
else
{
    notificationsWorker.WithEnvironment("Notifications__Enabled", "false");
    outboxPublisher.WithEnvironment("Notifications__Enabled", "false");
}

builder.AddDotnetProject("mortgages-web", "../PD.Mortgages.Customer.Web/PD.Mortgages.Customer.Web.csproj")
    .WithHttpHealthCheck("/health/ready")
    .WithReference(customerSessionsDatabase)
    .WithEnvironment("Session__Provider", "SqlServer")
    .WithEnvironment("Session__RunMigrationsOnStartup", "true")
    .WithEnvironment("MortgageApi__BaseUrl", mortgageApi.GetEndpoint("https"))
    .WithEnvironment("MortgageApi__DevelopmentKey", developmentApiKey)
    .WithEnvironment("MortgageApi__Scope", $"api://{apiClientId}/.default")
    .WaitFor(customerSessionsDatabase)
    .WaitFor(mortgageApi);

builder.AddDotnetProject("mortgages-staff-web", "../PD.Mortgages.Staff.Web/PD.Mortgages.Staff.Web.csproj")
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("MortgageApi__BaseUrl", mortgageApi.GetEndpoint("https"))
    .WithEnvironment("MortgageApi__Scope", $"api://{apiClientId}/Mortgage.Staff")
    .WaitFor(mortgageApi);

builder.Build().Run();
