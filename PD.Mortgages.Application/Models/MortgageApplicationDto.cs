using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;

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
    bool OpenBankingConsent,
    bool ConsentsRecorded,
    DecisionInPrincipleResult? DecisionInPrinciple,
    string? SelectedProductId,
    IReadOnlyList<MortgageDocument> Documents);
