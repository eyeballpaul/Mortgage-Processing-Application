using System.Net;
using System.Net.Http.Json;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;
using PD.Products.Contracts;

namespace PD.Mortgages.Infrastructure.Products;

public sealed class ProductCatalogClient(
    HttpClient httpClient,
    LocalProductCatalogFallback? fallback = null) : IProductCatalogClient
{
    public async Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetProductListAsync("api/v1/products", cancellationToken);
        }
        catch (Exception exception) when (IsFallbackEligible(exception, cancellationToken) && fallback is not null)
        {
            return await fallback.GetProductsAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(
        decimal propertyValue,
        decimal deposit,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetProductListAsync(
                $"api/v1/products/eligible?propertyValue={Uri.EscapeDataString(propertyValue.ToString(System.Globalization.CultureInfo.InvariantCulture))}&deposit={Uri.EscapeDataString(deposit.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
                cancellationToken);
        }
        catch (Exception exception) when (IsFallbackEligible(exception, cancellationToken) && fallback is not null)
        {
            return await fallback.GetEligibleProductsAsync(propertyValue, deposit, cancellationToken);
        }
    }

    public async Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/products/{Uri.EscapeDataString(id)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);
        return product is null ? null : Map(product);
    }

    public async Task<bool> IsProductEligibleAsync(
        string id,
        decimal propertyValue,
        decimal deposit,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"api/v1/products/{Uri.EscapeDataString(id)}/eligibility?propertyValue={Uri.EscapeDataString(propertyValue.ToString(System.Globalization.CultureInfo.InvariantCulture))}&deposit={Uri.EscapeDataString(deposit.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
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
        product.MaximumLoanToValue,
        product.CatalogVersion);

    private static bool IsFallbackEligible(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (exception is HttpRequestException || exception is TaskCanceledException);
}
