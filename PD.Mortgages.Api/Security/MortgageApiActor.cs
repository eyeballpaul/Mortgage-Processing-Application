using System.Security.Claims;
using PD.Mortgages.Application.Security;

namespace PD.Mortgages.Api.Security;

public sealed class MortgageApiActor(IHttpContextAccessor accessor, IConfiguration configuration) : IApplicationActor
{
    private HttpContext Context => accessor.HttpContext ?? throw new InvalidOperationException("An HTTP request is required.");
    public bool IsStaff => MortgageCallerAuthorization.IsStaff(Context.User, configuration);
    public bool IsAuthenticated => IsStaff;
    public string? SubjectId
    {
        get
        {
            if (IsStaff &&
                Guid.TryParse(Context.User.FindFirstValue("tid"), out var tenantId) &&
                Guid.TryParse(Context.User.FindFirstValue("oid"), out var objectId))
            {
                return $"{tenantId:D}:{objectId:D}";
            }

            if (MortgageCallerAuthorization.IsCustomer(Context.User, configuration))
            {
                var value = Context.Request.Headers["X-Mortgage-Session"].ToString();
                if (value.StartsWith("session:", StringComparison.Ordinal) &&
                    Guid.TryParseExact(value["session:".Length..], "N", out var sessionId))
                {
                    return $"session:{sessionId:N}";
                }
            }

            return null;
        }
    }
}
