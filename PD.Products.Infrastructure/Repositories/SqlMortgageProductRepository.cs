using Microsoft.EntityFrameworkCore;
using PD.Products.Domain.Entities;
using PD.Products.Domain.Repositories;
using PD.Products.Infrastructure.Persistence;

namespace PD.Products.Infrastructure.Repositories;

public sealed class SqlMortgageProductRepository(ProductsDbContext database) : IMortgageProductRepository
{
    public async Task<IReadOnlyList<MortgageProduct>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await database.Products
            .AsNoTracking()
            .OrderBy(product => product.Id)
            .ToListAsync(cancellationToken);

    public Task<MortgageProduct?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        database.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(product => product.Id == id, cancellationToken);
}
