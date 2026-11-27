using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using PD.Mortgages.Client;
using PD.Mortgages.Web.Services;

namespace PD.Mortgages.Web.Security;

public sealed class CustomerMortgageApiCredentials(
    IWizardSession session, IConfiguration configuration, IHostEnvironment environment) : IMortgageApiCredentials
{
    private static readonly TokenCredential Credential = new DefaultAzureCredential();

    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var developmentKey = configuration["MortgageApi:DevelopmentKey"];
        if (environment.IsDevelopment() && !string.IsNullOrWhiteSpace(developmentKey))
        {
            request.Headers.Add("X-Development-Api-Key", developmentKey);
        }
        else
        {
            var scope = configuration["MortgageApi:Scope"]
                ?? throw new InvalidOperationException("MortgageApi:Scope is required.");
            var token = await Credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        request.Headers.Add("X-Mortgage-Session", session.ActorId);
    }
}
