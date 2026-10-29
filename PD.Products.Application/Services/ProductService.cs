using PD.Products.Domain.Entities;
using PD.Products.Domain.Repositories;
using PD.Products.Domain.Services;

namespace PD.Products.Application.Services;

public sealed class ProductService(
    IMortgageProductRepository productRepository,
    ProductEligibilityService eligibilityService) : IProductService
{
    public Task<IReadOnlyList<MortgageProduct>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        productRepository.GetAllAsync(cancellationToken);

    public async Task<IReadOnlyList<MortgageProduct>> GetEligibleProductsAsync(
        decimal propertyValue,
        decimal deposit,
        CancellationToken cancellationToken = default)
    {
        var products = await productRepository.GetAllAsync(cancellationToken);
        return eligibilityService.GetEligibleProducts(propertyValue, deposit, products);
    }

    public Task<MortgageProduct?> GetProductAsync(string id, CancellationToken cancellationToken = default) =>
        productRepository.GetByIdAsync(id, cancellationToken);
}
