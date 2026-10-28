using PD.Mortgages.Application.Models;

namespace PD.Mortgages.Web.Models;

public sealed class ProductsViewModel
{
    public string? SelectedProductId { get; set; }
    public decimal LoanAmount { get; set; }
    public IReadOnlyList<MortgageProductDto> Products { get; set; } = [];
}
