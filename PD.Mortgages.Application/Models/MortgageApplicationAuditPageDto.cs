using PD.Mortgages.Domain.Entities;

namespace PD.Mortgages.Application.Models;

public sealed record MortgageApplicationAuditPageDto(
    IReadOnlyList<MortgageApplicationAuditEntry> Events,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
