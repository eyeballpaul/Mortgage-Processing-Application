using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PD.Eligibility.Contracts;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Types.Enums;

namespace PD.Mortgages.Infrastructure.Eligibility;

public sealed class EligibilityClient(HttpClient client, ILogger<EligibilityClient> logger) : IEligibilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<DecisionInPrincipleResult> AssessAsync(decimal propertyValue, decimal deposit, decimal annualIncome,
        bool creditCheckConsent, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("api/v1/eligibility/assess",
                new EligibilityRequest(propertyValue, deposit, annualIncome, creditCheckConsent), JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EligibilityResponse>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Eligibility returned an empty response.");
            var outcome = result.Outcome switch
            {
                EligibilityOutcome.CreditCheckNotSelected => DecisionInPrincipleOutcome.CreditCheckNotSelected,
                EligibilityOutcome.WithinIndicativeRange => DecisionInPrincipleOutcome.WithinIndicativeRange,
                EligibilityOutcome.AboveIndicativeRange => DecisionInPrincipleOutcome.AboveIndicativeRange,
                _ => throw new JsonException("Eligibility returned an unknown outcome.")
            };
            if (string.IsNullOrWhiteSpace(result.RuleVersion) || result.RequestedLoanAmount != propertyValue - deposit ||
                (creditCheckConsent ? outcome == DecisionInPrincipleOutcome.CreditCheckNotSelected || result.IndicativeMaximumBorrowing is null or < 0 :
                    outcome != DecisionInPrincipleOutcome.CreditCheckNotSelected || result.IndicativeMaximumBorrowing is not null))
                throw new JsonException("Eligibility returned an inconsistent decision.");
            return new DecisionInPrincipleResult(outcome, result.RequestedLoanAmount,
                result.IndicativeMaximumBorrowing, result.AssessedAtUtc.UtcDateTime, result.RuleVersion);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Eligibility service request failed ({ExceptionType})", exception.GetType().Name);
            throw new EligibilityUnavailableException();
        }
    }
}
