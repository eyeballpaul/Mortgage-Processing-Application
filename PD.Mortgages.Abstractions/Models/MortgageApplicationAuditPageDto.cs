namespace PD.Mortgages.Application.Models;

public sealed record MortgageApplicationAuditEntryDto(DateTime RecordedAtUtc, string ActorId, string Action);

public sealed record MortgageApplicationAuditPageDto(
    IReadOnlyList<MortgageApplicationAuditEntryDto> Events,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
