namespace PD.Mortgages.Domain.Entities;

public sealed record MortgageProduct(
    string Id,
    string Name,
    decimal InterestRate,
    decimal Fee,
    decimal Aprc,
    int FixedTermYears,
    int MaximumLoanToValue);
