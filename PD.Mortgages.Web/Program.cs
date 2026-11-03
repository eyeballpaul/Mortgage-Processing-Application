using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Web.Health;
using PD.Mortgages.Web.Middleware;
using PD.Mortgages.Web.Services;
using PD.Mortgages.Web.Security;
using PD.Mortgages.Web.Configuration;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services.AddOptions<ApplicationSecurityOptions>()
    .BindConfiguration(ApplicationSecurityOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ApplicationSecurityOptions>, ProductionConfigurationValidator>();
builder.Services.AddScoped<IApplicationActor, HttpContextApplicationActor>();
builder.Services.AddHealthChecks()
    .AddCheck<ApplicationHealthCheck>("application", tags: ["ready"]);
var sessionProvider = builder.Configuration["Session:Provider"] ?? "Memory";
if (string.Equals(sessionProvider, "Redis", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required when Session:Provider is Redis.");
        options.InstanceName = "pd-mortgages:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(30);
    options.Cookie.Name = ".PDMortgages.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = TimeSpan.FromDays(30);
    options.Cookie.SameSite = SameSiteMode.Lax;
});

var persistenceOptions = builder.Configuration.GetSection(MortgagePersistenceOptions.SectionName)
    .Get<MortgagePersistenceOptions>() ?? new MortgagePersistenceOptions();
builder.Services.AddSingleton(persistenceOptions);
if (string.Equals(persistenceOptions.Provider, "File", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IMortgageApplicationRepository, FileMortgageApplicationRepository>();
    builder.Services.AddSingleton<IMortgageDocumentStorage>(_ => new FileMortgageDocumentStorage(persistenceOptions.RootPath));
}
else
{
    builder.Services.AddSingleton<IMortgageApplicationRepository, InMemoryMortgageApplicationRepository>();
    builder.Services.AddSingleton<IMortgageDocumentStorage, InMemoryMortgageDocumentStorage>();
}
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IWizardSession, WizardSession>();
builder.Services.AddSingleton<IWizardPrerequisiteService, WizardPrerequisiteService>();
builder.Services.AddSingleton<WizardViewModelFactory>();
builder.Services.AddSingleton<IDocumentUploadValidator, DocumentUploadValidator>();
builder.Services.AddSingleton<LocalProductCatalogFallback>();
var useMockProductService = bool.TryParse(builder.Configuration["ProductService:UseMock"], out var configuredUseMock)
    && configuredUseMock;

if (useMockProductService)
{
    builder.Services.AddSingleton<IProductCatalogClient, MockProductCatalogClient>();
}
else
{
    builder.Services.AddTransient<ProductServiceResilienceHandler>();
    builder.Services.AddTransient<ProductCorrelationHandler>();
    builder.Services.AddHttpClient<IProductCatalogClient, ProductCatalogClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["ProductService:BaseUrl"] ?? "http://localhost:5260/");
        client.Timeout = TimeSpan.FromSeconds(10);
        var apiKey = builder.Configuration["ProductService:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Product-Service-Key", apiKey);
        }
    }).AddHttpMessageHandler<ProductCorrelationHandler>()
      .AddHttpMessageHandler<ProductServiceResilienceHandler>();
}
builder.Services.AddScoped<IMortgageApplicationService, MortgageApplicationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program { }
