using System.Net.Http.Headers;
using Microsoft.Identity.Web;
using PD.Mortgages.Client;

namespace PD.Mortgages.Staff.Web.Security;

public sealed class StaffMortgageApiCredentials(
    ITokenAcquisition tokenAcquisition,
    IHttpContextAccessor accessor,
    IConfiguration configuration) : IMortgageApiCredentials
{
    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = configuration["MortgageApi:Scope"]
            ?? throw new InvalidOperationException("MortgageApi:Scope is required.");
        var user = accessor.HttpContext?.User ?? throw new InvalidOperationException("A staff request is required.");
        var token = await tokenAcquisition.GetAccessTokenForUserAsync([scope], user: user);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
