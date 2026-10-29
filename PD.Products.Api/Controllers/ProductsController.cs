using Microsoft.AspNetCore.Mvc;
using PD.Products.Application.Services;
using PD.Products.Contracts;
using PD.Products.Domain.Entities;

namespace PD.Products.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetProducts(CancellationToken cancellationToken)
    {
        var products = await productService.GetProductsAsync(cancellationToken);
        return Ok(products.Select(Map).ToList());
    }

    [HttpGet("eligible")]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetEligibleProducts(
        [FromQuery] EligibleProductsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var products = await productService.GetEligibleProductsAsync(request.PropertyValue, request.Deposit, cancellationToken);
            return Ok(products.Select(Map).ToList());
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(new ProblemDetails { Detail = exception.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ProductResponse>> GetProduct(string id, CancellationToken cancellationToken)
    {
        var product = await productService.GetProductAsync(id, cancellationToken);
        return product is null ? NotFound() : Ok(Map(product));
    }

    private static ProductResponse Map(MortgageProduct product) => new(
        product.Id,
        product.Name,
        product.InterestRate,
        product.Fee,
        product.Aprc,
        product.FixedTermYears,
        product.MaximumLoanToValue);
}
