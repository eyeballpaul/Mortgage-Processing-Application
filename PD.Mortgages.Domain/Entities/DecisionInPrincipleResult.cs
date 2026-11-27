using PD.Mortgages.Domain.Types.Enums;

namespace PD.Mortgages.Domain.Entities;

public sealed record DecisionInPrincipleResult(
    DecisionInPrincipleOutcome Outcome,
    decimal RequestedLoanAmount,
    decimal? IndicativeMaximumBorrowing,
    DateTime AssessedAtUtc,
    string? RuleVersion = null);
