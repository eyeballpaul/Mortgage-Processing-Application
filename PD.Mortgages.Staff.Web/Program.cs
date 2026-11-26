using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PD.Mortgages.Client;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Staff.Web.Configuration;
using PD.Mortgages.Staff.Web.Security;
using PD.Shared;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
builder.Services.AddOptionalTracing(builder.Environment, "staff-web");

if (!Guid.TryParse(builder.Configuration["AzureAd:TenantId"], out _) ||
    !Guid.TryParse(builder.Configuration["AzureAd:ClientId"], out _) ||
    string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientSecret"]))
{
    throw new InvalidOperationException(
        "Single-tenant Entra ID settings are required. Configure GUID values for AzureAd:TenantId and AzureAd:ClientId, plus AzureAd:ClientSecret, using User Secrets locally or secure environment settings in Azure.");
}
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:BlobUri"]))
{
    throw new InvalidOperationException("DataProtection:BlobUri is required outside Development.");
}

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AuthorizeForScopesAttribute { ScopeKeySection = "MortgageApi:Scope" }));
builder.Services.AddProblemDetails();
builder.Services.AddAzureBlobDataProtection(builder.Configuration);
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();
builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "__Host-PDMortgages.Staff";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.AccessDeniedPath = "/account/access-denied";
});
builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.MapInboundClaims = true;
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = StaffAuthorization.RoleClaimType;
    var mortgageScope = builder.Configuration["MortgageApi:Scope"];
    if (!string.IsNullOrWhiteSpace(mortgageScope))
    {
        options.Scope.Add(mortgageScope);
    }
});
builder.Services.AddAuthorization(options =>
{
    var staffPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(StaffAuthorization.StaffRole)
        .RequireClaim(StaffAuthorization.TenantIdClaim)
        .RequireClaim(StaffAuthorization.ObjectIdClaim)
        .Build();
    options.DefaultPolicy = staffPolicy;
    options.FallbackPolicy = staffPolicy;
});
builder.Services.AddHealthChecks()
    .AddCheck<MortgageApiHealthCheck>("mortgage-api", tags: ["ready"]);
builder.Services.AddOptions<ApplicationSecurityOptions>()
    .BindConfiguration(ApplicationSecurityOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IDocumentUploadValidator, DocumentUploadValidator>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IValidateOptions<ApplicationSecurityOptions>, StaffSecurityOptionsValidator>();
builder.Services.AddScoped<IMortgageApiCredentials, StaffMortgageApiCredentials>();
builder.Services.AddMortgageApiClient(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets().AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=Applications}/{action=Index}/{id?}");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program { }
