using PD.Products.Domain.Entities;

namespace PD.Products.Infrastructure.Persistence;

public static class MortgageProductSeedData
{
    private static readonly MortgageProduct[] SeedProducts =
    [
        new("fixed-2", "2 year fixed", 4.29m, 999m, 5.9m, 2, 90),
        new("fixed-5", "5 year fixed", 4.09m, 999m, 5.5m, 5, 90),
        new("fixed-5-95", "5 year fixed · 95% LTV", 5.19m, 999m, 6.4m, 5, 95),
        new("tracker-2", "2 year tracker", 4.64m, 0m, 5.8m, 2, 85)
    ];

    public static IReadOnlyList<MortgageProduct> Products { get; } = Array.AsReadOnly(SeedProducts);
}
