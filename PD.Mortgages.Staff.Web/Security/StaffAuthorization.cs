using System.Security.Claims;

namespace PD.Mortgages.Staff.Web.Security;

public static class StaffAuthorization
{
    public const string StaffRole = "MortgageStaff";
    public const string TenantIdClaim = "http://schemas.microsoft.com/identity/claims/tenantid";
    public const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    public const string RoleClaimType = ClaimTypes.Role;
}
