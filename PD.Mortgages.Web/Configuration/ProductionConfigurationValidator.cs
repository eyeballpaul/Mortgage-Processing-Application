using Microsoft.Extensions.Options;
using PD.Mortgages.Application.Security;

namespace PD.Mortgages.Web.Configuration;

public sealed class ProductionConfigurationValidator(
    IHostEnvironment environment,
    IConfiguration configuration) : IValidateOptions<ApplicationSecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, ApplicationSecurityOptions options)
    {
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.Equals(configuration["Session:Provider"], "Redis", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            failures.Add("ConnectionStrings:Redis is required when Session:Provider is Redis.");
        }

        if (!options.RequireAuthenticatedUser)
        {
            failures.Add("ApplicationSecurity:RequireAuthenticatedUser must be true outside Development.");
        }

        if (string.IsNullOrWhiteSpace(configuration["ProductService:BaseUrl"]))
        {
            failures.Add("ProductService:BaseUrl is required outside Development.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
