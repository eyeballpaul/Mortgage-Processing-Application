using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PD.Mortgages.Api.Security;

public sealed class DevelopmentCustomerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevelopmentCustomer";
    public const string HeaderName = "X-Development-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = configuration["MortgageApi:DevelopmentKey"];
        var supplied = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid development customer credential."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "development-customer")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
