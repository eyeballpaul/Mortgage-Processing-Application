using System.Security.Claims;

namespace PD.Mortgages.Api.Security;

public static class MortgageCallerAuthorization
{
    public static bool IsAllowed(ClaimsPrincipal user, IConfiguration configuration) =>
        IsCustomer(user, configuration) || IsStaff(user, configuration);

    public static bool IsCustomer(ClaimsPrincipal user, IConfiguration configuration) =>
        user.Identity?.IsAuthenticated == true &&
        (user.Identity.AuthenticationType == DevelopmentCustomerAuthenticationHandler.SchemeName ||
         (CallerMatches(user, configuration["MortgageApi:CustomerClientId"]) &&
          !user.HasClaim(claim => claim.Type == "scp") &&
          user.HasClaim("roles", "MortgageCustomer")));

    public static bool IsStaff(ClaimsPrincipal user, IConfiguration configuration) =>
        user.Identity?.IsAuthenticated == true &&
        CallerMatches(user, configuration["MortgageApi:StaffClientId"]) &&
        user.FindAll("scp").Any(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("Mortgage.Staff", StringComparer.Ordinal)) &&
        user.HasClaim("roles", "MortgageStaff") &&
        Guid.TryParse(user.FindFirstValue("tid"), out _) &&
        Guid.TryParse(user.FindFirstValue("oid"), out _);

    private static bool CallerMatches(ClaimsPrincipal user, string? expectedClientId) =>
        Guid.TryParse(expectedClientId, out var expected) &&
        Guid.TryParse(user.FindFirstValue("azp") ?? user.FindFirstValue("appid"), out var actual) &&
        expected == actual;
}
