using PD.Mortgages.Application.Models;

namespace PD.Mortgages.Application.Services;

public interface IProductCatalogClient
{
    Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default);
    Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default);
}
