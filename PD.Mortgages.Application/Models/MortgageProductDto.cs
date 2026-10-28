namespace PD.Mortgages.Application.Models;

public sealed record MortgageProductDto(
    string Id,
    string Name,
    decimal InterestRate,
    decimal Fee,
    decimal Aprc,
    int FixedTermYears,
    int MaximumLoanToValue);
