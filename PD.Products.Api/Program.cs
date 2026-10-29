using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PD.Products.Application.Services;
using PD.Products.Domain.Repositories;
using PD.Products.Domain.Services;
using PD.Products.Api.Health;
using PD.Products.Api.Middleware;
using PD.Products.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<ProductCatalogHealthCheck>("product-catalog", tags: ["ready"]);
builder.Services.AddSingleton<IMortgageProductRepository, InMemoryMortgageProductRepository>();
builder.Services.AddSingleton<ProductEligibilityService>();
builder.Services.AddSingleton<IProductService, ProductService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseMiddleware<ProductApiKeyMiddleware>();
app.UseMiddleware<SimpleRateLimitMiddleware>();
app.MapControllers();
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
