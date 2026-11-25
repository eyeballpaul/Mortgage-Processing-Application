using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PD.Products.Infrastructure.Persistence;

public sealed class ProductsDbContextFactory : IDesignTimeDbContextFactory<ProductsDbContext>
{
    public ProductsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__products")
            ?? throw new InvalidOperationException("Set ConnectionStrings__products when creating or applying product migrations.");

        var options = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ProductsDbContext(options);
    }
}
