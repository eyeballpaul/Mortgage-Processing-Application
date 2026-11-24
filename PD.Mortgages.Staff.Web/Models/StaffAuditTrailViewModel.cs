using PD.Mortgages.Application.Models;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Staff.Web.Models;

public sealed class StaffAuditTrailViewModel
{
    public Guid ApplicationId { get; set; }
    public ApplicationStatus Status { get; set; }
    public IReadOnlyList<MortgageApplicationAuditPageDto> Pages { get; set; } = [];
    public IReadOnlyList<PD.Mortgages.Domain.Entities.MortgageApplicationAuditEntry> Events { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
