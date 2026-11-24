using PD.Mortgages.Domain.Entities;

namespace PD.Mortgages.Domain.Repositories;

public sealed record MortgageApplicationSearchResult(
    IReadOnlyList<MortgageApplication> Applications,
    int TotalCount,
    int Page,
    int PageSize);
