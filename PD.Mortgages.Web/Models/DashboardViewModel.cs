using PD.Mortgages.Application.Models;

namespace PD.Mortgages.Web.Models;

public sealed class DashboardViewModel
{
    public MortgageApplicationDto Application { get; set; } = default!;
    public MortgageProductDto? Product { get; set; }
    public int CurrentStep { get; set; }
    public decimal? EstimatedMonthlyPayment { get; set; }
}
