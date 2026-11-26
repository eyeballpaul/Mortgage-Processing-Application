extern alias EligibilityApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PD.Eligibility.Contracts;
using PD.Mortgages.Infrastructure.Eligibility;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Tests;

public sealed class EligibilityApiTests : IClassFixture<EligibilityApiTests.ApiFactory>
{
    private readonly ApiFactory factory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public EligibilityApiTests(ApiFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(false, 100000, EligibilityOutcome.CreditCheckNotSelected, null)]
    [InlineData(true, 100000, EligibilityOutcome.WithinIndicativeRange, 450000)]
    [InlineData(true, 50000, EligibilityOutcome.AboveIndicativeRange, 225000)]
    public async Task AssessmentUsesExistingIndicativeRules(bool consent, int income, EligibilityOutcome outcome, int? maximum)
    {
        using var client = factory.Authorized();
        using var response = await client.PostAsJsonAsync("/api/v1/eligibility/assess",
            new EligibilityRequest(300000, 30000, income, consent));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EligibilityResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(270000, result.RequestedLoanAmount);
        Assert.Equal(maximum, result.IndicativeMaximumBorrowing);
        Assert.Equal("1", result.RuleVersion);
    }

    [Fact]
    public async Task ApiRequiresServiceCredentialAndValidInputs()
    {
        using var anonymous = factory.CreateClient();
        var request = new EligibilityRequest(300000, 30000, 100000, true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/eligibility/assess", request)).StatusCode);
        using var authorized = factory.Authorized();
        Assert.Equal(HttpStatusCode.BadRequest, (await authorized.PostAsJsonAsync("/api/v1/eligibility/assess",
            new EligibilityRequest(100, 200, 0, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await authorized.PostAsJsonAsync("/api/v1/eligibility/assess",
            new { propertyValue = 300000, deposit = 30000, annualIncome = 100000 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task MortgageClientCallsAuthenticatedEligibilityApi()
    {
        using var http = factory.Authorized();
        var client = new EligibilityClient(http, NullLogger<EligibilityClient>.Instance);
        var result = await client.AssessAsync(300000, 30000, 100000, false, CancellationToken.None);
        Assert.Equal(DecisionInPrincipleOutcome.CreditCheckNotSelected, result.Outcome);
        Assert.Equal("1", result.RuleVersion);
    }

    [Fact]
    public async Task RepeatedServiceFailuresOpenTheCircuit()
    {
        var transport = new FailingHandler();
        using var http = new HttpClient(new EligibilityResilienceHandler(NullLogger<EligibilityResilienceHandler>.Instance)
        {
            InnerHandler = transport
        }) { BaseAddress = new Uri("http://eligibility.test/"), Timeout = Timeout.InfiniteTimeSpan };
        var client = new EligibilityClient(http, NullLogger<EligibilityClient>.Instance);
        for (var index = 0; index < 4; index++)
            await Assert.ThrowsAsync<EligibilityUnavailableException>(() =>
                client.AssessAsync(300000, 30000, 100000, false, CancellationToken.None));
        Assert.Equal(3, transport.Requests);
    }

    [Fact]
    public async Task ServiceTimeoutsOpenCircuitButCallerCancellationDoesNot()
    {
        var transport = new DelayedHandler();
        using var http = new HttpClient(new EligibilityResilienceHandler(
            NullLogger<EligibilityResilienceHandler>.Instance, TimeSpan.FromMilliseconds(50))
        {
            InnerHandler = transport
        }) { BaseAddress = new Uri("http://eligibility.test/"), Timeout = Timeout.InfiniteTimeSpan };
        var client = new EligibilityClient(http, NullLogger<EligibilityClient>.Instance);
        for (var index = 0; index < 3; index++)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.AssessAsync(300000, 30000, 100000, false, cancellation.Token));
        }
        for (var index = 0; index < 4; index++)
            await Assert.ThrowsAsync<EligibilityUnavailableException>(() =>
                client.AssessAsync(300000, 30000, 100000, false, CancellationToken.None));
        Assert.Equal(6, transport.Requests);
    }

    [Fact]
    public async Task TimeoutAlsoCoversResponseBody()
    {
        using var http = new HttpClient(new EligibilityResilienceHandler(
            NullLogger<EligibilityResilienceHandler>.Instance, TimeSpan.FromMilliseconds(50))
        {
            InnerHandler = new SlowBodyHandler()
        }) { BaseAddress = new Uri("http://eligibility.test/"), Timeout = Timeout.InfiniteTimeSpan };
        var client = new EligibilityClient(http, NullLogger<EligibilityClient>.Instance);
        await Assert.ThrowsAsync<EligibilityUnavailableException>(() =>
            client.AssessAsync(300000, 30000, 100000, false, CancellationToken.None));
    }

    private sealed class SlowBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowContent() });
    }

    private sealed class SlowContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.Delay(Timeout.InfiniteTimeSpan);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    public sealed class ApiFactory : WebApplicationFactory<EligibilityApi::Program>
    {
        private const string Key = "test-eligibility-key-01234567890123456789";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["EligibilityService:ApiKey"] = Key }));
        }

        public HttpClient Authorized()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Eligibility-Service-Key", Key);
            return client;
        }
    }
}
