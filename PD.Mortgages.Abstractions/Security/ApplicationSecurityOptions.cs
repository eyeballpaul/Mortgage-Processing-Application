namespace PD.Mortgages.Application.Security;

public sealed class ApplicationSecurityOptions
{
    public const string SectionName = "ApplicationSecurity";

    public bool RequireAuthenticatedUser { get; set; }
}
