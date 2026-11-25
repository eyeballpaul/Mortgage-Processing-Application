using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Staff.Web.Configuration;
using PD.Mortgages.Staff.Web.Health;
using PD.Mortgages.Staff.Web.Security;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("The staff portal requires configured employee authentication before it can run outside Development.");
}

builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<ApplicationHealthCheck>("application", tags: ["ready"]);
builder.Services.AddOptions<ApplicationSecurityOptions>()
    .BindConfiguration(ApplicationSecurityOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IApplicationActor, DevelopmentStaffApplicationActor>();

builder.Services.AddMortgagePersistence(builder.Configuration);

if (bool.TryParse(builder.Configuration["ProductService:UseMock"], out var useMockProductService) && useMockProductService)
{
    builder.Services.AddSingleton<LocalProductCatalogFallback>();
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

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IValidateOptions<ApplicationSecurityOptions>, StaffSecurityOptionsValidator>();
builder.Services.AddScoped<IMortgageApplicationService, MortgageApplicationService>();

var app = builder.Build();
await app.Services.MigrateMortgageDatabaseAsync();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Applications}/{action=Index}/{id?}");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.Run();

public partial class Program { }
