using PD.Products.Domain.Entities;
using PD.Products.Domain.Services;

namespace PD.Products.Tests;

public sealed class ProductEligibilityServiceTests
{
    private static readonly IReadOnlyList<MortgageProduct> Products =
    [
        new("90", "90% product", 4m, 0m, 4m, 2, 90),
        new("85", "85% product", 5m, 0m, 5m, 5, 85)
    ];

    [Fact]
    public void ReturnsProductsWithinTheCalculatedLtvLimit()
    {
        var service = new ProductEligibilityService();

        var eligible = service.GetEligibleProducts(200_000m, 20_000m, Products);

        Assert.Single(eligible);
        Assert.Equal("90", eligible[0].Id);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(200_000, -1)]
    [InlineData(200_000, 200_000)]
    public void RejectsInvalidPropertyAndDepositValues(decimal propertyValue, decimal deposit)
    {
        var service = new ProductEligibilityService();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.GetEligibleProducts(propertyValue, deposit, Products));
    }
}
