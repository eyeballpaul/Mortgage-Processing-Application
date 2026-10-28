using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Domain.Entities;

public sealed record DecisionInPrincipleResult(
    DecisionInPrincipleOutcome Outcome,
    decimal RequestedLoanAmount,
    decimal? IndicativeMaximumBorrowing,
    DateTime AssessedAtUtc);
