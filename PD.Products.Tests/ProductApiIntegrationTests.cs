using System.Net;
using System.Net.Http.Json;
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
        Assert.All(products, product => Assert.Equal("1", product.CatalogVersion));
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

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Product-Service-Key", "dev-product-api-key");
        return client;
    }
}
