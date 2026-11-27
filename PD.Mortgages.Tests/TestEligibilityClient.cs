using PD.Eligibility.Api;
using PD.Eligibility.Contracts;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Types.Enums;

namespace PD.Mortgages.Tests;

internal sealed class TestEligibilityClient : IEligibilityClient
{
    public async Task<DecisionInPrincipleResult> AssessAsync(decimal propertyValue, decimal deposit, decimal annualIncome,
        bool creditCheckConsent, CancellationToken cancellationToken)
    {
        var result = await new EligibilityService().AssessAsync(
            new EligibilityRequest(propertyValue, deposit, annualIncome, creditCheckConsent), cancellationToken);
        return new DecisionInPrincipleResult(Enum.Parse<DecisionInPrincipleOutcome>(result.Outcome.ToString()),
            result.RequestedLoanAmount, result.IndicativeMaximumBorrowing, result.AssessedAtUtc.UtcDateTime, result.RuleVersion);
    }

    public static void Record(MortgageApplication application)
    {
        var inputs = application.GetDecisionInputs();
        var maximum = inputs.CreditCheckConsent ? inputs.AnnualIncome * 4.5m : (decimal?)null;
        var outcome = maximum is null ? DecisionInPrincipleOutcome.CreditCheckNotSelected :
            inputs.PropertyValue - inputs.Deposit > maximum ? DecisionInPrincipleOutcome.AboveIndicativeRange :
            DecisionInPrincipleOutcome.WithinIndicativeRange;
        application.RecordIndicativeDecision(new DecisionInPrincipleResult(outcome,
            inputs.PropertyValue - inputs.Deposit, maximum, DateTime.UtcNow, "1"));
    }
}
