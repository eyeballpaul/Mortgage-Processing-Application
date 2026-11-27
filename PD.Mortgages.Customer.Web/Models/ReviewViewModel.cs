using PD.Mortgages.Application.Models;

namespace PD.Mortgages.Web.Models;

public sealed class ReviewViewModel
{
    public MortgageApplicationDto Application { get; set; } = default!;
    public MortgageProductDto? Product { get; set; }
    public decimal? EstimatedMonthlyPayment { get; set; }
}
