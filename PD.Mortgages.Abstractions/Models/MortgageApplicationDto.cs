using PD.Mortgages.Domain.Types.Enums;

namespace PD.Mortgages.Application.Models;

public sealed record MortgageApplicationDto(
    Guid Id,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    ApplicationStatus Status,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber,
    decimal? PropertyValue,
    decimal? Deposit,
    decimal? AnnualIncome,
    decimal? MonthlyCommitments,
    EmploymentType? EmploymentType,
    bool CreditCheckConsent,
    bool ConsentsRecorded,
    DecisionInPrincipleDto? DecisionInPrinciple,
    string? SelectedProductId,
    IReadOnlyList<MortgageDocumentDto> Documents,
    string? CreatedByEmployeeId,
    long Version,
    MortgageProductDto? SelectedProduct = null);
