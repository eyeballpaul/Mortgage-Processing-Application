extern alias MortgageApi;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Client;
using PD.Mortgages.Contracts;
using PD.Mortgages.Domain.Types.Enums;
using PD.Mortgages.Infrastructure.Eligibility;

namespace PD.Mortgages.Tests;

public sealed class MortgageApiIntegrationTests : IClassFixture<MortgageApiIntegrationTests.ApiFactory>
{
    private readonly ApiFactory factory;
    public MortgageApiIntegrationTests(ApiFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomerAndStaffDecisionCallsEligibilityApi(bool staff)
    {
        using var eligibility = new EligibilityApiTests.ApiFactory();
        using var eligibilityHttp = eligibility.Authorized();
        using var mortgage = new ApiFactory
        {
            EligibilityOverride = new EligibilityClient(eligibilityHttp, NullLogger<EligibilityClient>.Instance)
        };
        using var http = staff ? mortgage.Staff() : mortgage.Customer(Guid.NewGuid());
        var client = new MortgageApiClient(http, new ExistingHeadersCredentials());
        var application = staff ? await client.CreateForStaffAsync(TestContext.Current.CancellationToken) : await client.CreateAsync(TestContext.Current.CancellationToken);
        await client.SaveQualificationAsync(application.Id, 300000, 30000, application.Version, TestContext.Current.CancellationToken);
        application = await client.GetAsync(application.Id, TestContext.Current.CancellationToken);
        await client.SaveFinancesAsync(application.Id, 100000, 1000, EmploymentType.Employed, application.Version, TestContext.Current.CancellationToken);
        application = await client.GetAsync(application.Id, TestContext.Current.CancellationToken);
        await client.SaveConsentsAsync(application.Id, false, application.Version, TestContext.Current.CancellationToken);
        application = await client.GetAsync(application.Id, TestContext.Current.CancellationToken);

        var result = await client.GenerateDecisionInPrincipleAsync(application.Id, application.Version, TestContext.Current.CancellationToken);
        Assert.Equal(DecisionInPrincipleOutcome.CreditCheckNotSelected, result.DecisionInPrinciple!.Outcome);
        Assert.Equal(270000, result.DecisionInPrinciple.RequestedLoanAmount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EligibilityOutageReturnsUnavailableWithoutSavingDecision(bool staff)
    {
        using var mortgage = new ApiFactory { EligibilityOverride = new UnavailableEligibilityClient() };
        using var http = staff ? mortgage.Staff() : mortgage.Customer(Guid.NewGuid());
        var client = new MortgageApiClient(http, new ExistingHeadersCredentials());
        var application = staff ? await client.CreateForStaffAsync(TestContext.Current.CancellationToken) : await client.CreateAsync(TestContext.Current.CancellationToken);
        await client.SaveQualificationAsync(application.Id, 300000, 30000, application.Version, TestContext.Current.CancellationToken);
        application = await client.GetAsync(application.Id, TestContext.Current.CancellationToken);
        await client.SaveFinancesAsync(application.Id, 100000, 1000, EmploymentType.Employed, application.Version, TestContext.Current.CancellationToken);
        application = await client.GetAsync(application.Id, TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GenerateDecisionInPrincipleAsync(application.Id, application.Version, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        var unchanged = await client.GetAsync(application.Id, TestContext.Current.CancellationToken);
        Assert.Equal(application.Version, unchanged.Version);
        Assert.Null(unchanged.DecisionInPrinciple);
    }

    private sealed class UnavailableEligibilityClient : IEligibilityClient
    {
        public Task<PD.Mortgages.Domain.Entities.DecisionInPrincipleResult> AssessAsync(decimal propertyValue,
            decimal deposit, decimal annualIncome, bool creditCheckConsent, CancellationToken cancellationToken) =>
            throw new EligibilityUnavailableException();
    }

    [Fact]
    public async Task ApiRejectsInvalidApplicantAndFinancialDetailsWithoutChangingDraft()
    {
        using var http = factory.Customer(Guid.NewGuid());
        var client = new MortgageApiClient(http, new ExistingHeadersCredentials());
        var created = await client.CreateAsync(TestContext.Current.CancellationToken);
        http.DefaultRequestHeaders.Add("X-Expected-Version", created.Version.ToString());
        var root = $"/api/v1/mortgages/{created.Id}";

        using var applicant = await http.PutAsJsonAsync($"{root}/applicant", new SaveApplicantRequest("Jane", "Doe", "invalid", "07123456789"), cancellationToken: TestContext.Current.CancellationToken);
        using var qualification = await http.PutAsJsonAsync($"{root}/qualification", new SaveQualificationRequest(1, 0), cancellationToken: TestContext.Current.CancellationToken);
        using var finances = await http.PutAsJsonAsync($"{root}/finances", new SaveFinancesRequest(100000, 1000, (EmploymentTypeContract)99), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, applicant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, qualification.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, finances.StatusCode);
        var unchanged = await client.GetAsync(created.Id, TestContext.Current.CancellationToken);
        Assert.Equal(created.Version, unchanged.Version);
        Assert.Null(unchanged.FirstName);
    }

    [Fact]
    public async Task MutationsRequireVersionAndRejectStaleCustomerAndStaffEdits()
    {
        using var ownerHttp = factory.Customer(Guid.NewGuid());
        using var staffHttp = factory.Staff();
        var owner = new MortgageApiClient(ownerHttp, new ExistingHeadersCredentials());
        var staff = new MortgageApiClient(staffHttp, new ExistingHeadersCredentials());
        var created = await owner.CreateAsync(TestContext.Current.CancellationToken);
        var root = $"/api/v1/mortgages/{created.Id}";
        var request = new SaveQualificationRequest(300000, 30000);

        using var missing = await ownerHttp.PutAsJsonAsync($"{root}/qualification", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        ownerHttp.DefaultRequestHeaders.Add("X-Expected-Version", "not-a-version");
        using var invalid = await ownerHttp.PutAsJsonAsync($"{root}/qualification", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        ownerHttp.DefaultRequestHeaders.Remove("X-Expected-Version");

        var staffView = await staff.GetForStaffAsync(created.Id, TestContext.Current.CancellationToken);
        Assert.Equal(created.Version, staffView.Version);
        await owner.SaveQualificationAsync(created.Id, 300000, 30000, created.Version, TestContext.Current.CancellationToken);
        var updated = await owner.GetAsync(created.Id, TestContext.Current.CancellationToken);
        Assert.True(updated.Version > created.Version);
        await Assert.ThrowsAsync<PD.Mortgages.Application.Services.MortgageConcurrencyException>(() =>
            staff.SaveQualificationAsync(created.Id, 400000, 40000, staffView.Version, TestContext.Current.CancellationToken));
        ownerHttp.DefaultRequestHeaders.Add("X-Expected-Version", created.Version.ToString());
        using var stale = await ownerHttp.PutAsJsonAsync($"{root}/qualification", request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MortgageApiProblems.ConcurrencyConflict, problem!.Type);
        Assert.Equal(300000, (await owner.GetAsync(created.Id, TestContext.Current.CancellationToken)).PropertyValue);
        var audit = await staff.GetAuditTrailForStaffAsync(created.Id, 1, 25, TestContext.Current.CancellationToken);
        Assert.Single(audit.Events, entry => entry.Action == "Qualification updated");
    }

    [Fact]
    public async Task DeleteRejectsStaleVersionAndRemovesOnlyTheOwnedDraft()
    {
        using var ownerHttp = factory.Customer(Guid.NewGuid());
        using var otherHttp = factory.Customer(Guid.NewGuid());
        var owner = new MortgageApiClient(ownerHttp, new ExistingHeadersCredentials());
        var created = await owner.CreateAsync(TestContext.Current.CancellationToken);
        await owner.SaveQualificationAsync(created.Id, 300000, 30000, created.Version, TestContext.Current.CancellationToken);
        var current = await owner.GetAsync(created.Id, TestContext.Current.CancellationToken);
        var root = $"/api/v1/mortgages/{created.Id}";

        otherHttp.DefaultRequestHeaders.Add("X-Expected-Version", current.Version.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await otherHttp.DeleteAsync(root, TestContext.Current.CancellationToken)).StatusCode);
        ownerHttp.DefaultRequestHeaders.Add("X-Expected-Version", created.Version.ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await ownerHttp.DeleteAsync(root, TestContext.Current.CancellationToken)).StatusCode);
        ownerHttp.DefaultRequestHeaders.Remove("X-Expected-Version");
        ownerHttp.DefaultRequestHeaders.Add("X-Expected-Version", current.Version.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await ownerHttp.DeleteAsync(root, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerHttp.GetAsync(root, TestContext.Current.CancellationToken)).StatusCode);
        using var staff = factory.Staff();
        var audit = await new MortgageApiClient(staff, new ExistingHeadersCredentials()).GetAuditTrailForStaffAsync(created.Id, 1, 25, TestContext.Current.CancellationToken);
        Assert.Contains(audit.Events, entry => entry.Action == "Draft deleted");
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerHttp.GetAsync($"/api/v1/mortgages/staff/{created.Id}/audit", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/mortgages/staff/{Guid.NewGuid()}/audit", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task DraftIsSessionBoundAndOnlyStaffCanReadAfterSubmission()
    {
        using var owner = factory.Customer(Guid.NewGuid());
        using var other = factory.Customer(Guid.NewGuid());
        using var staff = factory.Staff();
        using var created = await owner.PostAsync("/api/v1/mortgages", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var application = await created.Content.ReadFromJsonAsync<MortgageApplicationDto>(factory.JsonOptions, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(application);
        var root = $"/api/v1/mortgages/{application.Id}";
        async Task SetCurrentVersion()
        {
            var current = await owner.GetFromJsonAsync<MortgageApplicationDto>(root, factory.JsonOptions);
            owner.DefaultRequestHeaders.Remove("X-Expected-Version");
            owner.DefaultRequestHeaders.Add("X-Expected-Version", current!.Version.ToString());
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(root, TestContext.Current.CancellationToken)).StatusCode);
        await SetCurrentVersion();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{root}/qualification", new SaveQualificationRequest(300000, 30000), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        await SetCurrentVersion();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{root}/applicant", new SaveApplicantRequest("Jane", "Doe", "jane@example.com", "07123456789"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        await SetCurrentVersion();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{root}/finances", new { annualIncome = 100000, monthlyCommitments = 1000, employmentType = "Employed" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        await SetCurrentVersion();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{root}/consents", new SaveConsentsRequest(false), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        await SetCurrentVersion();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"{root}/decision", null, TestContext.Current.CancellationToken)).StatusCode);
        await SetCurrentVersion();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{root}/product", new SelectProductRequest("fixed-2"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        var wireApplication = await owner.GetFromJsonAsync<ApplicationResponse>(root, factory.JsonOptions, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(wireApplication);
        Assert.Equal(ApplicationStatusContract.InProgress, wireApplication.Status);
        Assert.Equal(EmploymentTypeContract.Employed, wireApplication.EmploymentType);
        Assert.Equal(DecisionOutcomeContract.CreditCheckNotSelected, wireApplication.DecisionInPrinciple!.Outcome);
        Assert.Equal("fixed-2", wireApplication.SelectedProduct!.Id);
        var mappedApplication = await new MortgageApiClient(owner, new ExistingHeadersCredentials()).GetAsync(application.Id, TestContext.Current.CancellationToken);
        Assert.Equal(wireApplication.SelectedProduct.InterestRate, mappedApplication.SelectedProduct!.InterestRate);

        await SetCurrentVersion();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"{root}/documents", new UploadDocumentRequest("bad.pdf", "not a pdf"u8.ToArray()), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        using var uploaded = await owner.PostAsJsonAsync($"{root}/documents", new UploadDocumentRequest("../valid.pdf", "%PDF-1.7"u8.ToArray()), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        Assert.NotNull(uploaded.Headers.Location);
        using var refreshed = await owner.GetAsync(root, TestContext.Current.CancellationToken);
        var draft = await refreshed.Content.ReadFromJsonAsync<MortgageApplicationDto>(factory.JsonOptions, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(draft);
        var document = Assert.Single(draft.Documents);
        Assert.Equal("valid.pdf", document.FileName);
        Assert.Equal("%PDF-1.7", Encoding.UTF8.GetString(await owner.GetByteArrayAsync($"{root}/documents/{document.Id}", TestContext.Current.CancellationToken)));
        for (var index = 0; index < 9; index++)
        {
            await SetCurrentVersion();
            Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"{root}/documents", new UploadDocumentRequest($"valid-{index}.pdf", "%PDF-1.7"u8.ToArray()), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        }
        await SetCurrentVersion();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"{root}/documents", new UploadDocumentRequest("extra.pdf", "%PDF-1.7"u8.ToArray()), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        var submissionKey = Guid.NewGuid();
        var submissions = await Task.WhenAll(
            owner.PostAsJsonAsync($"{root}/customer-submission", new SubmitCustomerRequest(submissionKey), cancellationToken: TestContext.Current.CancellationToken),
            owner.PostAsJsonAsync($"{root}/customer-submission", new SubmitCustomerRequest(submissionKey), cancellationToken: TestContext.Current.CancellationToken));
        using var submitted = submissions[0];
        using var repeated = submissions[1];
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var receipt = await repeated.Content.ReadFromJsonAsync<SubmissionReceiptDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(application.Id, receipt!.Reference);
        other.DefaultRequestHeaders.Add("X-Submission-Key", submissionKey.ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"{root}/submission-receipt", TestContext.Current.CancellationToken)).StatusCode);
        owner.DefaultRequestHeaders.Add("X-Submission-Key", Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{root}/submission-receipt", TestContext.Current.CancellationToken)).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-Submission-Key");
        owner.DefaultRequestHeaders.Add("X-Submission-Key", submissionKey.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"{root}/submission-receipt", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(root, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/v1/mortgages/staff/{application.Id}", TestContext.Current.CancellationToken)).StatusCode);
        var staffClient = new MortgageApiClient(staff, new ExistingHeadersCredentials());
        var search = await staffClient.SearchForStaffAsync(application.Id.ToString(), ApplicationStatus.Submitted, 1, 25, TestContext.Current.CancellationToken);
        Assert.Contains(search.Applications, item => item.Id == application.Id && item.Status == ApplicationStatus.Submitted);
        var audit = await staff.GetFromJsonAsync<MortgageApplicationAuditPageDto>($"/api/v1/mortgages/staff/{application.Id}/audit?page=1&pageSize=25", factory.JsonOptions, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(audit);
        Assert.Single(audit.Events, entry => entry.Action == "Application submitted" && entry.ActorId.StartsWith("session:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StaffRequiresDelegatedScopeRoleAndApprovedCallingApp()
    {
        using var noToken = factory.CreateClient();
        noToken.DefaultRequestHeaders.Add("X-Mortgage-Session", $"session:{Guid.NewGuid():N}");
        noToken.DefaultRequestHeaders.Add("X-Staff-Role", "MortgageStaff");
        Assert.Equal(HttpStatusCode.Unauthorized, (await noToken.GetAsync("/api/v1/mortgages/staff?page=1&pageSize=25", TestContext.Current.CancellationToken)).StatusCode);

        using var customer = factory.Customer(Guid.NewGuid());
        customer.DefaultRequestHeaders.Add("X-Staff-Role", "MortgageStaff");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/mortgages/staff?page=1&pageSize=25", TestContext.Current.CancellationToken)).StatusCode);
        using var wrongCustomer = factory.Customer(Guid.NewGuid(), clientId: Guid.NewGuid().ToString());
        using var delegatedCustomer = factory.Customer(Guid.NewGuid(), delegated: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongCustomer.PostAsync("/api/v1/mortgages", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await delegatedCustomer.PostAsync("/api/v1/mortgages", null, TestContext.Current.CancellationToken)).StatusCode);

        using var missingRole = factory.Staff(role: false);
        using var missingScope = factory.Staff(scope: false);
        using var wrongCaller = factory.Staff(clientId: Guid.NewGuid().ToString());
        foreach (var client in new[] { missingRole, missingScope, wrongCaller })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/mortgages/staff?page=1&pageSize=25", TestContext.Current.CancellationToken)).StatusCode);
        }

        using var staff = factory.Staff();
        using var created = await staff.PostAsync("/api/v1/mortgages/staff", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var application = await created.Content.ReadFromJsonAsync<MortgageApplicationDto>(factory.JsonOptions, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal($"{ApiFactory.TenantId}:{ApiFactory.StaffObjectId}", application!.CreatedByEmployeeId);
        var audit = await staff.GetFromJsonAsync<MortgageApplicationAuditPageDto>($"/api/v1/mortgages/staff/{application.Id}/audit?page=1&pageSize=25", factory.JsonOptions, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal($"{ApiFactory.TenantId}:{ApiFactory.StaffObjectId}", Assert.Single(audit!.Events).ActorId);
    }

    [Fact]
    public async Task DevelopmentCredentialDoesNotAuthorizeStaffOrWorkInProduction()
    {
        using var development = factory.CreateClient();
        development.DefaultRequestHeaders.Add("X-Development-Api-Key", ApiFactory.DevelopmentKey);
        development.DefaultRequestHeaders.Add("X-Mortgage-Session", $"session:{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, (await development.PostAsync("/api/v1/mortgages", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await development.GetAsync("/api/v1/mortgages/staff", TestContext.Current.CancellationToken)).StatusCode);

        using var productionFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["Persistence:RunMigrationsOnStartup"] = "false",
                ["ConnectionStrings:mortgages"] = "Server=localhost;Database=unused;User Id=sa;Password=NotUsed;TrustServerCertificate=True",
                ["DocumentStorage:Provider"] = "AzureBlob",
                ["DocumentStorage:BlobServiceUri"] = "https://example.blob.core.windows.net/",
                ["DocumentStorage:ContainerName"] = "documents",
                ["ProductService:UseMock"] = "false",
                ["ProductService:BaseUrl"] = "https://products.example/",
                ["ProductService:ApiKey"] = "test-key",
                ["EligibilityService:ApiKey"] = "test-eligibility-key-01234567890123456789"
            }));
        });
        using var production = productionFactory.CreateClient();
        production.DefaultRequestHeaders.Add("X-Development-Api-Key", ApiFactory.DevelopmentKey);
        production.DefaultRequestHeaders.Add("X-Mortgage-Session", $"session:{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await production.PostAsync("/api/v1/mortgages", null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public void ProductionApiRejectsInMemoryPersistence()
    {
        using var productionFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var exception = Assert.Throws<InvalidOperationException>(() => productionFactory.CreateClient());
        Assert.Contains("requires SQL Server", exception.Message);
    }

    [Fact]
    public async Task DependencyCheckDetectsProductFailureWithoutBreakingStorageReadiness()
    {
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProductCatalogClient>();
            services.AddSingleton<IProductCatalogClient, UnavailableCatalog>();
        }));
        using var client = failingFactory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/dependencies", TestContext.Current.CancellationToken)).StatusCode);
    }

    private sealed class UnavailableCatalog : IProductCatalogClient
    {
        public Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Products unavailable.");
        public Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Products unavailable.");
        public Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Products unavailable.");
        public Task<bool> IsProductEligibleAsync(string id, decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Products unavailable.");
    }

    [Fact]
    public async Task ClientDistinguishesSessionDenialFromCallerAuthenticationFailureAndMissingDocuments()
    {
        using var ownerHttp = factory.Customer(Guid.NewGuid());
        using var otherHttp = factory.Customer(Guid.NewGuid());
        using var unauthenticatedHttp = factory.CreateClient();
        var owner = new MortgageApiClient(ownerHttp, new ExistingHeadersCredentials());
        var other = new MortgageApiClient(otherHttp, new ExistingHeadersCredentials());
        var unauthenticated = new MortgageApiClient(unauthenticatedHttp, new ExistingHeadersCredentials());
        var application = await owner.CreateAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.GetAsync(application.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<HttpRequestException>(() => unauthenticated.GetAsync(application.Id, TestContext.Current.CancellationToken));
        var documentId = await owner.AddDocumentAsync(application.Id, "test.pdf", "%PDF-1.7"u8.ToArray(), application.Version, TestContext.Current.CancellationToken);
        Assert.Equal("%PDF-1.7", Encoding.UTF8.GetString((await owner.GetDocumentAsync(application.Id, documentId, TestContext.Current.CancellationToken))!));
        Assert.Null(await owner.GetDocumentAsync(application.Id, Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Null(await owner.GetProductAsync("missing-product", TestContext.Current.CancellationToken));
        Assert.Null(await owner.GetSubmissionReceiptAsync(application.Id, Guid.NewGuid(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => owner.GetDocumentAsync(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClientDoesNotRetryNonIdempotentOperations()
    {
        using var handler = new CountingFailureHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var client = new MortgageApiClient(http, new ExistingHeadersCredentials());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SubmitAsync(Guid.NewGuid(), 0, TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Attempts);
    }

    private sealed class ExistingHeadersCredentials : IMortgageApiCredentials
    {
        public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CountingFailureHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    public sealed class ApiFactory : WebApplicationFactory<MortgageApi::Program>
    {
        public IEligibilityClient? EligibilityOverride { get; init; }
        public const string TenantId = "11111111-1111-1111-1111-111111111111";
        public const string StaffObjectId = "22222222-2222-2222-2222-222222222222";
        private const string ApiId = "33333333-3333-3333-3333-333333333333";
        private const string CustomerId = "44444444-4444-4444-4444-444444444444";
        private const string StaffId = "55555555-5555-5555-5555-555555555555";
        private const string Issuer = "https://issuer.example/test";
        public const string DevelopmentKey = "test-development-key-01234567890123456789";
        private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("only-a-test-signing-key-with-at-least-32-bytes"));
        public System.Text.Json.JsonSerializerOptions JsonOptions { get; } = new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = TenantId,
                ["AzureAd:ClientId"] = ApiId,
                ["MortgageApi:CustomerClientId"] = CustomerId,
                ["MortgageApi:StaffClientId"] = StaffId,
                ["MortgageApi:DevelopmentKey"] = DevelopmentKey,
                ["Persistence:Provider"] = "InMemory",
                ["ProductService:UseMock"] = "true"
            }));
            builder.ConfigureServices(services =>
            {
              services.RemoveAll<IEligibilityClient>();
              services.AddSingleton<IEligibilityClient>(EligibilityOverride ?? new TestEligibilityClient());
              services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
              {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.IssuerValidator = null;
                options.TokenValidationParameters.ValidAudience = ApiId;
                options.TokenValidationParameters.ValidAudiences = null;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.IssuerSigningKey = SigningKey;
              });
            });
        }

        public HttpClient Customer(Guid sessionId, string? clientId = null, bool delegated = false)
        {
            var client = CreateClient();
            var claims = new List<Claim>
            {
                new("azp", clientId ?? CustomerId),
                new("roles", "MortgageCustomer")
            };
            if (delegated) claims.Add(new Claim("scp", "Mortgage.Staff"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token([.. claims]));
            client.DefaultRequestHeaders.Add("X-Mortgage-Session", $"session:{sessionId:N}");
            return client;
        }

        public HttpClient Staff(bool role = true, bool scope = true, string? clientId = null)
        {
            var claims = new List<Claim>
            {
                new("azp", clientId ?? StaffId),
                new("tid", TenantId),
                new("oid", StaffObjectId)
            };
            if (role) claims.Add(new Claim("roles", "MortgageStaff"));
            if (scope) claims.Add(new Claim("scp", "Mortgage.Staff"));
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token([.. claims]));
            return client;
        }

        private static string Token(params Claim[] claims) => new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(Issuer, ApiId, claims, expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)));
    }
}
