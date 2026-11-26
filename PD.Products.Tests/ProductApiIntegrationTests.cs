using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using PD.Products.Contracts;

namespace PD.Products.Tests;

public sealed class ProductApiIntegrationTests(ProductApiFactory factory) : IClassFixture<ProductApiFactory>
{
    [Fact]
    public async Task ProductsEndpointRequiresApiKey()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProductsEndpointReturnsContractResponsesWithValidApiKey()
    {
        using var client = CreateAuthenticatedClient();

        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/api/v1/products");

        Assert.NotNull(products);
        Assert.Equal(4, products.Count);
        Assert.Contains(products, product => product.Id == "fixed-2");
        var product = Assert.Single(products, item => item.Id == "fixed-2");
        var fetched = await client.GetFromJsonAsync<ProductResponse>("/api/v1/products/fixed-2");
        Assert.Equal(product.CatalogVersion, fetched!.CatalogVersion);
        Assert.NotEqual(product.CatalogVersion, products.First(item => item.Id == "fixed-5").CatalogVersion);
    }

    [Fact]
    public async Task EligibleProductsEndpointAppliesEligibilityRules()
    {
        using var client = CreateAuthenticatedClient();

        var products = await client.GetFromJsonAsync<List<ProductResponse>>(
            "/api/v1/products/eligible?propertyValue=300000&deposit=30000");

        Assert.NotNull(products);
        Assert.Equal(3, products.Count);
        Assert.DoesNotContain(products, product => product.Id == "tracker-2");
    }

    [Fact]
    public async Task ReadinessEndpointReturnsHealthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthProbesRemainAvailableWhenRateLimitIsExhausted()
    {
        using var isolatedFactory = new ProductApiFactory();
        using var client = isolatedFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Product-Service-Key", "dev-product-api-key");
        for (var index = 0; index < 61; index++)
        {
            using var response = await client.GetAsync("/api/v1/products");
        }

        using var limited = await client.GetAsync("/api/v1/products");
        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public void ProductionRejectsInMemoryPersistence()
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var exception = Assert.Throws<InvalidOperationException>(() => production.CreateClient());
        Assert.Contains("requires SQL Server", exception.Message);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Product-Service-Key", "dev-product-api-key");
        return client;
    }
}
