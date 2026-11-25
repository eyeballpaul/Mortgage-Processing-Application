using System.Security.Claims;
using PD.Mortgages.Application.Security;

namespace PD.Mortgages.Staff.Web.Security;

public sealed class EntraStaffApplicationActor(IHttpContextAccessor httpContextAccessor) : IApplicationActor
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public bool IsStaff => IsAuthenticated && User?.IsInRole(StaffAuthorization.StaffRole) == true;

    public string? SubjectId
    {
        get
        {
            if (!IsAuthenticated ||
                !Guid.TryParse(User?.FindFirst("tid")?.Value, out var tenantId) ||
                !Guid.TryParse(User?.FindFirst("oid")?.Value, out var objectId))
            {
                return null;
            }

            return $"{tenantId:D}:{objectId:D}";
        }
    }
}
