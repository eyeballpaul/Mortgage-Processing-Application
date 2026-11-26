using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Client;
using PD.Mortgages.Staff.Web.Controllers;
using PD.Mortgages.Staff.Web.Security;

namespace PD.Mortgages.Tests;

public sealed class StaffConcurrencyWebTests
{
    [Fact]
    public async Task StartingStaffDraftRequiresPostAndAntiforgeryToken()
    {
        var transport = new ConflictHandler();
        using var factory = CreateFactory(transport);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/applications/start")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/applications/start", null)).StatusCode);
        Assert.Equal(0, transport.CreationCount);
        var page = await client.GetStringAsync("/account/access-denied");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var response = await client.PostAsync("/applications/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, transport.CreationCount);
    }

    [Fact]
    public async Task SubmittedViewShowsSnapshotWithoutFetchingLiveProducts()
    {
        var transport = new ConflictHandler { Submitted = true };
        using var factory = CreateFactory(transport);
        using var client = factory.CreateClient();

        var page = await client.GetStringAsync($"/applications/{transport.Id}");

        Assert.Contains("Historic fixed", page);
        Assert.Contains("4.25%", page);
        Assert.Equal(0, transport.ProductLookupCount);
    }

    [Fact]
    public async Task StaffFormCarriesVersionAndReportsConflict()
    {
        var transport = new ConflictHandler();
        using var factory = CreateFactory(transport);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var path = $"/applications/{transport.Id}/step/1";
        var page = await client.GetStringAsync(path);
        Assert.Contains("name=\"ExpectedVersion\"", page);
        Assert.Contains("value=\"7\"", page);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var response = await client.PostAsync(path, new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("ExpectedVersion", "7"),
            new("PropertyValue", "300000"), new("Deposit", "30000")
        ]));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Your changes were not saved", await response.Content.ReadAsStringAsync());
        Assert.Equal("7", transport.Version);
    }

    [Fact]
    public async Task StaffAjaxAssessmentCarriesVersionAndReportsConflict()
    {
        var transport = new ConflictHandler();
        using var factory = CreateFactory(transport);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var page = await client.GetStringAsync($"/applications/{transport.Id}/step/5");
        Assert.Contains("name=\"ExpectedVersion\"", page);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/applications/{transport.Id}/decision-in-principle")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("expectedVersion", "7")])
        };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed elsewhere", await response.Content.ReadAsStringAsync());
        Assert.Equal("7", transport.Version);
    }

    private static WebApplicationFactory<ApplicationWizardController> CreateFactory(ConflictHandler transport) =>
        new WebApplicationFactory<ApplicationWizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("AzureAd:TenantId", "11111111-1111-1111-1111-111111111111");
            builder.UseSetting("AzureAd:ClientId", "22222222-2222-2222-2222-222222222222");
            builder.UseSetting("AzureAd:ClientSecret", "test-secret");
            builder.UseSetting("MortgageApi:BaseUrl", "https://localhost:7270/");
            builder.UseSetting("MortgageApi:Scope", "api://22222222-2222-2222-2222-222222222222/Mortgage.Staff");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:ClientId"] = "22222222-2222-2222-2222-222222222222",
                ["AzureAd:ClientSecret"] = "test-secret",
                ["MortgageApi:Scope"] = "api://22222222-2222-2222-2222-222222222222/Mortgage.Staff"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.RemoveAll<IMortgageApiCredentials>();
                services.AddScoped<IMortgageApiCredentials, TestCredentials>();
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport);
            });
        });

    private sealed class TestCredentials : IMortgageApiCredentials
    {
        public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.Name, "Staff"),
                new Claim(ClaimTypes.Role, StaffAuthorization.StaffRole),
                new Claim(StaffAuthorization.TenantIdClaim, "tenant"),
                new Claim(StaffAuthorization.ObjectIdClaim, "employee")
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class ConflictHandler : HttpMessageHandler
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string? Version { get; private set; }
        public bool Submitted { get; init; }
        public int CreationCount { get; private set; }
        public int ProductLookupCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/staff", StringComparison.Ordinal))
                CreationCount++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/eligible-products", StringComparison.Ordinal))
            {
                ProductLookupCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            if (request.Method == HttpMethod.Put || request.RequestUri!.AbsolutePath.EndsWith("/decision", StringComparison.Ordinal))
            {
                Version = request.Headers.GetValues("X-Expected-Version").Single();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"type\":\"urn:pd-mortgages:concurrency-conflict\",\"status\":409}", Encoding.UTF8, "application/problem+json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"id\":\"{Id}\",\"version\":7,\"status\":{(Submitted ? 1 : 0)},\"documents\":[],\"propertyValue\":300000,\"deposit\":30000,\"firstName\":\"Jane\",\"lastName\":\"Doe\",\"email\":\"jane@example.com\",\"phoneNumber\":\"07123456789\",\"annualIncome\":100000,\"monthlyCommitments\":1000,\"employmentType\":0,\"consentsRecorded\":true,\"selectedProductId\":\"fixed-2\",\"selectedProduct\":{{\"id\":\"fixed-2\",\"name\":\"Historic fixed\",\"interestRate\":4.25,\"fee\":999,\"aprc\":5,\"fixedTermYears\":2,\"maximumLoanToValue\":90,\"catalogVersion\":\"original\"}}}}", Encoding.UTF8, "application/json")
            });
        }
    }
}
