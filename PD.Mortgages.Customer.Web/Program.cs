using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using PD.Mortgages.CustomerSessions;
using PD.Mortgages.Client;
using PD.Mortgages.Application.Services;
using PD.Shared;
using PD.Mortgages.Web.Services;
using PD.Mortgages.Web.Security;
using PD.Mortgages.Web.Configuration;
using PD.Mortgages.Web.Health;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptionalTracing(builder.Environment, "customer-web");

builder.Services.AddControllersWithViews();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("application-creation", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));
    options.AddPolicy("dependency-check", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddProblemDetails();
builder.Services.AddAzureBlobDataProtection(builder.Configuration);
ProductionConfigurationValidator.Validate(builder.Environment, builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<MortgageApiHealthCheck>("mortgage-api", tags: ["ready"])
    .AddCheck<CustomerSessionHealthCheck>("customer-session-store", tags: ["ready"])
    .AddCheck<MortgageDependencyHealthCheck>("mortgage-dependencies", tags: ["dependencies"]);
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
else if (string.Equals(sessionProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var sessionConnectionString = builder.Configuration.GetConnectionString("customersessions")
        ?? throw new InvalidOperationException("ConnectionStrings:customersessions is required when Session:Provider is SqlServer.");
    builder.Services.AddDbContext<CustomerSessionDbContext>(options => options.UseSqlServer(sessionConnectionString));
    builder.Services.AddDistributedSqlServerCache(options =>
    {
        options.ConnectionString = sessionConnectionString;
        options.SchemaName = "dbo";
        options.TableName = "MortgageSessionCache";
    });
}
else if (string.Equals(sessionProvider, "Memory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    throw new InvalidOperationException($"Session provider '{sessionProvider}' is not supported.");
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

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IWizardSession, WizardSession>();
builder.Services.AddSingleton<WizardViewModelFactory>();
builder.Services.AddSingleton<IDocumentUploadValidator, DocumentUploadValidator>();
builder.Services.AddScoped<IMortgageApiCredentials, CustomerMortgageApiCredentials>();
builder.Services.AddMortgageApiClient(builder.Configuration, builder.Environment);

var app = builder.Build();
if (string.Equals(sessionProvider, "SqlServer", StringComparison.OrdinalIgnoreCase) &&
    builder.Configuration.GetValue<bool>("Session:RunMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CustomerSessionDbContext>().Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
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
app.MapHealthChecks("/health/dependencies", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("dependencies")
}).RequireRateLimiting("dependency-check");

app.Run();

public partial class Program { }
