namespace PD.Products.Contracts;

public sealed record ProductResponse(
    string Id,
    string Name,
    decimal InterestRate,
    decimal Fee,
    decimal Aprc,
    int FixedTermYears,
    int MaximumLoanToValue);
