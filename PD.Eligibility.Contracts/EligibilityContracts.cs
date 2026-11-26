using System.Text.Json.Serialization;

namespace PD.Eligibility.Contracts;

/// <summary>Financial inputs for a fictional indicative assessment.</summary>
public sealed record EligibilityRequest(
    [property: JsonRequired] decimal PropertyValue,
    [property: JsonRequired] decimal Deposit,
    [property: JsonRequired] decimal AnnualIncome,
    [property: JsonRequired] bool CreditCheckConsent);

/// <summary>Possible outcomes of an indicative assessment.</summary>
public enum EligibilityOutcome
{
    CreditCheckNotSelected,
    WithinIndicativeRange,
    AboveIndicativeRange
}

/// <summary>A fictional indicative assessment and its rule version.</summary>
public sealed record EligibilityResponse(
    EligibilityOutcome Outcome,
    decimal RequestedLoanAmount,
    decimal? IndicativeMaximumBorrowing,
    DateTimeOffset AssessedAtUtc,
    string RuleVersion);
