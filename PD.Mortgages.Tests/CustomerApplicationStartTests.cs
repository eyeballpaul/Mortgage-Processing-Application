using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Client;
using PD.Mortgages.Web.Controllers;

namespace PD.Mortgages.Tests;

public sealed class CustomerApplicationStartTests
{
    [Fact]
    public async Task ResponseCorrelationUsesIncomingW3CTraceId()
    {
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/?sensitive=fake-test-secret");
        request.Headers.Add("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        using var response = await client.SendAsync(request);
        Assert.Equal("0123456789abcdef0123456789abcdef", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task ReadinessFailsWhenCustomerSessionStoreIsUnavailable()
    {
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDistributedCache>();
                services.AddSingleton<IDistributedCache, UnavailableCache>();
                services.AddHttpClient("MortgageApiHealth").ConfigurePrimaryHttpMessageHandler(() => new HealthyApiHandler());
            });
        });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostSubmissionResponseCanBeRecoveredWithoutBrowsing(bool returnHome)
    {
        var transport = new LostSubmissionHandler();
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MortgageApi:DevelopmentKey"] = "test-development-key-01234567890123456789"
                }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Regex.Match(await client.GetStringAsync("/"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var start = await client.PostAsync("/application/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        using var documents = await client.GetAsync("/application/documents");
        Assert.Equal(HttpStatusCode.OK, documents.StatusCode);
        var review = await client.GetStringAsync("/application/review");
        token = Regex.Match(review, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var first = await client.PostAsync("/application/submit",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("expectedVersion", "7")]));
        Assert.Contains("could not confirm", await first.Content.ReadAsStringAsync());

        using var recovery = returnHome
            ? await client.GetAsync("/")
            : await client.PostAsync("/application/submit",
                new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("expectedVersion", "7")]));
        Assert.Equal("/application/submitted", recovery.Headers.Location?.OriginalString);
        Assert.Contains(transport.Id.ToString(), await client.GetStringAsync("/application/submitted"));
        Assert.Equal(returnHome ? 1 : 2, transport.SubmissionAttempts);
    }

    [Fact]
    public async Task MissingReceiptRouteDoesNotDiscardPendingSubmission()
    {
        var transport = new LostSubmissionHandler { ReceiptRouteUnavailable = true };
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MortgageApi:DevelopmentKey"] = "test-development-key-01234567890123456789"
                }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Regex.Match(await client.GetStringAsync("/"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var start = await client.PostAsync("/application/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        using var documents = await client.GetAsync("/application/documents");
        Assert.Equal(HttpStatusCode.OK, documents.StatusCode);
        var review = await client.GetStringAsync("/application/review");
        token = Regex.Match(review, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var submit = await client.PostAsync("/application/submit",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("expectedVersion", "7")]));

        var home = await client.GetStringAsync("/");
        Assert.Contains("check your saved application", home);
        transport.ReceiptRouteUnavailable = false;
        using var recovered = await client.GetAsync("/");
        Assert.Equal("/application/submitted", recovered.Headers.Location?.OriginalString);
        Assert.Contains(transport.Id.ToString(), await client.GetStringAsync("/application/submitted"));
    }

    [Fact]
    public async Task SubmissionValidationErrorReturnsToReview()
    {
        var transport = new LostSubmissionHandler { RejectSubmission = true };
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MortgageApi:DevelopmentKey"] = "test-development-key-01234567890123456789"
                }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Regex.Match(await client.GetStringAsync("/"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var start = await client.PostAsync("/application/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        using var documents = await client.GetAsync("/application/documents");
        var review = await client.GetStringAsync("/application/review");
        token = Regex.Match(review, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var response = await client.PostAsync("/application/submit",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("expectedVersion", "7")]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Review the applicant details", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StartingRequiresPostAndAntiforgeryTokenAndIsRateLimited()
    {
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var get = await client.GetAsync("/application/start");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);

        for (var index = 0; index < 5; index++)
        {
            using var invalidPost = await client.PostAsync("/application/start", null);
            Assert.Equal(HttpStatusCode.BadRequest, invalidPost.StatusCode);
        }

        using var limitedPost = await client.PostAsync("/application/start", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedPost.StatusCode);
    }

    [Fact]
    public async Task StartingTwiceInTheSameSessionDoesNotCreateAnotherDraft()
    {
        var transport = new CreateApplicationHandler();
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MortgageApi:DevelopmentKey"] = "test-development-key-01234567890123456789"
                }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);

        async Task<HttpResponseMessage> StartAsync() => await client.PostAsync("/application/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", match.Groups[1].Value)]));

        using var first = await StartAsync();
        using var second = await StartAsync();
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal("/application/resume", second.Headers.Location?.OriginalString);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task SaveProgressReadsTheDraftBeforeUpdatingSessionProgress()
    {
        var transport = new CreateApplicationHandler();
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MortgageApi:DevelopmentKey"] = "test-development-key-01234567890123456789"
                }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var start = await client.PostAsync("/application/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        using var save = await client.PostAsync("/application/save-progress",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("step", "3")]));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal("/application/finances", save.Headers.Location?.OriginalString);
        Assert.Equal(1, transport.ReadCount);
    }

    [Fact]
    public async Task HomeAndResumeRefreshDraftAndOnlyClearUnavailableSessions()
    {
        var transport = new CreateApplicationHandler();
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MortgageApi:DevelopmentKey"] = "test-development-key-01234567890123456789"
                }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Regex.Match(await client.GetStringAsync("/"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var start = await client.PostAsync("/application/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Contains("Resume your application", await client.GetStringAsync("/"));
        using var resume = await client.GetAsync("/application/resume");
        Assert.Equal("/application/qualification", resume.Headers.Location?.OriginalString);
        Assert.Equal(2, transport.ReadCount);

        transport.ReadStatus = HttpStatusCode.Unauthorized;
        var temporaryFailure = await client.GetStringAsync("/");
        Assert.Contains("Resume your application", temporaryFailure);
        Assert.Contains("check your saved application", temporaryFailure);
        using var failedResume = await client.GetAsync("/application/resume");
        Assert.Equal("/", failedResume.Headers.Location?.OriginalString);

        transport.ReadStatus = HttpStatusCode.Forbidden;
        var stale = await client.GetStringAsync("/");
        Assert.DoesNotContain("Resume your application", stale);
    }

    [Fact]
    public async Task StaleCustomerFormShowsConflictAndSendsRenderedVersion()
    {
        var transport = new ConflictApplicationHandler();
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MortgageApi:DevelopmentKey"] = "test-development-key-01234567890123456789"
                }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var home = await client.GetStringAsync("/");
        var token = Regex.Match(home, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var start = await client.PostAsync("/application/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        var form = await client.GetStringAsync("/application/qualification");
        Assert.Contains("name=\"expectedVersion\" value=\"7\"", form);
        token = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var save = await client.PostAsync("/application/qualification",
            new FormUrlEncodedContent([
                new("__RequestVerificationToken", token), new("expectedVersion", "7"),
                new("PropertyValue", "300000"), new("Deposit", "30000")
            ]));

        Assert.Equal(HttpStatusCode.Conflict, save.StatusCode);
        Assert.Contains("Your changes were not saved", await save.Content.ReadAsStringAsync());
        Assert.Equal("7", transport.Version);
    }

    [Fact]
    public async Task StaleAjaxAssessmentReturnsAnUnderstandableConflict()
    {
        var transport = new ConflictApplicationHandler();
        using var factory = new WebApplicationFactory<WizardController>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MortgageApi:DevelopmentKey"] = "test-development-key-01234567890123456789"
                }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<IMortgageApplicationService, MortgageApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var home = await client.GetStringAsync("/");
        var token = Regex.Match(home, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var start = await client.PostAsync("/application/start",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        var page = await client.GetStringAsync("/application/decision-in-principle");
        Assert.Contains("name=\"expectedVersion\" value=\"7\"", page);
        token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/application/decision-in-principle/assess")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("expectedVersion", "7")])
        };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("changed elsewhere", await response.Content.ReadAsStringAsync());
        Assert.Equal("7", transport.Version);
    }

    private sealed class HealthyApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class UnavailableCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new IOException("Session store unavailable.");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new IOException("Session store unavailable.");
        public void Refresh(string key) => throw new IOException("Session store unavailable.");
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new IOException("Session store unavailable.");
        public void Remove(string key) => throw new IOException("Session store unavailable.");
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new IOException("Session store unavailable.");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new IOException("Session store unavailable.");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new IOException("Session store unavailable.");
    }

    private sealed class LostSubmissionHandler : HttpMessageHandler
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int SubmissionAttempts { get; private set; }
        public bool RejectSubmission { get; init; }
        public bool ReceiptRouteUnavailable { get; set; }
        private Guid? submissionKey;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customer-submission", StringComparison.Ordinal))
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var key = body.RootElement.GetProperty("submissionKey").GetGuid();
                if (submissionKey is not null) Assert.Equal(submissionKey, key);
                submissionKey = key;
                SubmissionAttempts++;
                if (RejectSubmission)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"status\":400,\"detail\":\"Review the applicant details\"}", Encoding.UTF8, "application/problem+json")
                    };
                if (SubmissionAttempts == 1) throw new HttpRequestException("Response was lost.");
                return Receipt();
            }
            if (path.EndsWith("/submission-receipt", StringComparison.Ordinal))
                return submissionKey is null || ReceiptRouteUnavailable ? new HttpResponseMessage(HttpStatusCode.NotFound) : Receipt();
            var json = $"{{\"id\":\"{Id}\",\"version\":7,\"documents\":[],\"propertyValue\":300000,\"deposit\":30000,\"firstName\":\"Jane\",\"lastName\":\"Doe\",\"email\":\"jane@example.com\",\"phoneNumber\":\"07123456789\",\"annualIncome\":100000,\"monthlyCommitments\":1000,\"employmentType\":0,\"consentsRecorded\":true,\"selectedProductId\":\"fixed-2\",\"decisionInPrinciple\":{{\"outcome\":0,\"requestedLoanAmount\":270000,\"assessedAtUtc\":\"2026-01-01T00:00:00Z\"}}}}";
            return new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private HttpResponseMessage Receipt() => new(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"reference\":\"{Id}\",\"submittedAt\":\"2026-01-01T00:00:00Z\"}}", Encoding.UTF8, "application/json")
        };
    }

    private sealed class ConflictApplicationHandler : HttpMessageHandler
    {
        private readonly Guid id = Guid.NewGuid();
        public string? Version { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Put || request.RequestUri!.AbsolutePath.EndsWith("/decision", StringComparison.Ordinal))
            {
                Version = request.Headers.GetValues("X-Expected-Version").Single();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"type\":\"urn:pd-mortgages:concurrency-conflict\",\"status\":409}", Encoding.UTF8, "application/problem+json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"id\":\"{id}\",\"version\":7,\"documents\":[],\"propertyValue\":300000,\"deposit\":30000,\"firstName\":\"Jane\",\"lastName\":\"Doe\",\"email\":\"jane@example.com\",\"phoneNumber\":\"07123456789\",\"annualIncome\":100000,\"monthlyCommitments\":1000,\"employmentType\":0,\"consentsRecorded\":true}}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CreateApplicationHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public int ReadCount { get; private set; }
        public HttpStatusCode ReadStatus { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Method == HttpMethod.Get) ReadCount++;
            if (request.Method == HttpMethod.Get && ReadStatus != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(ReadStatus)
                {
                    Content = new StringContent("{\"type\":\"urn:pd-mortgages:application-unavailable\"}", Encoding.UTF8, "application/problem+json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    $"{{\"id\":\"{Guid.NewGuid()}\",\"documents\":[]}}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
