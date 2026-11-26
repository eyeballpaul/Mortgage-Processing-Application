using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PD.Products.Application.Services;
using PD.Products.Domain.Services;
using PD.Products.Api.Health;
using PD.Products.Api.Middleware;
using PD.Shared;
using PD.Products.Infrastructure.Persistence;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptionalTracing(builder.Environment, "products-api");

if (!builder.Environment.IsDevelopment() &&
    (!string.Equals(builder.Configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase) ||
     string.IsNullOrWhiteSpace(builder.Configuration["ProductService:ApiKey"])))
{
    throw new InvalidOperationException("The Products API requires SQL Server and a product service API key outside Development.");
}

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddHealthChecks()
    .AddCheck<ProductCatalogHealthCheck>("product-catalog", tags: ["ready"]);
builder.Services.AddProductPersistence(builder.Configuration);
builder.Services.AddSingleton<ProductEligibilityService>();
builder.Services.AddScoped<IProductService, ProductService>();

var app = builder.Build();
await app.Services.MigrateProductDatabaseAsync(builder.Configuration);

app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseMiddleware<ProductApiKeyMiddleware>();
app.UseRateLimiter();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).DisableRateLimiting();

app.Run();

public partial class Program { }
