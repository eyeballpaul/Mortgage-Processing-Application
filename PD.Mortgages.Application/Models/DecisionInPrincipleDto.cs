using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Application.Models;

public sealed record DecisionInPrincipleDto(
    DecisionInPrincipleOutcome Outcome,
    decimal RequestedLoanAmount,
    decimal? IndicativeMaximumBorrowing,
    DateTime AssessedAtUtc);
