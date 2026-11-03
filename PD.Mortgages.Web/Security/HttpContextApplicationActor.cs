using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PD.Mortgages.Application.Security;

namespace PD.Mortgages.Web.Security;

public sealed class HttpContextApplicationActor(IHttpContextAccessor httpContextAccessor) : IApplicationActor
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public string? SubjectId =>
        User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User?.FindFirstValue("sub");
}
