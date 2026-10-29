using System.Security.Cryptography;
using System.Text;

namespace PD.Products.Api.Middleware;

public sealed class ProductApiKeyMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<ProductApiKeyMiddleware> logger)
{
    private const string HeaderName = "X-Product-Service-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var configuredKey = configuration["ProductService:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (!environment.IsDevelopment())
            {
                logger.LogCritical("Product service API key is not configured outside development.");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            await next(context);
            return;
        }

        var suppliedKey = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(suppliedKey) || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(suppliedKey),
                Encoding.UTF8.GetBytes(configuredKey)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
