using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using PD.Eligibility.Api;
using PD.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptionalTracing(builder.Environment, "eligibility-api");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IEligibilityService, EligibilityService>();
var app = builder.Build();
var key = app.Configuration["EligibilityService:ApiKey"];
if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
    throw new InvalidOperationException("EligibilityService:ApiKey must contain at least 32 characters.");
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        (!context.Request.Headers.TryGetValue("X-Eligibility-Service-Key", out var supplied) ||
         !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied.ToString()), Encoding.UTF8.GetBytes(key))))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapEligibility();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.Run();

public partial class Program { }
