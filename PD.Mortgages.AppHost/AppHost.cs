using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("mortgages-sql")
    .WithDataVolume("mortgages-sql-data");
var mortgagesDatabase = sqlServer.AddDatabase("mortgages");
var customerSessionsDatabase = sqlServer.AddDatabase("customersessions", databaseName: "customer-sessions");
var productsDatabase = sqlServer.AddDatabase("products");
var documentStorageRootPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PD.Mortgages");
var developmentApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var eligibilityApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var apiClientId = builder.Configuration["MortgageApi:ApiClientId"] ?? Guid.Empty.ToString();
var tenantId = builder.Configuration["MortgageApi:TenantId"] ?? Guid.Empty.ToString();
var staffClientId = builder.Configuration["MortgageApi:StaffClientId"] ?? Guid.Empty.ToString();

var productsApi = builder.AddDotnetProject("products-api", "../PD.Products.Api/PD.Products.Api.csproj")
    .WithReference(productsDatabase)
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

builder.AddDotnetProject("mortgages-web", "../PD.Mortgages.Web/PD.Mortgages.Web.csproj")
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
