using Microsoft.AspNetCore.Http;

namespace PD.Mortgages.Infrastructure.Products;

public sealed class ProductCorrelationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        return base.SendAsync(request, cancellationToken);
    }
}
