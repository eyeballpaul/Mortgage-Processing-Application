using PD.Mortgages.Domain.Entities;

namespace PD.Mortgages.Application.Services;

public interface IEligibilityClient
{
    Task<DecisionInPrincipleResult> AssessAsync(decimal propertyValue, decimal deposit, decimal annualIncome,
        bool creditCheckConsent, CancellationToken cancellationToken);
}

public sealed class EligibilityUnavailableException : Exception
{
    public EligibilityUnavailableException() : base("The eligibility service is temporarily unavailable. Please try again.") { }
}
