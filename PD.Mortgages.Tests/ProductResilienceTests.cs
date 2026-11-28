using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using PD.Mortgages.Infrastructure.Products;

namespace PD.Mortgages.Tests;

public sealed class ProductResilienceTests
{
    [Fact]
    public async Task ProductReadsUseLocalFallbackWhenServiceIsUnavailable()
    {
        var transport = new StubHandler((_, _) => throw new HttpRequestException("service unavailable"));
        var resilience = new ProductServiceResilienceHandler(NullLogger<ProductServiceResilienceHandler>.Instance)
        {
            InnerHandler = transport
        };
        var httpClient = new HttpClient(resilience) { BaseAddress = new Uri("http://products.test/") };
        var client = new ProductCatalogClient(httpClient, new LocalProductCatalogFallback());

        var products = await client.GetProductsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, products.Count);
    }

    [Fact]
    public async Task CircuitBreakerShortCircuitsAfterRepeatedTransientFailures()
    {
        var transport = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var resilience = new ProductServiceResilienceHandler(NullLogger<ProductServiceResilienceHandler>.Instance)
        {
            InnerHandler = transport
        };
        using var client = new HttpClient(resilience) { BaseAddress = new Uri("http://products.test/") };

        for (var index = 0; index < 3; index++)
        {
            using var response = await client.GetAsync("api/v1/products", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        using var shortCircuitedResponse = await client.GetAsync("api/v1/products", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, shortCircuitedResponse.StatusCode);
        Assert.Equal(9, transport.RequestCount);
    }

    [Fact]
    public async Task ResilienceLogsDoNotIncludeRequestQueryOrExceptionText()
    {
        var logger = new RecordingLogger();
        var resilience = new ProductServiceResilienceHandler(logger)
        {
            InnerHandler = new StubHandler((_, _) => throw new HttpRequestException("SYNTHETIC_PRIVATE_VALUE"))
        };
        using var client = new HttpClient(resilience) { BaseAddress = new Uri("http://products.test/") };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("api/v1/products/eligible?propertyValue=SYNTHETIC_PRIVATE_VALUE", TestContext.Current.CancellationToken));

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("SYNTHETIC_PRIVATE_VALUE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedTimeoutsOpenCircuitButCallerCancellationDoesNot()
    {
        var transport = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var resilience = new ProductServiceResilienceHandler(
            NullLogger<ProductServiceResilienceHandler>.Instance, TimeSpan.FromMilliseconds(50))
        {
            InnerHandler = transport
        };
        using var client = new HttpClient(resilience) { BaseAddress = new Uri("http://products.test/"), Timeout = Timeout.InfiniteTimeSpan };

        for (var index = 0; index < 3; index++)
        {
            using var callerCancellation = new CancellationTokenSource();
            callerCancellation.CancelAfter(TimeSpan.FromMilliseconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("api/v1/products", callerCancellation.Token));
        }
        Assert.Equal(3, transport.RequestCount);

        for (var index = 0; index < 3; index++)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("api/v1/products", TestContext.Current.CancellationToken));

        using var response = await client.GetAsync("api/v1/products", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(6, transport.RequestCount);
    }

    private sealed class RecordingLogger : ILogger<ProductServiceResilienceHandler>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return send(request, cancellationToken);
        }
    }
}
