using System.Net;
using System.Net.Http.Json;
using PD.Mortgages.Infrastructure.Products;
using PD.Products.Contracts;

namespace PD.Mortgages.Tests;

public sealed class ProductCatalogClientTests
{
    [Fact]
    public async Task MapsProductResponsesToApplicationDtos()
    {
        var handler = new StubHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new ProductResponse("fixed-2", "2 year fixed", 4.29m, 999m, 5.9m, 2, 90)
                })
            };
            return Task.FromResult(response);
        });
        var client = new ProductCatalogClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://products.test/")
        });

        var products = await client.GetProductsAsync();

        var product = Assert.Single(products);
        Assert.Equal("fixed-2", product.Id);
        Assert.Equal(4.29m, product.InterestRate);
    }

    [Fact]
    public async Task ReturnsNullForMissingProduct()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new ProductCatalogClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://products.test/")
        });

        var product = await client.GetProductAsync("missing");

        Assert.Null(product);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
