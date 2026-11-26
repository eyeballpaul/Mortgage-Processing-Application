using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Client;

public static class MortgageClientRegistration
{
    public static void AddMortgageApiClient(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!Uri.TryCreate(configuration["MortgageApi:BaseUrl"], UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && address.IsLoopback && address.Scheme == Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException("MortgageApi:BaseUrl must be an HTTPS address (HTTP loopback is allowed in Development).");
        }
        if (string.IsNullOrWhiteSpace(configuration["MortgageApi:Scope"]))
        {
            throw new InvalidOperationException("MortgageApi:Scope is required.");
        }

        services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>(client =>
        {
            client.BaseAddress = address;
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient("MortgageApiHealth", client =>
        {
            client.BaseAddress = address;
            client.Timeout = TimeSpan.FromSeconds(5);
        });
    }
}

public sealed class MortgageApiHealthCheck(IHttpClientFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var response = await factory.CreateClient("MortgageApiHealth").GetAsync("health/ready", cancellationToken);
        return response.IsSuccessStatusCode
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The Mortgage API is not ready.");
    }

}

public sealed class MortgageDependencyHealthCheck(IHttpClientFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var response = await factory.CreateClient("MortgageApiHealth").GetAsync("health/dependencies", cancellationToken);
        return response.IsSuccessStatusCode
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The Mortgage API dependency check failed.");
    }
}
