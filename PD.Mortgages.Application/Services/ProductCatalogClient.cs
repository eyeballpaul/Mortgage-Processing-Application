using System.Net;
using System.Net.Http.Json;
using PD.Mortgages.Application.Models;
using PD.Products.Contracts;

namespace PD.Mortgages.Application.Services;

public sealed class ProductCatalogClient(HttpClient httpClient) : IProductCatalogClient
{
    public Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        GetProductListAsync("api/products", cancellationToken);

    public Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(
        decimal propertyValue,
        decimal deposit,
        CancellationToken cancellationToken = default) =>
        GetProductListAsync(
            $"api/products/eligible?propertyValue={Uri.EscapeDataString(propertyValue.ToString(System.Globalization.CultureInfo.InvariantCulture))}&deposit={Uri.EscapeDataString(deposit.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
            cancellationToken);

    public async Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/products/{Uri.EscapeDataString(id)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);
        return product is null ? null : Map(product);
    }

    private async Task<IReadOnlyList<MortgageProductDto>> GetProductListAsync(string uri, CancellationToken cancellationToken)
    {
        var products = await httpClient.GetFromJsonAsync<List<ProductResponse>>(uri, cancellationToken) ?? [];
        return products.Select(Map).ToList();
    }

    private static MortgageProductDto Map(ProductResponse product) => new(
        product.Id,
        product.Name,
        product.InterestRate,
        product.Fee,
        product.Aprc,
        product.FixedTermYears,
        product.MaximumLoanToValue);
}
