using PD.Eligibility.Contracts;

namespace PD.Eligibility.Api;

public interface IEligibilityService
{
    Task<EligibilityResponse> AssessAsync(EligibilityRequest request, CancellationToken cancellationToken);
}

public sealed class EligibilityService : IEligibilityService
{
    public async Task<EligibilityResponse> AssessAsync(EligibilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CreditCheckConsent)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var loan = request.PropertyValue - request.Deposit;
        var maximum = request.CreditCheckConsent ? request.AnnualIncome * 4.5m : (decimal?)null;
        var outcome = maximum is null ? EligibilityOutcome.CreditCheckNotSelected :
            loan > maximum ? EligibilityOutcome.AboveIndicativeRange : EligibilityOutcome.WithinIndicativeRange;
        return new EligibilityResponse(outcome, loan, maximum, DateTimeOffset.UtcNow, "1");
    }
}
