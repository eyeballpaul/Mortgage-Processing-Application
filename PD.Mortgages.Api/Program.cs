using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using PD.Mortgages.Api.Middleware;
using PD.Mortgages.Api.Health;
using PD.Mortgages.Api.Security;
using PD.Mortgages.Api.Services;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Infrastructure.Eligibility;
using PD.Mortgages.Events;
using PD.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptionalTracing(builder.Environment, "mortgage-api");

var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<MortgageApiExceptionHandler>();
var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "MortgageAuthentication";
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});
authentication.AddPolicyScheme("MortgageAuthentication", null, options =>
    options.ForwardDefaultSelector = context =>
        isDevelopment && context.Request.Headers.ContainsKey(DevelopmentCustomerAuthenticationHandler.HeaderName)
            ? DevelopmentCustomerAuthenticationHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme);
authentication.AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
if (isDevelopment)
{
    authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentCustomerAuthenticationHandler>(
        DevelopmentCustomerAuthenticationHandler.SchemeName, _ => { });
}
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
    options.MapInboundClaims = false);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MortgageCaller", policy => policy.RequireAuthenticatedUser()
        .RequireAssertion(context => MortgageCallerAuthorization.IsAllowed(context.User, builder.Configuration)));
    options.AddPolicy("MortgageStaff", policy => policy.RequireAuthenticatedUser()
        .RequireAssertion(context => MortgageCallerAuthorization.IsStaff(context.User, builder.Configuration)));
});
builder.Services.AddHealthChecks()
    .AddCheck<ApplicationHealthCheck>("mortgage-storage", tags: ["ready"])
    .AddCheck<ProductDependencyHealthCheck>("products-api", tags: ["dependencies"])
    .AddCheck<EligibilityDependencyHealthCheck>("eligibility-api", tags: ["dependencies"]);
builder.Services.AddMortgagePersistence(builder.Configuration);
builder.Services.AddHostedService<DraftMaintenanceService>();
if (builder.Configuration.GetValue<bool>("Notifications:Enabled"))
{
    if (!string.Equals(builder.Configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Notifications require SQL Server.");
    }
    builder.Services.AddSingleton(NotificationServiceBus.Create(builder.Configuration, builder.Environment));
    builder.Services.AddHostedService<SubmissionOutboxPublisher>();
}
builder.Services.AddOptions<MortgageWorkflowOptions>().BindConfiguration(MortgageWorkflowOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConsentTermsVersion), "MortgageWorkflow:ConsentTermsVersion is required.")
    .ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IApplicationActor, MortgageApiActor>();
builder.Services.AddScoped<IMortgageApplicationService, MortgageApplicationService>();
builder.Services.AddSingleton<IDocumentUploadValidator, DocumentUploadValidator>();
builder.Services.AddTransient<EligibilityResilienceHandler>();
builder.Services.AddHttpClient<IEligibilityClient, EligibilityClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["EligibilityService:BaseUrl"]
        ?? throw new InvalidOperationException("EligibilityService:BaseUrl is required."));
    client.Timeout = Timeout.InfiniteTimeSpan;
    client.DefaultRequestHeaders.Add("X-Eligibility-Service-Key", builder.Configuration["EligibilityService:ApiKey"]
        ?? throw new InvalidOperationException("EligibilityService:ApiKey is required."));
}).AddHttpMessageHandler<EligibilityResilienceHandler>();

if (builder.Configuration.GetValue<bool>("ProductService:UseMock"))
{
    builder.Services.AddSingleton<LocalProductCatalogFallback>();
    builder.Services.AddSingleton<IProductCatalogClient, MockProductCatalogClient>();
}
else
{
    builder.Services.AddTransient<ProductCorrelationHandler>();
    builder.Services.AddTransient<ProductServiceResilienceHandler>();
    builder.Services.AddHttpClient<IProductCatalogClient, ProductCatalogClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["ProductService:BaseUrl"]
            ?? throw new InvalidOperationException("ProductService:BaseUrl is required."));
        client.Timeout = Timeout.InfiniteTimeSpan;
        var apiKey = builder.Configuration["ProductService:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Product-Service-Key", apiKey);
        }
    }).AddHttpMessageHandler<ProductCorrelationHandler>()
      .AddHttpMessageHandler<ProductServiceResilienceHandler>();
}

var app = builder.Build();
var developmentKey = builder.Configuration["MortgageApi:DevelopmentKey"];
var useDevelopmentCustomer = isDevelopment && !string.IsNullOrWhiteSpace(developmentKey);
if (useDevelopmentCustomer && developmentKey!.Length < 32)
{
    throw new InvalidOperationException("The development customer credential must contain at least 32 characters.");
}
foreach (var name in new[] { "AzureAd:TenantId", "AzureAd:ClientId", "MortgageApi:CustomerClientId", "MortgageApi:StaffClientId" })
{
    if (!Guid.TryParse(builder.Configuration[name], out var value) || (value == Guid.Empty && !useDevelopmentCustomer))
    {
        throw new InvalidOperationException($"A valid {name} GUID is required for Mortgage API authentication.");
    }
}
if (!isDevelopment &&
    (!string.Equals(builder.Configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase) ||
     !string.Equals(builder.Configuration["DocumentStorage:Provider"], "AzureBlob", StringComparison.OrdinalIgnoreCase) ||
     builder.Configuration.GetValue<bool>("ProductService:UseMock") ||
     string.IsNullOrWhiteSpace(builder.Configuration["ProductService:ApiKey"]) ||
     string.IsNullOrWhiteSpace(builder.Configuration["EligibilityService:ApiKey"])))
{
    throw new InvalidOperationException("The Mortgage API requires SQL Server, Azure Blob document storage and the real Products API outside Development.");
}
await app.Services.MigrateMortgageDatabaseAsync();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapHealthChecks("/health/dependencies", new HealthCheckOptions { Predicate = check => check.Tags.Contains("dependencies") }).AllowAnonymous();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
app.Run();

public partial class Program { }
