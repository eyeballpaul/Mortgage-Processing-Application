using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Web.Health;
using PD.Mortgages.Web.Middleware;
using PD.Mortgages.Web.Services;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;
using PD.Mortgages.Infrastructure.Products;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<ApplicationHealthCheck>("application", tags: ["ready"]);
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(30);
    options.Cookie.Name = ".PDMortgages.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = TimeSpan.FromDays(30);
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddSingleton<IMortgageApplicationRepository, InMemoryMortgageApplicationRepository>();
builder.Services.AddSingleton<IMortgageDocumentStorage, InMemoryMortgageDocumentStorage>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IWizardSession, WizardSession>();
builder.Services.AddSingleton<IDocumentUploadValidator, DocumentUploadValidator>();
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
builder.Services.AddSingleton<IMortgageApplicationService, MortgageApplicationService>();

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
