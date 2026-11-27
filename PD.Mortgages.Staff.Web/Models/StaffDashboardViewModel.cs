using PD.Mortgages.Application.Models;
using PD.Mortgages.Domain.Types.Enums;

namespace PD.Mortgages.Staff.Web.Models;

public sealed class StaffDashboardViewModel
{
    public string? Search { get; set; }
    public ApplicationStatus? Status { get; set; }
    public IReadOnlyList<MortgageApplicationDto> Applications { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalPages { get; set; } = 1;
}
