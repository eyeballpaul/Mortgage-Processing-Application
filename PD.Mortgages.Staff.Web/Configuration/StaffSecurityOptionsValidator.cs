using Microsoft.Extensions.Options;
using PD.Mortgages.Application.Security;

namespace PD.Mortgages.Staff.Web.Configuration;

public sealed class StaffSecurityOptionsValidator : IValidateOptions<ApplicationSecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, ApplicationSecurityOptions options) =>
        options.RequireAuthenticatedUser
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("The staff portal must require an authenticated user.");
}
