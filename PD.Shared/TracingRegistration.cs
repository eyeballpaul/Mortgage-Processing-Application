using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PD.Shared;

public static class TracingRegistration
{
    public static void AddOptionalTracing(this IServiceCollection services, IHostEnvironment environment, string serviceName)
    {
        var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !environment.IsDevelopment())) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("The OTLP endpoint must be an absolute HTTPS URL without embedded credentials (HTTP is allowed in Development).");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)))
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddProcessor(new RemoveUrlDetailsProcessor())
                .AddOtlpExporter());
    }

    private sealed class RemoveUrlDetailsProcessor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity)
        {
            foreach (var tag in new[] { "url.full", "url.path", "url.query", "http.url", "http.target" })
                activity.SetTag(tag, null);
        }
    }
}
