using PD.Mortgages.Domain.Entities;

namespace PD.Mortgages.Domain.Repositories;

public sealed record MortgageApplicationAuditPage(
    IReadOnlyList<MortgageApplicationAuditEntry> Events,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
