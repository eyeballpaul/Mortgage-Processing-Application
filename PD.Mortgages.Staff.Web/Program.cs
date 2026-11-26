using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Staff.Web.Configuration;
using PD.Mortgages.Staff.Web.Health;
using PD.Mortgages.Staff.Web.Security;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});

if (!Guid.TryParse(builder.Configuration["AzureAd:TenantId"], out _) ||
    !Guid.TryParse(builder.Configuration["AzureAd:ClientId"], out _) ||
    string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientSecret"]))
{
    throw new InvalidOperationException(
        "Single-tenant Entra ID settings are required. Configure GUID values for AzureAd:TenantId and AzureAd:ClientId, plus AzureAd:ClientSecret, using User Secrets locally or secure environment settings in Azure.");
}

builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services.AddAzureBlobDataProtection(builder.Configuration);
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
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
    .AddCheck<ApplicationHealthCheck>("application", tags: ["ready"]);
builder.Services.AddOptions<ApplicationSecurityOptions>()
    .BindConfiguration(ApplicationSecurityOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddOptions<MortgageWorkflowOptions>()
    .BindConfiguration(MortgageWorkflowOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConsentTermsVersion), "MortgageWorkflow:ConsentTermsVersion is required.");
builder.Services.AddSingleton<IDocumentUploadValidator, DocumentUploadValidator>();

builder.Services.AddMortgagePersistence(builder.Configuration);

if (bool.TryParse(builder.Configuration["ProductService:UseMock"], out var useMockProductService) && useMockProductService)
{
    builder.Services.AddSingleton<LocalProductCatalogFallback>();
    builder.Services.AddSingleton<IProductCatalogClient, MockProductCatalogClient>();
}
else
{
    builder.Services.AddTransient<ProductServiceResilienceHandler>();
    builder.Services.AddTransient<ProductCorrelationHandler>();
    builder.Services.AddHttpClient<IProductCatalogClient, ProductCatalogClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["ProductService:BaseUrl"] ?? "http://localhost:5260/");
        client.Timeout = TimeSpan.FromSeconds(10);
        var apiKey = builder.Configuration["ProductService:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Product-Service-Key", apiKey);
        }
    }).AddHttpMessageHandler<ProductCorrelationHandler>()
      .AddHttpMessageHandler<ProductServiceResilienceHandler>();
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IApplicationActor, EntraStaffApplicationActor>();
builder.Services.AddSingleton<IValidateOptions<ApplicationSecurityOptions>, StaffSecurityOptionsValidator>();
builder.Services.AddScoped<IMortgageApplicationService, MortgageApplicationService>();

var app = builder.Build();
await app.Services.MigrateMortgageDatabaseAsync();
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
