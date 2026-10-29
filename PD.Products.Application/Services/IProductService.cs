using PD.Products.Domain.Entities;

namespace PD.Products.Application.Services;

public interface IProductService
{
    Task<IReadOnlyList<MortgageProduct>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MortgageProduct>> GetEligibleProductsAsync(decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default);
    Task<MortgageProduct?> GetProductAsync(string id, CancellationToken cancellationToken = default);
}
