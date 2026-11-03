using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PD.Mortgages.Infrastructure.Products;

namespace PD.Mortgages.Tests;

public sealed class ProductResilienceTests
{
    [Fact]
    public async Task ProductReadsUseLocalFallbackWhenServiceIsUnavailable()
    {
        var transport = new StubHandler((_, _) => throw new HttpRequestException("service unavailable"));
        var resilience = new ProductServiceResilienceHandler(NullLogger<ProductServiceResilienceHandler>.Instance)
        {
            InnerHandler = transport
        };
        var httpClient = new HttpClient(resilience) { BaseAddress = new Uri("http://products.test/") };
        var client = new ProductCatalogClient(httpClient, new LocalProductCatalogFallback());

        var products = await client.GetProductsAsync();

        Assert.Equal(4, products.Count);
    }

    [Fact]
    public async Task CircuitBreakerShortCircuitsAfterRepeatedTransientFailures()
    {
        var transport = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var resilience = new ProductServiceResilienceHandler(NullLogger<ProductServiceResilienceHandler>.Instance)
        {
            InnerHandler = transport
        };
        using var client = new HttpClient(resilience) { BaseAddress = new Uri("http://products.test/") };

        for (var index = 0; index < 3; index++)
        {
            using var response = await client.GetAsync("api/v1/products");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        using var shortCircuitedResponse = await client.GetAsync("api/v1/products");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, shortCircuitedResponse.StatusCode);
        Assert.Equal(9, transport.RequestCount);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return send(request, cancellationToken);
        }
    }
}
