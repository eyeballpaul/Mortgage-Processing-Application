using PD.Mortgages.Application.Models;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Web.Models;

public sealed class ProductsViewModel
{
    public string? SelectedProductId { get; set; }
    public decimal LoanAmount { get; set; }
    public DecisionInPrincipleOutcome? DipOutcome { get; set; }
    public IReadOnlyList<MortgageProductDto> Products { get; set; } = [];
}
