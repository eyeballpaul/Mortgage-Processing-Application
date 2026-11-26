using Microsoft.AspNetCore.Http.HttpResults;
using PD.Eligibility.Contracts;

namespace PD.Eligibility.Api;

public static class EligibilityEndpoints
{
    public static void MapEligibility(this WebApplication app)
    {
        app.MapPost("/api/v1/eligibility/assess", async Task<Results<Ok<EligibilityResponse>, ValidationProblem>> (
            EligibilityRequest request, IEligibilityService service, CancellationToken cancellationToken) =>
        {
            if (request.PropertyValue is < 50_000 or > 5_000_000 ||
                request.Deposit < 0 || request.Deposit >= request.PropertyValue ||
                request.AnnualIncome is < 10_000 or > 10_000_000)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["inputs"] = ["Property value, deposit or annual income is outside the supported range."]
                });
            return TypedResults.Ok(await service.AssessAsync(request, cancellationToken));
        }).WithName("AssessEligibility")
          .WithSummary("Calculate a fictional indicative mortgage decision")
          .WithDescription("Does not contact a credit agency or lender.");
    }
}
